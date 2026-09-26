using System.Diagnostics;
using System.Numerics;

namespace ControllerForwardingTool.Input;

public readonly record struct MotionSample(double Timestamp, Vector3 AccelG, Vector3 GyroDps);
public sealed record MotionReading(bool Available, Vector3 AccelG, Vector3 RawGyro, Vector3 CorrectedGyro,
    Vector3 Bias, bool Calibrated, bool Collecting, int Samples, double Progress, string Status,
    Vector3 FilteredGyro, Quaternion Orientation, bool Stationary, double AngularMotion,
    double AccelerationMotion, double AutoProgress, string AutoStatus, double Timestamp = 0);

/// <summary>Fresh sensor samples drive calibration and orientation on the input thread.
/// All vectors use SDL coordinates, g and degrees/second. UI polling never creates samples.</summary>
public sealed class GyroCalibration
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private readonly object gate = new();
    private readonly MotionOrientation orientation = new();
    private GyroOptions options = new();
    private MotionSample? latest;
    private Vector3 bias, meanGyro, gyroM2, manualAccelAnchor, previousGyro, filtered;
    private double firstTimestamp, lastTimestamp, lastArrival, startedAt, accelSum, accelSquares, maxStep;
    private bool collecting, calibrated, filterInitialized;
    private int count;
    private string status = "未校准 · 将手柄平放后采样 3 秒";
    private Vector3 autoMean, autoM2, accelAnchor;
    private double autoStarted, autoProgress, angularMotion, accelerationMotion;
    private int autoCount;
    private bool stationary, autoComplete;
    private string autoStatus = "等待新传感器数据";

    public void Configure(GyroOptions value)
    {
        lock (gate)
        {
            if (options == value) return;
            value = value.Normalize();
            if (options == value) return;
            options = value; filterInitialized = false;
            ResetAuto(value.AutoCalibrate ? "参数已更新，等待静止" : "自动校准已关闭");
        }
    }
    public void Start(double now)
    {
        lock (gate)
        {
            collecting = true; count = 0; firstTimestamp = lastTimestamp = double.NaN;
            meanGyro = gyroM2 = previousGyro = Vector3.Zero; accelSum = accelSquares = maxStep = 0;
            startedAt = now; status = "采集中 · 请平放手柄并保持静止";
            ResetAuto("手动校准进行中");
        }
    }
    public void Cancel(string reason = "校准已取消")
    {
        lock (gate) { if (collecting) Reject(reason); ResetAuto(reason); }
    }
    public void RecenterOrientation() { lock (gate) orientation.Reset(); }
    public void Reset()
    {
        lock (gate)
        {
            collecting = calibrated = false; bias = Vector3.Zero; count = 0;
            filterInitialized = false; filtered = latest?.GyroDps ?? Vector3.Zero;
            status = "零偏已重置"; ResetAuto("等待重新静置"); orientation.Reset();
        }
    }
    public void Observe(MotionSample sample, double now)
    {
        lock (gate)
        {
            if (!Finite(sample.AccelG) || !Finite(sample.GyroDps) || !double.IsFinite(sample.Timestamp) || !double.IsFinite(now)) return;
            if (latest is { } old && sample.Timestamp <= old.Timestamp) return;
            double dt = latest is { } previous ? sample.Timestamp - previous.Timestamp : double.NaN;
            if (now - sample.Timestamp > .15 || sample.Timestamp - now > .01)
            {
                if (collecting) Reject("校准失败：传感器数据延迟超过 150 ms");
                ResetAuto("数据延迟，等待新样本"); return;
            }
            if (dt > .25)
            {
                if (collecting && count > 0) Reject("校准失败：输入中断");
                ResetAuto("输入中断，重新等待静止"); filterInitialized = false;
            }
            latest = sample; lastArrival = now;
            bool wasCollecting = collecting;
            if (collecting) ObserveManual(sample);
            if (!wasCollecting) ObserveAutomatic(sample);
            else { stationary = false; autoStatus = "手动校准进行中"; }
            Vector3 corrected = sample.GyroDps - bias;
            // Only small motion is smoothed. Fast deliberate turns pass through without filter lag.
            if (!filterInitialized || !options.SmoothSmallMotion || corrected.Length() >= 8 || !double.IsFinite(dt))
                filtered = corrected;
            else
                filtered = Vector3.Lerp(filtered, corrected, (float)(1 - Math.Exp(-dt / .025)));
            filterInitialized = true;
            orientation.Observe(sample.Timestamp, sample.AccelG, filtered);
        }
    }
    private void ObserveManual(MotionSample sample)
    {
        double norm = sample.AccelG.Length();
        if (norm is < .85 or > 1.15) { Reject("校准失败：重力读数不稳定，请平放手柄"); return; }
        if (count == 0) { firstTimestamp = sample.Timestamp; manualAccelAnchor = sample.AccelG; }
        else maxStep = Math.Max(maxStep, Vector3.Distance(sample.GyroDps, previousGyro));
        if (Vector3.Distance(sample.AccelG, manualAccelAnchor) > .035)
        { Reject("校准失败：检测到倾斜或移动，请放稳后重试"); return; }
        previousGyro = sample.GyroDps; lastTimestamp = sample.Timestamp; count++;
        // Welford avoids cancellation in E[x²]-E[x]² with a large constant offset.
        Accumulate(sample.GyroDps, count, ref meanGyro, ref gyroM2);
        accelSum += norm; accelSquares += norm * norm;
        if (lastTimestamp - firstTimestamp < 3) return;
        Vector3 variance = Vector3.Max(Vector3.Zero, gyroM2 / count);
        double accelMean = accelSum / count;
        if (count < 150) { Reject("校准失败：3 秒内有效样本不足 150 帧"); return; }
        if (Math.Sqrt(Math.Max(0, accelSquares / count - accelMean * accelMean)) >= .02 ||
            MaxStd(variance) >= 3.0 / 16.384 || maxStep >= 40.0 / 16.384)
        { Reject("校准失败：检测到移动，请放稳后重试"); return; }
        // Manual calibration is an explicit request with the controller resting on a table.
        // A stable large bias is correctable; its absolute magnitude is not a motion detector.
        bias = meanGyro; calibrated = true; collecting = false; filterInitialized = false;
        status = $"校准完成 · {count} 个有效样本 · 已应用零偏";
    }
    private void ObserveAutomatic(MotionSample sample)
    {
        if (autoCount == 0) { autoStarted = sample.Timestamp; accelAnchor = sample.AccelG; }
        angularMotion = (sample.GyroDps - bias).Length();
        accelerationMotion = Vector3.Distance(sample.AccelG, accelAnchor);
        stationary = sample.AccelG.Length() is >= .85f and <= 1.15f &&
            angularMotion <= options.AngularThreshold && accelerationMotion <= options.AccelerationThreshold;
        if (!options.AutoCalibrate) { ResetAuto("自动校准已关闭"); return; }
        if (!stationary) { ResetAuto("检测到移动 · 静置进度已重置"); return; }
        autoCount++;
        Accumulate(sample.GyroDps, autoCount, ref autoMean, ref autoM2);
        double elapsed = sample.Timestamp - autoStarted;
        autoProgress = autoComplete ? 100 : Math.Clamp(elapsed / options.StationarySeconds * 100, 0, 100);
        autoStatus = autoComplete ? "静置校准已完成 · 继续监测漂移" : $"保持静止 · 还需 {Math.Max(0, options.StationarySeconds - elapsed):F1} 秒";
        if (elapsed < options.StationarySeconds) return;
        Vector3 variance = Vector3.Max(Vector3.Zero, autoM2 / autoCount);
        if (autoCount < 100 || MaxStd(variance) > Math.Min(.18, options.AngularThreshold / 4))
        { ResetAuto("样本不足或不稳定 · 重新等待静止"); return; }
        bias = autoMean; calibrated = true; filterInitialized = false;
        status = $"自动校准完成 · {autoCount} 个有效样本 · 已应用零偏";
        autoComplete = true; autoProgress = 100; autoStatus = "静置校准已完成 · 继续监测漂移";
        autoCount = 0; autoMean = autoM2 = Vector3.Zero;
    }
    public MotionReading Read(double now)
    {
        lock (gate)
        {
            if (collecting && (now - Math.Max(startedAt, lastArrival) > .5 || now - startedAt > 5)) Reject("校准失败：输入中断或采样超时");
            var frame = latest.GetValueOrDefault();
            bool available = latest.HasValue && now - lastArrival <= .25 && now - frame.Timestamp <= .25;
            if (!available) { ResetAuto("等待新传感器数据"); stationary = false; }
            double progress = count == 0 ? 0 : Math.Clamp((lastTimestamp - firstTimestamp) / 3 * 100, 0, 100);
            return new(available, frame.AccelG, frame.GyroDps, frame.GyroDps - bias, bias, calibrated, collecting,
                count, progress, status, filtered, orientation.Value, stationary, angularMotion, accelerationMotion, autoProgress, autoStatus, frame.Timestamp);
        }
    }
    private void ResetAuto(string reason)
    {
        autoCount = 0; autoProgress = 0; autoComplete = false;
        autoMean = autoM2 = Vector3.Zero; autoStatus = reason;
    }
    private static void Accumulate(Vector3 value, int samples, ref Vector3 mean, ref Vector3 m2)
    {
        Vector3 delta = value - mean;
        mean += delta / samples;
        m2 += delta * (value - mean);
    }
    private void Reject(string reason) { collecting = false; status = reason + (calibrated ? "；保留上次零偏" : ""); }
    private static double MaxStd(Vector3 variance) => Math.Sqrt(Math.Max(variance.X, Math.Max(variance.Y, variance.Z)));
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
