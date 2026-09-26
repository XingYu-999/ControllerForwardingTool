using System.Diagnostics;
using System.Globalization;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.VirtualDevice;

public sealed class UsbIpClient
{
    private readonly UsbIpDriverInstaller installer = new();
    public async Task<int> AttachAsync(int tcpPort, string busId, CancellationToken token)
    {
        string text = await RunAsync(["--tcp-port", tcpPort.ToString(CultureInfo.InvariantCulture),
            "attach", "--remote", "127.0.0.1", "--bus-id", busId, "--terse", "--once"], token);
        if (!int.TryParse(text.Trim(), out int port) || port < 0)
            throw new IOException($"USB/IP 未返回可确认的挂载端口：{text}");
        return port;
    }

    public Task<string> DetachAsync(int port, CancellationToken token) =>
        RunAsync(["detach", "--port", port.ToString(CultureInfo.InvariantCulture)], token);

    private async Task<string> RunAsync(string[] args, CancellationToken token)
    {
        string exe = installer.FindClient() ?? throw new FileNotFoundException("请先安装 / 修复 USB/IP 驱动");
        ProcessStartInfo info = new(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppDataPaths.EnsureRuntimeDirectory() };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process process = Process.Start(info) ?? throw new IOException("无法启动 usbip.exe");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        string stdout = await output, stderr = await error;
        if (process.ExitCode != 0) throw new IOException($"USB/IP 返回 {process.ExitCode}：{stderr} {stdout}");
        return stdout;
    }
}
