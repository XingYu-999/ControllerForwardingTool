using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public static class StickMath
{
    public static (double X, double Y) Filter(double x, double y, double threshold, bool radial)
    {
        threshold = double.IsFinite(threshold) ? Math.Clamp(threshold, 0, .3) : 0;
        if (threshold == 0) return (x, y);
        if (radial)
        {
            double length = Math.Sqrt(x * x + y * y);
            if (length <= threshold || length == 0) return (0, 0);
            double scale = Math.Min(1, (length - threshold) / (1 - threshold)) / length;
            return (x * scale, y * scale);
        }
        double Axis(double n) => Math.Abs(n) <= threshold ? 0 : Math.Sign(n) * Math.Min(1, (Math.Abs(n) - threshold) / (1 - threshold));
        return (Axis(x), Axis(y));
    }
    public static ControllerState Apply(ControllerState state, StickProfile? profile, BridgeOptions options)
    {
        ushort[] axes = [state.LeftX, state.LeftY, state.RightX, state.RightY];
        if (options.UseStickCalibration && profile is { Valid: true })
            for (int i = 0; i < 4; i++) axes[i] = profile.Map(axes[i], i);
        if (options.StickDeadzone > 0)
            for (int i = 0; i < 4; i += 2)
            {
                var (x, y) = Filter(Normalize(axes[i]), Normalize(axes[i + 1]), options.StickDeadzone, options.RadialDeadzone);
                axes[i] = Raw(x); axes[i + 1] = Raw(y);
            }
        return state with { LeftX = axes[0], LeftY = axes[1], RightX = axes[2], RightY = axes[3] };
    }
    private static double Normalize(ushort v) => (v - 2048) / (v >= 2048 ? 2047.0 : 2048);
    private static ushort Raw(double v) => (ushort)Math.Clamp(Math.Round(2048 + v * (v >= 0 ? 2047 : 2048)), 0, 4095);
}

public sealed class StickTrace
{
    private readonly double[] sectors = new double[72];
    public double[] Snapshot => sectors.ToArray();
    public int Coverage => sectors.Count(x => x > 0);
    public string Summary => Coverage < 65 ? $"覆盖 {Coverage * 100 / 72}% · 请沿边缘转一圈" : $"平均圆度偏差 {sectors.Where(x => x > 0).Average(x => Math.Abs(1 - x)):P1}";
    public void Observe(double x, double y)
    {
        double radius = Math.Sqrt(x * x + y * y);
        if (radius < .5) return;
        int bucket = (int)Math.Floor((Math.Atan2(y, x) + Math.PI) / (2 * Math.PI) * 72) % 72;
        sectors[bucket] = Math.Max(sectors[bucket], Math.Min(radius, 1.5));
    }
    public void Reset() => Array.Clear(sectors);
}

/// <summary>Fresh BLE reports only. Upstream's 2 s center / 8 s range workflow,
/// with a stationary-center gate and a directional-range validation before persistence.</summary>
public sealed class StickCalibration
{
    private readonly object gate = new();
    private readonly List<ushort[]> samples = [];
    private DateTimeOffset started, last;
    private bool range;
    private StickProfile? baseline;
    public bool Running { get; private set; }
    public string Status { get; private set; } = "先松开双摇杆采集中心，再沿边缘转动双摇杆采集行程。";
    public void Start(bool fullRange, StickProfile? current)
    {
        lock (gate)
        {
            if (fullRange && current is not { Valid: true }) { Status = "请先完成中心校准"; return; }
            samples.Clear(); range = fullRange; baseline = current;
            started = last = DateTimeOffset.Now; Running = true;
            Status = fullRange ? "采集中：连续转动两个摇杆到所有边缘 · 8 秒" : "采集中：松开双摇杆，保持静止 · 2 秒";
        }
    }
    public StickProfile? Observe(ControllerState state)
    {
        lock (gate)
        {
            if (!Running) return null;
            var now = DateTimeOffset.Now;
            if (now - last > TimeSpan.FromMilliseconds(500)) { Cancel("采样中断，保留原有校准"); return null; }
            last = now;
            if (samples.Count < 10000) samples.Add([state.LeftX, state.LeftY, state.RightX, state.RightY]);
            if (now - started < TimeSpan.FromSeconds(range ? 8 : 2)) return null;
            Running = false;
            if (samples.Count < (range ? 150 : 60)) { Status = "有效样本不足，保留原有校准"; return null; }
            var center = range ? baseline!.Centers : Enumerable.Range(0, 4).Select(i => (ushort)Math.Round(samples.Average(s => s[i]))).ToArray();
            var min = Enumerable.Range(0, 4).Select(i => range ? samples.Min(s => s[i]) : (ushort)Math.Max(0, center[i] - 1600)).ToArray();
            var max = Enumerable.Range(0, 4).Select(i => range ? samples.Max(s => s[i]) : (ushort)Math.Min(4095, center[i] + 1600)).ToArray();
            var profile = new StickProfile(center, min, max);
            if (!profile.Valid || (!range && Enumerable.Range(0, 4).Any(i => samples.Max(s => s[i]) - samples.Min(s => s[i]) > 80)))
            { Status = range ? "行程未覆盖所有方向，保留原有校准" : "摇杆未静止或偏离中心，保留原有校准"; return null; }
            Status = $"{(range ? "完整行程" : "中心")}校准完成 · {samples.Count} 个样本 · 已按设备保存";
            return profile;
        }
    }
    public void Cancel(string reason) { lock (gate) { Running = false; Status = reason; samples.Clear(); } }
    public string ReadStatus()
    {
        lock (gate)
        {
            if (Running && DateTimeOffset.Now - last > TimeSpan.FromMilliseconds(500)) Cancel("输入超时，保留原有校准");
            return Status;
        }
    }
}
