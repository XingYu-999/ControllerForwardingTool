using System.Text;

namespace ControllerForwardingTool.Core;

// Stage/error logging only (never the per-frame input path). Each write is flushed
// so a crash or forced exit retains the last diagnostic. No install-directory fallback.
internal sealed class ApplicationLog(string directory, long maxBytes = 5 * 1024 * 1024, int retainedFiles = 10) : IDisposable
{
    internal static ApplicationLog Current { get; } = new(AppDataPaths.LogDirectoryPath);
    private readonly object gate = new();
    private string? currentFile;
    private bool disposed;
    public string? LastError { get; private set; }

    public bool Write(string area, string message)
    {
        lock (gate)
        {
            if (disposed) return false;
            try
            {
                string line = $"{DateTimeOffset.Now:O} [{area}] {message}";
                if (line.Length > 32768) line = line[..32768] + " [truncated]";
                bool newFile = currentFile is null || !File.Exists(currentFile) ||
                    new FileInfo(currentFile).Length + Encoding.UTF8.GetByteCount(line) + 2 > maxBytes;
                if (newFile)
                {
                    Directory.CreateDirectory(directory);
                    currentFile = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
                }
                // Close between stage messages so ordinary editors can open the log
                // while the app is running (many Windows readers deny shared writes).
                using (var stream = new FileStream(currentFile!, newFile ? FileMode.CreateNew : FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.WriteLine(line);
                if (newFile) PruneLogs(currentFile!);
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
                currentFile = null;
                return false; // Logging failure must not break controller input.
            }
        }
    }

    private void PruneLogs(string currentPath)
    {
        // Only our generated app logs; exports and user files are never pruned.
        try
        {
            foreach (var file in new DirectoryInfo(directory).GetFiles("app-*.log")
                .Where(x => !x.FullName.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastWriteTimeUtc).Skip(Math.Max(0, retainedFiles - 1)))
            {
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
    }
}
