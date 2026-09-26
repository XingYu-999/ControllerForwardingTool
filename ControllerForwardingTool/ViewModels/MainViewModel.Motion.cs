using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial Quaternion TesterOrientation { get; set; } = Quaternion.Identity;
    [ObservableProperty] public partial double GyroPitchDps { get; set; }
    [ObservableProperty] public partial double GyroYawDps { get; set; }
    [ObservableProperty] public partial double GyroRollDps { get; set; }
    [ObservableProperty] public partial bool AutoGyroCalibration { get; set; } = true;
    [ObservableProperty] public partial bool SmoothGyroMotion { get; set; }
    [ObservableProperty] public partial double GyroAngularThreshold { get; set; } = 1.5;
    [ObservableProperty] public partial double GyroAccelerationThreshold { get; set; } = .035;
    [ObservableProperty] public partial double GyroStationarySeconds { get; set; } = 3;
    [ObservableProperty] public partial double AutoGyroProgress { get; set; }
    [ObservableProperty] public partial double GyroAngularActivity { get; set; }
    [ObservableProperty] public partial double GyroAccelerationActivity { get; set; }
    [ObservableProperty] public partial string GyroAutoStatus { get; set; } = "等待传感器输入";
    [ObservableProperty] public partial string GyroStationaryStatus { get; set; } = "无传感器数据";
    [ObservableProperty] public partial string GyroAngularActivityText { get; set; } = "—";
    [ObservableProperty] public partial string GyroAccelerationActivityText { get; set; } = "—";
    [ObservableProperty] public partial string GyroManualProgressText { get; set; } = "平放在稳定的桌面上，然后开始校准。";
    [ObservableProperty] public partial string GyroSettingsResult { get; set; } = "参数修改立即生效；保存后下次启动沿用。";
    public bool IsGyroCalibration => SelectedPage == "陀螺仪校准";
    public bool IsTesterSection => IsTester || IsGyroCalibration;
    private bool loadingMotionSettings;

    private void InitializeMotionSettings()
    {
        loadingMotionSettings = true;
        var settings = bridgeOptions.Motion;
        AutoGyroCalibration = settings.AutoCalibrate; SmoothGyroMotion = settings.SmoothSmallMotion;
        GyroAngularThreshold = settings.AngularThreshold; GyroAccelerationThreshold = settings.AccelerationThreshold;
        GyroStationarySeconds = settings.StationarySeconds;
        loadingMotionSettings = false; ApplyMotionSettings();
    }
    private GyroOptions CurrentMotionOptions() => new GyroOptions {
        AutoCalibrate = AutoGyroCalibration, SmoothSmallMotion = SmoothGyroMotion,
        AngularThreshold = GyroAngularThreshold, AccelerationThreshold = GyroAccelerationThreshold,
        StationarySeconds = GyroStationarySeconds
    }.Normalize();
    private void ApplyMotionSettings()
    {
        if (loadingMotionSettings) return;
        var options = CurrentMotionOptions();
        bleCalibration.Configure(options); gamepads.ConfigureMotion(options);
        GyroSettingsResult = "参数已生效 · 点击保存后下次启动沿用";
    }
    partial void OnAutoGyroCalibrationChanged(bool value) => ApplyMotionSettings();
    partial void OnSmoothGyroMotionChanged(bool value) => ApplyMotionSettings();
    partial void OnGyroAngularThresholdChanged(double value) => ApplyMotionSettings();
    partial void OnGyroAccelerationThresholdChanged(double value) => ApplyMotionSettings();
    partial void OnGyroStationarySecondsChanged(double value) => ApplyMotionSettings();
    [RelayCommand] private void SaveGyroSettings()
    {
        try
        {
            var next = bridgeOptions with { Motion = CurrentMotionOptions() };
            next.Save(); bridgeOptions = next; GyroSettingsResult = "陀螺仪参数已保存";
        }
        catch (Exception ex) { GyroSettingsResult = $"保存失败：{ex.Message}"; }
    }
    [RelayCommand] private void RecenterMotion() => CurrentCalibration?.RecenterOrientation();
    private void UpdateMotionPanel(MotionReading? reading, bool available)
    {
        TesterOrientation = available ? reading!.Orientation : Quaternion.Identity;
        GyroPitchDps = available ? reading!.FilteredGyro.X : 0;
        GyroYawDps = available ? reading!.FilteredGyro.Y : 0;
        GyroRollDps = available ? reading!.FilteredGyro.Z : 0;
        AutoGyroProgress = available ? reading!.AutoProgress : 0;
        GyroAutoStatus = available ? reading!.AutoStatus : "等待有效传感器输入";
        GyroStationaryStatus = !available ? "无传感器数据" : reading!.Stationary ? "当前静止" : "正在移动 / 判定中";
        GyroAngularActivity = available ? reading!.AngularMotion / GyroAngularThreshold : 0;
        GyroAccelerationActivity = available ? reading!.AccelerationMotion / GyroAccelerationThreshold : 0;
        GyroAngularActivityText = available ? $"当前 {reading!.AngularMotion:F2} °/s" : "—";
        GyroAccelerationActivityText = available ? $"当前 {reading!.AccelerationMotion:F3} g" : "—";
        GyroManualProgressText = IsCalibrating
            ? $"保持静止 · 还需 {(100 - CalibrationProgress) * .03:F1} 秒 · {reading!.Samples} 个有效样本"
            : "平放在稳定的桌面上，然后开始校准。";
    }
}
