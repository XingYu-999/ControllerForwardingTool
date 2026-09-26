using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControllerForwardingTool.Core;

/// <summary>Each application directory has its own activation channel.</summary>
internal sealed class SingleInstanceService : IDisposable
{
    private const string Ack = "ACK";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task listener;

    private SingleInstanceService(string directory, NamedPipeServerStream? server, Action activate)
    {
        listener = ListenAsync(directory, server, activate, stopping.Token);
    }

    // Reserve the channel before starting Avalonia so simultaneous launches cannot
    // both conclude that they are the first instance. Only an ACK suppresses startup.
    public static SingleInstanceService? StartOrActivate(Action activate, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(activate);
        directory = NormalizeDirectory(directory ?? AppContext.BaseDirectory);
        NamedPipeServerStream? server = null;
        try { server = CreateServer(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (TryActivateAsync(directory).GetAwaiter().GetResult()) return null;
            Trace.TraceWarning($"Instance activation unavailable; continuing startup: {ex.Message}");
        }
        return new SingleInstanceService(directory, server, activate);
    }

    internal static string NormalizeDirectory(string directory)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    internal static string GetPipeName(string directory)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeDirectory(directory))));
        return "ControllerForwardingTool_Activation_" + hash;
    }

    private static NamedPipeServerStream CreateServer(string directory) => new(
        GetPipeName(directory), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task<bool> TryActivateAsync(string directory)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var client = new NamedPipeClientStream(".", GetPipeName(directory),
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);

            // A newly launched foreground process can pass its foreground permission
            // to the existing process before asking it to activate its window.
            if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(client.SafePipeHandle, out uint processId))
                AllowSetForegroundWindow(processId);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true);
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(("ACTIVATE|" + directory).AsMemory(), timeout.Token).ConfigureAwait(false);
            await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == Ack;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task ListenAsync(string directory, NamedPipeServerStream? server,
        Action activate, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    server ??= CreateServer(directory);
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);
                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true);
                    string? request = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    bool accepted = request == "ACTIVATE|" + directory;
                    // The callback records/queues activation even before the UI is ready.
                    // Confirm only after that request has been accepted.
                    if (accepted) activate();
                    await writer.WriteLineAsync((accepted ? Ack : "REJECT").AsMemory(), timeout.Token).ConfigureAwait(false);
                    await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    // Slow/disconnected clients must not terminate the activation listener.
                }
                finally
                {
                    // Keep the same server handle between requests, retaining ownership
                    // of the directory's channel even while no client is connected.
                    try { if (server?.IsConnected == true) server.Disconnect(); }
                    catch (IOException)
                    {
                        server?.Dispose();
                        server = null;
                    }
                }
                if (server is null)
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { server?.Dispose(); }
    }

    public void Dispose()
    {
        stopping.Cancel();
        try { listener.GetAwaiter().GetResult(); }
        finally { stopping.Dispose(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
