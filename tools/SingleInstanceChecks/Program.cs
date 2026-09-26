using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ControllerForwardingTool.Core;

if (args.Length > 0)
{
    if (args[0] == "race")
    {
        Console.WriteLine("ARMED");
        Console.ReadLine();
    }
    using var instance = SingleInstanceService.StartOrActivate(() => Console.WriteLine("ACTIVATED"), args[1]);
    if (args[0] == "client") return instance is null ? 0 : 2;
    Console.WriteLine(instance is null ? "REDIRECTED" : "READY");
    if (instance is not null) Console.ReadLine();
    return 0;
}

string directory = Path.Combine(Path.GetTempPath(), "实例 checks " + Guid.NewGuid().ToString("N"));
Check(SingleInstanceService.GetPipeName(directory) ==
    SingleInstanceService.GetPipeName(directory.ToUpperInvariant() + Path.DirectorySeparatorChar),
    "Directory matching ignores Windows case and trailing separators");
using var first = Start("server", directory);
using var second = Start("server", directory + "-other");
try
{
    Check(await Read(first) == "READY", "First instance starts");
    Check(await Read(second) == "READY", "Different application directory starts independently");
    for (int i = 0; i < 3; i++)
    {
        using var duplicate = Start("client", directory.ToUpperInvariant() + Path.DirectorySeparatorChar);
        await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Check(duplicate.ExitCode == 0 && await Read(first) == "ACTIVATED",
            "Same-directory process receives ACK and exits normally");
    }
    using (var duplicate = Start("client", directory + "-other"))
    {
        await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Check(duplicate.ExitCode == 0 && await Read(second) == "ACTIVATED",
            "Activation reaches the correct directory while both copies run");
    }
    using (var invalid = await Connect(directory))
    {
        using var writer = new StreamWriter(invalid, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(invalid, leaveOpen: true);
        await writer.WriteLineAsync("ACTIVATE|" + directory + "-wrong");
        Check(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "REJECT",
            "Wrong-directory activation is rejected");
    }
    using (var stalled = await Connect(directory)) await Task.Delay(2300);
    using (var duplicate = Start("client", directory))
    {
        await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Check(duplicate.ExitCode == 0 && await Read(first) == "ACTIVATED",
            "Listener recovers after a stalled client");
    }
}
finally
{
    await Stop(first);
    await Stop(second);
}

using (var restarted = Start("server", directory))
{
    try { Check(await Read(restarted) == "READY", "Shutdown releases the channel for restart"); }
    finally { await Stop(restarted); }
}

using (var racerA = Start("race", directory + "-race"))
using (var racerB = Start("race", directory + "-race"))
{
    try
    {
        Check(await Read(racerA) == "ARMED" && await Read(racerB) == "ARMED", "Concurrent launches prepared");
        await Task.WhenAll(racerA.StandardInput.WriteLineAsync("go"), racerB.StandardInput.WriteLineAsync("go"));
        await Task.WhenAll(racerA.StandardInput.FlushAsync(), racerB.StandardInput.FlushAsync());
        string?[] results = await Task.WhenAll(ReadStartup(racerA), ReadStartup(racerB));
        Check(results.Count(x => x == "READY") == 1 && results.Count(x => x == "REDIRECTED") == 1,
            "Simultaneous launches keep exactly one instance");
    }
    finally { await Stop(racerA); await Stop(racerB); }
}

// A reachable process without the expected ACK must never suppress startup.
foreach (string? reply in new string?[] { "REJECT", null })
{
    string failedDirectory = directory + "-failure-" + (reply ?? "timeout");
    using var pipe = new NamedPipeServerStream(SingleInstanceService.GetPipeName(failedDirectory),
        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    using var client = Start("client", failedDirectory);
    await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var reader = new StreamReader(pipe, leaveOpen: true);
    await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
    if (reply is not null)
    {
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(reply);
    }
    await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
    Check(client.ExitCode == 2, $"{reply ?? "No reply"} continues normal startup instead of exiting");
}
return 0;

static Process Start(string mode, string directory)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true,
    };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
        start.ArgumentList.Add(typeof(SingleInstanceService).Assembly.Location);
    start.ArgumentList.Add(mode);
    start.ArgumentList.Add(directory);
    return Process.Start(start)!;
}

static Task<string?> Read(Process process) => process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));

static async Task<string?> ReadStartup(Process process)
{
    string? line;
    do { line = await Read(process); } while (line == "ACTIVATED");
    return line;
}

static async Task Stop(Process process)
{
    if (process.HasExited) return;
    await process.StandardInput.WriteLineAsync("exit");
    await process.StandardInput.FlushAsync();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (TimeoutException) { process.Kill(); throw; }
    Check(process.ExitCode == 0, "Listener shuts down cleanly");
}

static async Task<NamedPipeClientStream> Connect(string directory)
{
    var client = new NamedPipeClientStream(".", SingleInstanceService.GetPipeName(directory),
        PipeDirection.InOut, PipeOptions.Asynchronous);
    await client.ConnectAsync(3000);
    return client;
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
}
