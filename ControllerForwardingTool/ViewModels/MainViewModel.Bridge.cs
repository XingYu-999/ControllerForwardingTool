using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private BridgeOptions bridgeOptions = BridgeOptions.Load();
    private readonly StickCalibration stickCalibration = new();
    private StickProfile? activeStickProfile;
    private readonly StickTrace leftTrace = new(), rightTrace = new();
    private long outputBaseline;
    private DateTimeOffset diagnosticAt, audioGuardAt;
    private bool driverRemovable, driverReady;
    public IReadOnlyList<VirtualModeCard> ModeCards { get; private set; } = [];
    public IReadOnlyList<string> PushRates { get; } = ["跟随输入源帧（推荐）", "66 Hz", "125 Hz", "250 Hz", "按目标手柄默认 USB 上限"];
    public IReadOnlyList<string> DeadzoneModes { get; } = ["径向 / 圆形", "轴向 / 十字形"];
    public IReadOnlyList<string> TesterSkins { get; } = ["自动识别", "Xbox", "PlayStation", "Switch", "NS2 Pro"];
    [ObservableProperty] public partial VirtualModeCard? SelectedModeCard { get; set; }
    [ObservableProperty] public partial string PushRate { get; set; } = "跟随输入源帧（推荐）";
    [ObservableProperty] public partial decimal ApiPort { get; set; }
    [ObservableProperty] public partial decimal UsbPort { get; set; }
    [ObservableProperty] public partial double RumbleMultiplier { get; set; } = 1;
    [ObservableProperty] public partial bool AudioProtection { get; set; } = true;
    [ObservableProperty] public partial decimal GyroPitchScale { get; set; } = 1;
    [ObservableProperty] public partial decimal GyroYawScale { get; set; } = 1;
    [ObservableProperty] public partial decimal GyroRollScale { get; set; } = 1;
    [ObservableProperty] public partial bool InvertPitch { get; set; }
    [ObservableProperty] public partial bool InvertYaw { get; set; }
    [ObservableProperty] public partial bool InvertRoll { get; set; }
    [ObservableProperty] public partial bool UseStickCalibration { get; set; } = true;
    [ObservableProperty] public partial double OutputDeadzone { get; set; }
    [ObservableProperty] public partial string OutputDeadzoneMode { get; set; } = "径向 / 圆形";
    [ObservableProperty] public partial string ConfigurationResult { get; set; } = "设置保存在本机；修改后点击应用。推送频率是软件目标值，实际值见下方诊断。";
    [ObservableProperty] public partial string AudioResult { get; set; } = "防止虚拟 PS5 音频设备成为默认扬声器、通信设备或麦克风。";
    [ObservableProperty] public partial string BridgeLinkText { get; set; } = "NEUTRAL · 等待实体输入";
    [ObservableProperty] public partial string BridgeAddressText { get; set; } = "未启动本地服务";
    [ObservableProperty] public partial string BridgeRateText { get; set; } = "0.0 Hz";
    [ObservableProperty] public partial string BridgeFeedbackText { get; set; } = "尚无主机反馈";
    [ObservableProperty] public partial string BridgeRumbleText { get; set; } = "BLE 震动特征未就绪";
    [ObservableProperty] public partial string BridgeCountsText { get; set; } = "发送 0 · 反馈 0";
    [ObservableProperty] public partial string BridgeInputText { get; set; } = "无有效 BLE 输入";
    [ObservableProperty] public partial string DeviceEnumerationText { get; set; } = "尚无 Windows 手柄";
    [ObservableProperty] public partial string StickCalibrationText { get; set; } = "尚未校准 · 原始摇杆直通";
    [ObservableProperty] public partial string DiagnosticsResult { get; set; } = "";
    [ObservableProperty] public partial double TesterDeadzone { get; set; }
    [ObservableProperty] public partial string TesterDeadzoneMode { get; set; } = "径向 / 圆形";
    [ObservableProperty] public partial string TesterSkin { get; set; } = "自动识别";
    [ObservableProperty] public partial ControllerLayout DisplayLayout { get; set; }
    [ObservableProperty] public partial ControllerForwardingTool.Core.ControllerButtons DisplayButtons { get; set; }
    [ObservableProperty] public partial double FilteredLeftX { get; set; }
    [ObservableProperty] public partial double FilteredLeftY { get; set; }
    [ObservableProperty] public partial double FilteredRightX { get; set; }
    [ObservableProperty] public partial double FilteredRightY { get; set; }
    [ObservableProperty] public partial double[] LeftSectors { get; set; } = new double[72];
    [ObservableProperty] public partial double[] RightSectors { get; set; } = new double[72];
    [ObservableProperty] public partial string LeftCircularity { get; set; } = "待完成一圈";
    [ObservableProperty] public partial string RightCircularity { get; set; } = "待完成一圈";
    [ObservableProperty] public partial string InputHealthText { get; set; } = "等待输入";
    public bool CanManageDriver => !IsServerRunning && !IsVirtualBusy && !IsInstallingDriver;
    public bool CanUninstallDriver => driverRemovable && CanManageDriver;
    public bool CanEditMode => !IsVirtualBusy && !IsServerRunning;
    public string VirtualModeStatus => IsServerRunning
        ? $"正在模拟 · {VirtualProfile.Get(output.Mode).Name}"
        : $"已选择 · {SelectedModeCard?.Profile.Name ?? "—"} · 未启动";
    partial void OnSelectedModeCardChanged(VirtualModeCard? value) => OnPropertyChanged(nameof(VirtualModeStatus));
    public bool CanCalibrateSticks => IsConnected && !stickCalibration.Running;

    private void InitializeBridge()
    {
        bridgeOptions = bridgeOptions.SelectRoute(bridgeOptions.Mode);
        ModeCards = VirtualProfile.All.Select(p => new VirtualModeCard(p, SelectMode)).ToArray();
        SelectMode(ModeCards.First(c => c.Profile.Mode == bridgeOptions.Mode));
        InitializeApplicationSettings();
        output.Options = bridgeOptions; transport.RumbleGain = bridgeOptions.RumbleGain;
        output.RumbleReceived += OnBridgeRumble;
        output.Faulted += message => Ui(async () => { AddLog("虚拟输出", message); await StopServerAsync(); VirtualState = message; });
    }

    private void SelectMode(VirtualModeCard card)
    {
        if (!CanEditMode || SelectedModeCard == card) return;
        if (SelectedModeCard is { } previous)
            routeDrafts[previous.Profile.Mode] = new(CaptureRouteDraft(), HasMappingChanges);
        SelectedModeCard = card;
        foreach (var item in ModeCards) item.IsSelected = item == card;
        bool hasDraft = routeDrafts.TryGetValue(card.Profile.Mode, out var draft);
        LoadRouteDraft(hasDraft ? draft!.Options : bridgeOptions.Route(card.Profile.Mode));
        HasMappingChanges = hasDraft && draft!.MappingChanged;
        ConfigurationResult = hasDraft ? "已恢复此线路本次编辑的参数；点击应用并保存后生效。" : "已载入此线路的独立配置；修改后点击应用并保存。";
        if (HasMappingChanges) ButtonMappingStatus = "已恢复此线路未保存的映射修改；点击应用并保存后生效。";
        OnPropertyChanged(nameof(RouteConfigurationTitle));
        UpdateMappingComparison();
    }

    private sealed record RouteDraft(OutputRouteOptions Options, bool MappingChanged);
    private readonly Dictionary<VirtualControllerMode, RouteDraft> routeDrafts = [];
    private VirtualControllerMode EditedOutputMode => IsServerRunning ? output.Mode : SelectedModeCard?.Profile.Mode ?? output.Mode;
    public string RouteConfigurationTitle => $"{VirtualProfile.Get(EditedOutputMode).Name} · 独立线路配置";

    private OutputRouteOptions CaptureRouteDraft() => new()
    {
        InputKind = IsWindowsBridgeInput ? BridgeInputKind.WindowsGamepad : BridgeInputKind.Ns2Ble,
        Ns2Buttons = CaptureButtonMappings(),
        PushHz = PushRate switch { "66 Hz" => 66, "125 Hz" => 125, "250 Hz" => 250, "按目标手柄默认 USB 上限" => -1, _ => 0 },
        ApiPort = (int)ApiPort, UsbPort = (int)UsbPort, RumbleGain = RumbleMultiplier, AudioGuard = AudioProtection,
        GyroPitch = (double)GyroPitchScale, GyroYaw = (double)GyroYawScale, GyroRoll = (double)GyroRollScale,
        InvertPitch = InvertPitch, InvertYaw = InvertYaw, InvertRoll = InvertRoll,
        UseStickCalibration = UseStickCalibration, StickDeadzone = OutputDeadzone / 100,
        RadialDeadzone = OutputDeadzoneMode == DeadzoneModes[0]
    };

    private void LoadRouteDraft(OutputRouteOptions route)
    {
        PushRate = PushRates[route.PushHz switch { 66 => 1, 125 => 2, 250 => 3, -1 => 4, _ => 0 }];
        ApiPort = route.ApiPort; UsbPort = route.UsbPort;
        RumbleMultiplier = route.RumbleGain; AudioProtection = route.AudioGuard;
        GyroPitchScale = (decimal)route.GyroPitch; GyroYawScale = (decimal)route.GyroYaw; GyroRollScale = (decimal)route.GyroRoll;
        InvertPitch = route.InvertPitch; InvertYaw = route.InvertYaw; InvertRoll = route.InvertRoll;
        UseStickCalibration = route.UseStickCalibration;
        OutputDeadzone = route.StickDeadzone * 100; OutputDeadzoneMode = DeadzoneModes[route.RadialDeadzone ? 0 : 1];
        BridgeInputSelection = BridgeInputKinds[route.InputKind == BridgeInputKind.WindowsGamepad ? 1 : 0];
        LoadButtonMappings(route.Ns2Buttons);
    }

    [RelayCommand] private void ApplyBridgeSettings() => ApplyBridgeConfiguration();
    internal bool ApplyBridgeConfiguration(Action<BridgeOptions>? save = null)
    {
        try
        {
            if (ApiPort > 0 && ApiPort < 1024 || UsbPort > 0 && UsbPort < 1024 || ApiPort != 0 && ApiPort == UsbPort)
                throw new InvalidOperationException("端口必须为 0（自动）或 1024–65535，且两个端口不能相同");
            var next = bridgeOptions.SaveRoute(EditedOutputMode, CaptureRouteDraft());
            if (save is null) next.Save(); else save(next);
            bridgeOptions = next; output.Options = next; transport.RumbleGain = next.RumbleGain;
            HasMappingChanges = false;
            ButtonMappingStatus = $"{RouteConfigurationTitle}：映射已保存，输出运行时下一帧生效。";
            ConfigurationResult = $"{RouteConfigurationTitle}已保存 · 按键映射、振动、频率、摇杆及体感参数仅用于此线路";
            return true;
        }
        catch (Exception ex) { ConfigurationResult = $"保存配置失败：{ex.Message}"; return false; }
    }

    [RelayCommand] private async Task RepairAudioAsync()
    {
        AudioResult = "正在检查 Windows 默认音频设备…";
        var logs = await Task.Run(AudioEndpointGuard.EnsureDualSenseIsNotDefault);
        foreach (string line in logs) AddLog("音频保护", line);
        AudioResult = logs.Any(x => x.Contains("warning", StringComparison.OrdinalIgnoreCase))
            ? "音频检查出现问题，详情见日志" : "已检查并保护默认音频设备，详情见日志";
    }

    [RelayCommand] private void CalibrateStickCenter() => BeginStickCalibration(false);
    [RelayCommand] private void CalibrateStickRange() => BeginStickCalibration(true);
    private void BeginStickCalibration(bool range)
    {
        if (!IsConnected) return;
        stickCalibration.Start(range, activeStickProfile); OnPropertyChanged(nameof(CanCalibrateSticks));
    }
    [RelayCommand] private void ResetStickCalibration()
    {
        stickCalibration.Cancel("已清除本设备摇杆校准，恢复原始直通");
        activeStickProfile = null;
        if (calibratedBleDevice is not { } address) return;
        var profiles = new Dictionary<string, StickProfile>(bridgeOptions.StickProfiles);
        profiles.Remove(address.ToString("X12"));
        bridgeOptions = bridgeOptions with { StickProfiles = profiles };
        SaveCalibration();
    }
    private void StoreStickProfile(StickProfile profile, ulong? address)
    {
        if (address is null || address != calibratedBleDevice) return;
        activeStickProfile = profile;
        var profiles = new Dictionary<string, StickProfile>(bridgeOptions.StickProfiles) { [address.Value.ToString("X12")] = profile };
        bridgeOptions = bridgeOptions with { StickProfiles = profiles }; SaveCalibration();
    }
    private void SaveCalibration()
    {
        try { bridgeOptions.Save(); }
        catch (Exception ex) { AddLog("摇杆校准", $"保存失败：{ex.Message}"); }
    }

    [RelayCommand] private void ResetStickTrace() { leftTrace.Reset(); rightTrace.Reset(); }
    [RelayCommand] private void SetTesterDeadzone(string value) { if (double.TryParse(value, out double n)) TesterDeadzone = Math.Clamp(n, 0, 30); }
    private void UpdateTesterAnalysis()
    {
        DisplayLayout = TesterSkin switch { "Xbox" => ControllerLayout.Xbox, "PlayStation" => ControllerLayout.DualSense,
            "Switch" => ControllerLayout.SwitchPro, "NS2 Pro" => ControllerLayout.Switch2Pro, _ => TesterLayout };
        DisplayButtons = ControllerLayouts.RemapFaceButtons(TesterButtons, TesterLayout.IsNintendo(), DisplayLayout.IsNintendo());
        var left = StickMath.Filter(TesterLeftX, TesterLeftY, TesterDeadzone / 100, TesterDeadzoneMode == DeadzoneModes[0]);
        var right = StickMath.Filter(TesterRightX, TesterRightY, TesterDeadzone / 100, TesterDeadzoneMode == DeadzoneModes[0]);
        FilteredLeftX = left.X; FilteredLeftY = left.Y; FilteredRightX = right.X; FilteredRightY = right.Y;
        if (TesterIsOnline) { leftTrace.Observe(TesterLeftX, TesterLeftY); rightTrace.Observe(TesterRightX, TesterRightY); }
        LeftSectors = leftTrace.Snapshot; RightSectors = rightTrace.Snapshot;
        LeftCircularity = leftTrace.Summary; RightCircularity = rightTrace.Summary;
        InputHealthText = TesterIsOnline ? $"{TesterButtonValues.Count(x => x.Value > 0)} 个按键按下 · {(IsMappedTester ? "已保存映射预览" : !IsWindowsTester ? transport.BatteryDescription : TesterBatteryPercent < 0 ? "电量未知" : $"电量 {TesterBatteryPercent}%")}" : "等待 USB / 蓝牙手柄";
    }

    private void UpdateBridgeDiagnostics(DateTimeOffset now)
    {
        if (now < diagnosticAt.AddSeconds(1)) return;
        UpdateOverview();
        BridgeRateText = $"{(output.Sent - outputBaseline) / Math.Max(1, (now - diagnosticAt).TotalSeconds):F1} Hz";
        outputBaseline = output.Sent; diagnosticAt = now;
        BridgeLinkText = !IsServerRunning ? "OFFLINE · 虚拟输出未启动" : output.Live ? "LIVE · 实体输入正在转发" : "NEUTRAL · 等待实体输入 / 已安全归零";
        BridgeAddressText = output.Endpoints;
        BridgeCountsText = $"发送 {output.Sent:N0} · 主机反馈 {output.FeedbackCount:N0}";
        BridgeFeedbackText = output.FeedbackState;
        BridgeRumbleText = IsWindowsBridgeInput ? gamepads.FeedbackStatus : $"{(transport.CanRumble ? "BLE 震动通道已就绪" : "BLE 震动特征未就绪")} · 写入 {transport.RumbleWrites:N0} / 失败 {transport.RumbleFailures:N0}";
        lock (frameGate) BridgeInputText = IsWindowsBridgeInput ? BridgeSourceStatus : IsConnected ? $"FD2 最近输入距今 {Math.Max(0, (now - lastFrame.ReceivedAt).TotalMilliseconds):F0} ms · {ControllerStage}" : "未连接实体 NS2 BLE；虚拟输出保持中立状态";
        DeviceEnumerationText = WindowsGamepads.Count == 0 ? "尚无 Windows 手柄" : string.Join("\n", WindowsGamepads.Select(d => d.ToString()));
        StickCalibrationText = stickCalibration.ReadStatus(); OnPropertyChanged(nameof(CanCalibrateSticks));
        if (IsServerRunning && bridgeOptions.AudioGuard && output.Mode is VirtualControllerMode.DualSense or VirtualControllerMode.DualSenseEdge && now >= audioGuardAt)
        {
            audioGuardAt = now.AddSeconds(5);
            if (!RepairAudioCommand.IsRunning) RepairAudioCommand.Execute(null);
        }
    }

    [RelayCommand] private void ClearDiagnostics() { Logs.Clear(); DiagnosticsResult = "已清空界面日志；计数保留到下一次启动。"; }
    [RelayCommand] private async Task PingBackendAsync()
    {
        try { DiagnosticsResult = await output.PingAsync(lifetime.Token); }
        catch (Exception ex) { DiagnosticsResult = $"后端检查失败：{ex.Message}"; }
    }
    [RelayCommand] private void ExportDiagnostics()
    {
        try
        {
            string directory = AppDataPaths.LogDirectoryPath;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}.txt");
            File.WriteAllText(path, string.Join(Environment.NewLine, new[] { $"{AppIdentity.ChineseName} / {AppIdentity.EnglishName} 运行诊断", DateTimeOffset.Now.ToString("O"),
                SelectedModeCard?.Profile.Protocol, DriverState, BridgeLinkText, BridgeAddressText, BridgeCountsText, BridgeFeedbackText,
                BridgeRumbleText, FramesText, SticksText, GyroStatus, StickCalibrationText, "--- 日志 ---" }.Concat(Logs)), Encoding.UTF8);
            DiagnosticsResult = $"诊断已导出：{path}";
        }
        catch (Exception ex) { DiagnosticsResult = $"导出失败：{ex.Message}"; }
    }
}

public partial class VirtualModeCard : ObservableObject
{
    public VirtualProfile Profile { get; }
    public CroppedBitmap Image { get; }
    public IRelayCommand SelectCommand { get; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    public VirtualModeCard(VirtualProfile profile, Action<VirtualModeCard> select)
    {
        Profile = profile;
        using var stream = AssetLoader.Open(new Uri($"avares://{typeof(App).Assembly.GetName().Name}/Assets/Controllers/{profile.Image}.png"));
        var bitmap = new Bitmap(stream);
        // Trim only the official images' outer whitespace for consistent product scale.
        // Coordinates refer to the bundled 1000px-wide originals; keep those files unchanged.
        var frame = profile.Mode switch
        {
            VirtualControllerMode.DualSense => new PixelRect(170, 100, 660, 470),
            VirtualControllerMode.Ns2Pro => new PixelRect(170, 35, 660, 500),
            VirtualControllerMode.Xbox360 => new PixelRect(20, 200, 960, 620),
            VirtualControllerMode.DualSenseEdge => new PixelRect(40, 40, 960, 500),
            VirtualControllerMode.Ns1Pro => new PixelRect(180, 0, 640, 563),
            _ => new PixelRect(bitmap.PixelSize)
        };
        Image = new CroppedBitmap { Source = bitmap, SourceRect = frame };
        SelectCommand = new RelayCommand(() => select(this));
    }
}
