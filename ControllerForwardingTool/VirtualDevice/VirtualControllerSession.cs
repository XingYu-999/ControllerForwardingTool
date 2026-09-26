using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.VirtualDevice;

/// <summary>Owns one local backend, one USB/IP attachment and its bounded input stream.</summary>
public sealed class VirtualControllerSession : IAsyncDisposable
{
    private readonly UsbIpClient client = new();
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private UsbIpPrototypeServer? ns1;
    private Process? backend;
    private ViiperProtocolClient? api;
    private ViiperDeviceStream? stream;
    private CancellationTokenSource? lifetime;
    private Task? writer, reader;
    private int? attachedPort;
    private bool stopping;
    private ControllerState latest = ControllerState.Neutral(DateTimeOffset.Now);
    private BridgeOptions options = new();
    private long sent, received, revision, sentRevision;
    private string endpoints = "未启动", feedbackState = "尚无主机反馈";
    private string? fault;
    private DualSenseHapticRumbleScheduler haptics = new();
    private readonly OutputWakeSignal inputChanged = new();
    public BridgeOptions Options { get => Volatile.Read(ref options); set { Volatile.Write(ref options, value.Normalize()); inputChanged.Signal(); ns1?.NotifySettingsChanged(); } }
    public long Sent => Interlocked.Read(ref sent);
    public long FeedbackCount => Interlocked.Read(ref received);
    public string Endpoints => endpoints;
    public string FeedbackState => Volatile.Read(ref feedbackState);
    public bool Live => IsRunning && DateTimeOffset.Now - Volatile.Read(ref latest).ReceivedAt < TimeSpan.FromMilliseconds(250) && Interlocked.Read(ref revision) > 0;
    public bool IsRunning => lifetime is { IsCancellationRequested: false };
    public VirtualControllerMode Mode { get; private set; }
    public event Action<string>? Status;
    public event Action<byte[]>? OutputReceived;
    public event Action<Pro2OutputPacket>? RumbleReceived;
    public event Action<string>? Faulted;
    public async Task<string> PingAsync(CancellationToken token) => api is not null
        ? "VIIPER 响应：" + await api.PingAsync(token)
        : ns1 is not null ? "NS1 本地 USB/IP 服务正在运行" : "请先启动虚拟输出";

    public Task StartAsync(VirtualControllerMode mode, CancellationToken token) => StartCoreAsync(mode, token, true);
    internal Task StartBackendOnlyAsync(VirtualControllerMode mode, CancellationToken token) => StartCoreAsync(mode, token, false);
    private async Task StartCoreAsync(VirtualControllerMode mode, CancellationToken token, bool attachWindows)
    {
        await lifecycle.WaitAsync(token);
        try
        {
            await StopCoreAsync();
            stopping = false;
            fault = null;
            Mode = mode;
            var profile = VirtualProfile.Get(mode);
            latest = ControllerState.Neutral(DateTimeOffset.MinValue);
            sent = received = revision = sentRevision = 0;
            haptics = new(); feedbackState = "尚无主机反馈";
            Status?.Invoke($"准备 {mode} 虚拟输出");
            lifetime = new CancellationTokenSource();
            int usbPort = Options.UsbPort == 0 ? FreePort() : Options.UsbPort;
            endpoints = $"USB/IP 127.0.0.1:{usbPort}";
            string busId;
            if (mode == VirtualControllerMode.Ns1Pro)
            {
                ns1 = new UsbIpPrototypeServer(usbPort, () => Options.PushHz);
                ns1.Event += message => Status?.Invoke(message);
                ns1.OutputReceived += ProcessFeedback;
                ns1.InputReportSent += () => Interlocked.Increment(ref sent);
                ns1.Start();
                busId = UsbIpNs1Device.BusId;
            }
            else
            {
                int apiPort;
                do { apiPort = Options.ApiPort == 0 ? FreePort() : Options.ApiPort; } while (apiPort == usbPort && Options.ApiPort == 0);
                if (apiPort == usbPort) throw new InvalidOperationException("API 与 USB/IP 端口不能相同");
                string exe = Path.Combine(AppContext.BaseDirectory, "drivers", "viiper", "viiper-haptic.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("缺少随附 VIIPER 后端，请重新完整发布应用", exe);
                await using (var file = File.OpenRead(exe))
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(
                        "F153400F095817AF5056A6658A6EBD93A46F533F0BCD5E5DB3E59B0731278727", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("VIIPER 后端校验不匹配");
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = AppDataPaths.EnsureRuntimeDirectory() };
                foreach (string arg in new[] { "server", $"--api.addr=127.0.0.1:{apiPort}",
                    $"--usb.addr=127.0.0.1:{usbPort}", "--update-notify=none",
                    "--api.device-handler-connect-timeout=60s", "--api.auto-attach-local-client=false" }) info.ArgumentList.Add(arg);
                backend = Process.Start(info) ?? throw new IOException("无法启动 VIIPER");
                backend.OutputDataReceived += (_, _) => { };
                backend.ErrorDataReceived += (_, args) => { if (args.Data?.Contains("ERROR", StringComparison.OrdinalIgnoreCase) == true) Status?.Invoke(args.Data); };
                backend.BeginOutputReadLine(); backend.BeginErrorReadLine();
                api = new ViiperProtocolClient("127.0.0.1", apiPort);
                endpoints = $"API 127.0.0.1:{apiPort} · USB/IP 127.0.0.1:{usbPort}";
                bool ready = false;
                for (int i = 0; i < 40; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (backend.HasExited) throw new IOException($"VIIPER 启动失败：{backend.ExitCode}");
                    try { await api.PingAsync(token); ready = true; break; }
                    catch (SocketException) { await Task.Delay(100, token); }
                }
                if (!ready) throw new TimeoutException("VIIPER 启动超时");
                Status?.Invoke("VIIPER 已启动，正在创建 USB 总线");
                uint bus = await api.BusCreateAsync(token);
                Status?.Invoke($"正在创建 {profile.Name} 设备");
                var properties = new Dictionary<string, object?> { ["serial_number"] = $"NS2PROWIN11-{mode}" };
                if (mode == VirtualControllerMode.Ns2Pro) { properties["source_paced"] = true; properties["input_interval_ms"] = 4; }
                ViiperDevice device = await api.AddDeviceAsync(bus, profile.DeviceType, properties, token);
                if (!profile.Matches(device)) throw new IOException($"后端设备身份不匹配：{device.Type} {device.Vid}:{device.Pid}，应为 {profile.Protocol}");
                busId = $"{bus}-{device.DevId}";
                stream = await api.OpenStreamAsync(bus, device.DevId, token);
                endpoints += $" · 总线 {busId}";
                Status?.Invoke($"{profile.Name} 输入通道已打开，身份 {profile.Identity} 已校验");
                await stream.WriteAsync(VirtualReportEncoder.Encode(mode, ControllerState.Neutral(DateTimeOffset.Now), Options), token);
                writer = Task.Run(() => WriteLoopAsync(lifetime.Token));
                reader = Task.Run(() => ReadFeedbackAsync(lifetime.Token));
            }
            if (attachWindows)
            {
                Status?.Invoke("虚拟 USB 已创建，正在挂载到 Windows…");
                attachedPort = await client.AttachAsync(usbPort, busId, token);
                Status?.Invoke($"USB/IP 已挂载（端口 {attachedPort}）；等待 Windows 手柄枚举");
            }
            else Status?.Invoke("后端测试：已创建设备，未挂载 Windows");
            if (Volatile.Read(ref fault) is { } error) throw new IOException(error);
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
        finally { lifecycle.Release(); }
    }

    public void Publish(ControllerState state)
    {
        Volatile.Write(ref latest, state);
        Interlocked.Increment(ref revision);
        inputChanged.Signal();
        ns1?.Publish(state);
    }

    private async Task WriteLoopAsync(CancellationToken token)
    {
        try
        {
            // This pump never captures the Avalonia synchronization context. Latest state only:
            // no input backlog, and a lost BLE source becomes neutral within 250 ms.
            HighResolutionPeriodicTimer? clock = null;
            int previousHz = 0;
            bool sentNeutral = false;
            long lastSendAt = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var config = Options;
                    int hz = OutputRatePolicy.Resolve(config.PushHz, Mode);
                    if (hz != previousHz)
                    { clock?.Dispose(); clock = hz > 0 ? new(TimeSpan.FromSeconds(1.0 / hz)) : null; previousHz = hz; }
                    if (clock is not null) clock.WaitForNextTick(token);
                    long version = Interlocked.Read(ref revision);
                    var state = Volatile.Read(ref latest);
                    double ageMs = (DateTimeOffset.Now - state.ReceivedAt).TotalMilliseconds;
                    bool stale = ageMs >= 250;
                    if (hz == 0 && version == sentRevision && (!stale || sentNeutral && Stopwatch.GetElapsedTime(lastSendAt).TotalMilliseconds < 100))
                    {
                        await inputChanged.WaitAsync(TimeSpan.FromMilliseconds(stale ? 100 : Math.Clamp(250 - ageMs, 1, 250)), token);
                        continue;
                    }
                    if (stale) state = ControllerState.Neutral(DateTimeOffset.Now);
                    await stream!.WriteAsync(VirtualReportEncoder.Encode(Mode, state, config), token);
                    Interlocked.Increment(ref sent); sentRevision = version; sentNeutral = stale; lastSendAt = Stopwatch.GetTimestamp();
                }
            }
            finally { clock?.Dispose(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        { ReportFault($"虚拟输入通道中断：{ex.Message}"); }
    }

    private async Task ReadFeedbackAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                byte[] feedback = await stream!.ReadExactAsync(VirtualProfile.Get(Mode).FeedbackSize, token);
                ProcessFeedback(feedback);
            }
        }
        catch (Exception ex)
        { ReportFault($"虚拟反馈通道中断：{ex.Message}"); }
    }

    private void ProcessFeedback(byte[] feedback)
    {
        if (stopping) return;
        Interlocked.Increment(ref received);
        OutputReceived?.Invoke(feedback);
        Pro2OutputPacket packet;
        string reason;
        bool mapped = Mode == VirtualControllerMode.DualSense
            ? haptics.TryProcess(feedback, out packet, out _, out reason)
            : Pro2OutputPacketMapper.TryMapFeedback(Mode, feedback, out packet, out reason);
        Volatile.Write(ref feedbackState, mapped ? $"{packet.Source} · {(packet.Active ? "震动" : "停止")} · {feedback.Length} 字节" : $"{feedback.Length} 字节 · {reason}");
        if (mapped) RumbleReceived?.Invoke(packet);
    }

    private void ReportFault(string message)
    {
        if (stopping || Interlocked.CompareExchange(ref fault, message, null) is not null) return;
        lifetime?.Cancel(); Faulted?.Invoke(message);
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        stopping = true;
        RumbleReceived?.Invoke(Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "session-stop"));
        Publish(ControllerState.Neutral(DateTimeOffset.Now));
        if (attachedPort is int port)
        {
            try { await client.DetachAsync(port, CancellationToken.None); }
            catch (Exception ex) { Status?.Invoke($"卸载端口 {port} 失败：{ex.Message}；关闭本地后端以断开设备"); }
            attachedPort = null;
        }
        lifetime?.Cancel();
        if (stream is not null) await stream.DisposeAsync();
        if (writer is not null) await writer;
        if (reader is not null) await reader;
        if (ns1 is not null) await ns1.StopAsync();
        if (backend is not null)
        {
            if (!backend.HasExited) { backend.Kill(entireProcessTree: true); await backend.WaitForExitAsync(); }
            backend.Dispose();
        }
        lifetime?.Dispose();
        lifetime = null; stream = null; ns1 = null; backend = null; api = null; writer = reader = null;
        // All feedback producers have now ended. A callback that was already in
        // progress when shutdown began must not leave a latched motor state behind.
        RumbleReceived?.Invoke(Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "session-stop"));
        endpoints = "未启动";
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
