using System.Collections.Concurrent;

namespace ControllerForwardingTool.Input;

public sealed record GamepadFrame(IReadOnlyList<GamepadDevice> Devices, IReadOnlyDictionary<uint, GamepadSnapshot> Inputs,
    string Status, double At);

/// <summary>One SDL owning thread and immutable snapshots. No native calls or thread joins on the UI thread.</summary>
public sealed class GamepadMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly ConcurrentQueue<(Func<SdlGamepadService, string> Action, TaskCompletionSource<string> Result, double Deadline)> commands = new();
    private readonly ConcurrentDictionary<uint, GyroCalibration> calibrations = new();
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GamepadFrame latest = new([], new Dictionary<uint, GamepadSnapshot>(), "正在启动手柄输入服务…", 0);
    private bool ns2Enabled;
    private int refreshRequested;
    private GyroOptions motionOptions = new();
    private sealed record Feedback(GamepadDevice Device, RumbleSettings Settings);
    private Feedback? feedback;
    private string feedbackStatus = "等待游戏震动反馈";
    public string FeedbackStatus => Volatile.Read(ref feedbackStatus);
    public void QueueFeedback(GamepadDevice device, RumbleSettings settings) { Volatile.Write(ref feedback,new(device,settings)); wake.Set(); }
    public void ClearFeedback() { Volatile.Write(ref feedback,null); wake.Set(); }
    public GamepadMonitor()
    {
        new Thread(Run) { Name = "SDL controller input", IsBackground = true }.Start();
    }
    public GamepadFrame Latest => Volatile.Read(ref latest);
    public event Action<GamepadFrame>? FrameUpdated;
    public string Status => Latest.Status;
    public void EnableNs2Usb(bool enabled) { Volatile.Write(ref ns2Enabled, enabled); wake.Set(); }
    public void RequestRefresh() { Interlocked.Exchange(ref refreshRequested, 1); wake.Set(); }
    public GyroCalibration Calibration(uint id)
    {
        var calibration = calibrations.GetOrAdd(id, _ => new());
        calibration.Configure(Volatile.Read(ref motionOptions));
        return calibration;
    }
    public void ConfigureMotion(GyroOptions options)
    {
        Volatile.Write(ref motionOptions, options.Normalize());
        foreach (var calibration in calibrations.Values) calibration.Configure(motionOptions);
    }
    public GamepadSnapshot? Read(GamepadDevice device) => GyroCalibration.Now - Latest.At < .5 && Latest.Inputs.TryGetValue(device.Id, out var frame) ? frame : null;
    public Task<string> RumbleAsync(GamepadDevice? device, RumbleSettings settings) => Enqueue(s => s.Rumble(device, settings));
    public Task<string> StopRumbleAsync(GamepadDevice? device) => Enqueue(s => { s.StopRumble(device); return "震动已停止"; });
    private Task<string> Enqueue(Func<SdlGamepadService, string> action)
    {
        if (shutdown.IsCancellationRequested || finished.Task.IsCompleted) return Task.FromResult("输入服务已关闭");
        if (commands.Count >= 16) return Task.FromResult("输入服务忙，请稍后重试");
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue((action, result, GyroCalibration.Now + 3)); wake.Set();
        return result.Task.WaitAsync(TimeSpan.FromSeconds(4));
    }
    private void Run()
    {
        try
        {
            using var service = new SdlGamepadService(enableNs2Usb: false);
            service.MotionSampleReceived += (id, sample) => Calibration(id).Observe(sample, GyroCalibration.Now);
            bool enabled = false;
            IReadOnlyList<GamepadDevice> devices = [];
            double nextRefresh = 0;
            double nextRumble = 0;
            Feedback? appliedFeedback = null;
            while (!shutdown.IsCancellationRequested)
            {
                bool requested = Volatile.Read(ref ns2Enabled);
                if (requested != enabled) { service.EnableNs2Usb(requested); enabled = requested; nextRefresh = 0; }
                if (GyroCalibration.Now >= nextRefresh || Interlocked.Exchange(ref refreshRequested, 0) != 0)
                {
                    devices = service.Refresh(); nextRefresh = GyroCalibration.Now + 1;
                    foreach (uint id in calibrations.Keys.Except(devices.Select(d => d.Id))) calibrations.TryRemove(id, out _);
                }
                while (commands.TryDequeue(out var cmd))
                {
                    try { cmd.Result.TrySetResult(GyroCalibration.Now < cmd.Deadline ? cmd.Action(service) : "指令已过期，未发送震动"); }
                    catch (Exception ex) { cmd.Result.TrySetResult($"设备操作失败：{ex.Message}"); }
                }
                service.Update();
                Dictionary<uint, GamepadSnapshot> inputs = [];
                foreach (var device in devices) if (service.Read(device) is { } input) inputs[device.Id] = input;
                var desiredFeedback = Volatile.Read(ref feedback);
                if (desiredFeedback != appliedFeedback || GyroCalibration.Now >= nextRumble)
                {
                    if (appliedFeedback is { } old && (desiredFeedback is null || old.Device.Id != desiredFeedback.Device.Id || !inputs.ContainsKey(old.Device.Id)))
                        service.StopRumble(old.Device);
                    if (desiredFeedback is { } current && inputs.ContainsKey(current.Device.Id))
                        Volatile.Write(ref feedbackStatus,service.Rumble(current.Device,current.Settings));
                    appliedFeedback = desiredFeedback;
                    nextRumble = GyroCalibration.Now + .08;
                }
                Volatile.Write(ref latest, new(devices, inputs, service.Status, GyroCalibration.Now));
                FrameUpdated?.Invoke(Latest);
                wake.WaitOne(8);
            }
        }
        catch (Exception ex) { Volatile.Write(ref latest, new([], new Dictionary<uint, GamepadSnapshot>(), $"手柄输入服务停止：{ex.Message}", GyroCalibration.Now)); }
        finally
        {
            while (commands.TryDequeue(out var cmd)) cmd.Result.TrySetResult("输入服务已关闭");
            finished.TrySetResult();
        }
    }
    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel(); wake.Set();
        // A defective native device cannot indefinitely prevent closing the application.
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { }
    }
}
