using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Bluetooth;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Protocol.Ns2;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly Ns2BleTransport transport = new();
    private readonly Fd2Decoder decoder = new();
    private readonly VirtualControllerSession output = new();
    private readonly UsbIpDriverInstaller driverInstaller = new();
    private readonly GamepadMonitor gamepads = new();
    private readonly GyroCalibration bleCalibration = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly object frameGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<(ulong, Windows.Devices.Bluetooth.BluetoothAddressType), BleConnectionFailure> connectionFailures = [];
    private bool scanRequested = true;
    private bool loadingBluetoothSettings;
    private DateTimeOffset nextScanDiagnosticAt;
    private bool disconnecting;
    private BleCandidate? activeBleCandidate;
    private DateTimeOffset nextCandidateExpiryAt;
    private DateTimeOffset nextHostDiagnosticAt;
    private DateTimeOffset nextBleRateLogAt;
    private ControllerState lastFrame = ControllerState.Neutral(DateTimeOffset.Now);
    private byte[] rawSample = [];
    private long validFrames, invalidFrames, rateBaseline, feedbackCount;
    private DateTimeOffset rateAt = DateTimeOffset.Now, nextDevicesAt, nextScanAt;
    private CancellationTokenSource? connectionCancellation;
    private Task? connectingTask;
    private CancellationTokenSource? rumbleTestCancellation;
    private int acceptingFrames, ns2BatteryPercent = -1;
    private uint? lastMotionTimestamp;
    private ulong? calibratedBleDevice;
    private bool disposed, closing;
    private string selectedPage = "概览";

    public MainViewModel()
    {
        InitializeBridge();
        loadingBluetoothSettings = true;
        RegisterHostOnSync = bridgeOptions.RegisterHostOnSync;
        loadingBluetoothSettings = false;
        InitializeMotionSettings();
        InitializeInputBridge();
        LoadButtonMappings(bridgeOptions.Ns2Buttons);
        RefreshRememberedDevices();
        transport.CandidateSeen += OnCandidateSeen;
        transport.StageChanged += stage => Ui(() => ControllerStage = stage);
        transport.ScanStopped += () => Ui(() => { IsScanning = false; nextScanAt = DateTimeOffset.Now.AddSeconds(5); });
        transport.Diagnostic += message => Ui(() => AddLog("BLE", message));
        transport.FrameReceived += OnFrameReceived;
        transport.DeviceDisconnected += () => Ui(() => { if (!IsConnecting) _ = HandleLostConnectionAsync(); });
        transport.BatteryChanged += value => Interlocked.Exchange(ref ns2BatteryPercent, value ?? -1);
        output.Status += message => Ui(() => { VirtualState = message; AddLog("USB/IP", message); });
        output.OutputReceived += _ => Interlocked.Increment(ref feedbackCount);
        timer.Tick += (_, _) => { UpdatePreview(); UpdateTesterAnalysis(); UpdateMappingComparison(); Ns2BatteryText = IsConnected ? transport.BatteryDescription : "未连接"; UpdateBridgeDiagnostics(DateTimeOffset.Now); };
        timer.Start(); RefreshDriverState(); RefreshGamepads();
        AddLog("应用", "自动发现手柄已开启；在虚拟页选择 NS2 蓝牙或 Windows 手柄作为输入");
        _ = InitializeAsync();
    }

    public ObservableCollection<BleCandidate> Candidates { get; } = [];
    [ObservableProperty] public partial string RememberedAliasDraft { get; set; } = "";
    [ObservableProperty] public partial string RememberedAliasStatus { get; set; } = "仅修改本地显示名称；留空保存可恢复原名";
    [ObservableProperty] public partial BleDeviceIdentity? RenamingDevice { get; set; }
    public bool IsRenamingDevice => RenamingDevice is not null;
    public string RenameDeviceTitle => RenamingDevice is { } device ? $"重命名：{device.Summary}" : "";
    partial void OnRenamingDeviceChanged(BleDeviceIdentity? value)
    { OnPropertyChanged(nameof(IsRenamingDevice)); OnPropertyChanged(nameof(RenameDeviceTitle)); }
    public ObservableCollection<BleDeviceIdentity> RememberedDevices { get; } = [];
    [ObservableProperty] public partial BleDeviceIdentity? SelectedRememberedDevice { get; set; }
    [ObservableProperty] public partial string RememberedDevicesStatus { get; set; } = "尚无成功连接记录";
    [ObservableProperty] public partial bool IsForgettingDevice { get; set; }
    partial void OnIsForgettingDeviceChanged(bool value) => OnPropertyChanged(nameof(CanConnect));
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<GamepadDevice> WindowsGamepads { get; } = [];
    public IReadOnlyList<string> TesterSources { get; } = ["Windows 手柄 · 自动识别", "NS2 Pro · 蓝牙输入", "模拟手柄 · 已保存映射预览"];
    public IReadOnlyList<string> VirtualModes { get; } = ["NS2 Pro · 原生 USB", "NS1 Pro · 兼容 USB"];
    public ObservableCollection<TesterValue> TesterButtonValues { get; } = [];
    public ObservableCollection<TesterValue> TesterAxisValues { get; } = [];
    public string SelectedPage
    {
        get => selectedPage;
        set
        {
            if (!SetProperty(ref selectedPage, value)) return;
            UpdateNs2UsbAccess();
            foreach (string name in new[] { nameof(IsOverview), nameof(IsBluetooth), nameof(IsVirtual), nameof(IsMapping),
                nameof(IsLogs), nameof(IsSettings), nameof(IsChangelog), nameof(IsAbout), nameof(IsTester), nameof(IsGyroCalibration), nameof(IsTesterSection) }) OnPropertyChanged(name);
        }
    }
    public bool IsOverview => SelectedPage == "概览";
    public bool IsBluetooth => SelectedPage == "NS2 Pro 连接";
    public bool IsVirtual => SelectedPage == "虚拟手柄";
    public bool IsLogs => SelectedPage == "日志";
    public bool IsSettings => SelectedPage == "设置";
    public bool IsChangelog => SelectedPage == "更新日志";
    public bool IsAbout => SelectedPage == "关于";
    public bool IsTester => SelectedPage == "手柄测试";
    public bool IsWindowsTester => SelectedTesterSource.StartsWith("Windows", StringComparison.Ordinal);
    public bool CanConnect => SelectedCandidate is not null && !IsConnecting && !IsConnected && !IsForgettingDevice && !IsNs2UsbConnected && !IsNs2UsbBusy;
    public bool CanStopConnecting => AutoConnect || IsScanning || IsConnecting;
    public string ConnectActionText => SelectedCandidate is { } candidate && !bridgeOptions.BleDevices.AllowsAutoConnect(candidate)
        ? "重新配对选中设备" : "连接选中设备";
    public bool CanStartVirtual => !IsVirtualBusy && !IsInstallingDriver && !IsServerRunning && driverReady && (!IsWindowsBridgeInput || SelectedBridgeGamepad is not null);
    public bool IsDriverReady => driverReady;
    public string TesterBackend => gamepads.Status;
    public string ConnectionStatusLabel => IsNs2UsbConnected ? "NS2 Pro · USB 已连接" : IsConnected ? "NS2 Pro · 蓝牙已连接"
        : IsConnecting ? "NS2 Pro 连接中"
        : IsScanning ? "正在搜索 NS2 Pro"
        : "NS2 Pro 未连接";
    public double NavigationWidth => IsNavigationExpanded ? 216 : 72;
    [ObservableProperty] public partial bool IsNavigationExpanded { get; set; } = true;
    partial void OnIsNavigationExpandedChanged(bool value) => OnPropertyChanged(nameof(NavigationWidth));
    [RelayCommand] private void ToggleNavigation() => IsNavigationExpanded = !IsNavigationExpanded;
    [ObservableProperty] public partial ControllerLayout TesterLayout { get; set; }
    [ObservableProperty] public partial decimal RumbleDuration { get; set; } = 1000;
    [ObservableProperty] public partial double RumbleLow { get; set; } = 25;
    [ObservableProperty] public partial double RumbleHigh { get; set; } = 25;
    [ObservableProperty] public partial string GyroStatus { get; set; } = "当前输入源未提供陀螺仪";
    [ObservableProperty] public partial string GyroRawText { get; set; } = "—";
    [ObservableProperty] public partial string GyroCorrectedText { get; set; } = "—";
    [ObservableProperty] public partial string GyroBiasText { get; set; } = "—";
    [ObservableProperty] public partial bool CanCalibrate { get; set; }
    [ObservableProperty] public partial bool IsCalibrating { get; set; }
    [ObservableProperty] public partial double CalibrationProgress { get; set; }
    [ObservableProperty] public partial string AdapterState { get; set; } = "检查中";
    [ObservableProperty] public partial string ControllerStage { get; set; } = "等待手柄";
    [ObservableProperty] public partial string ControllerStateText { get; set; } = "尚未收到输入";
    [ObservableProperty] public partial string VirtualState { get; set; } = "虚拟 USB 未启动";
    [ObservableProperty] public partial string InstallerState { get; set; } = "检查中";
    [ObservableProperty] public partial string DriverState { get; set; } = "检查中";
    [ObservableProperty] public partial string GameState { get; set; } = "等待游戏实测";
    [ObservableProperty] public partial string ButtonsText { get; set; } = "无";
    [ObservableProperty] public partial string SticksText { get; set; } = "L (2048, 2048) · R (2048, 2048)";
    [ObservableProperty] public partial string ImuText { get; set; } = "暂无数据";
    [ObservableProperty] public partial string FramesText { get; set; } = "有效 0 · 无效 0";
    [ObservableProperty] public partial string LastInputText { get; set; } = "尚未收到有效报文";
    [ObservableProperty] public partial string RawSampleText { get; set; } = "暂无报文";
    [ObservableProperty] public partial BleCandidate? SelectedCandidate { get; set; }
    [ObservableProperty] public partial bool IsScanning { get; set; }
    [ObservableProperty] public partial bool IsConnecting { get; set; }
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial bool AutoConnect { get; set; } = true;
    [ObservableProperty] public partial bool RegisterHostOnSync { get; set; }
    [ObservableProperty] public partial string RegistrationSettingsStatus { get; set; } = "";
    [ObservableProperty] public partial string BleConnectionTiming { get; set; } = "蓝牙未连接";
    public bool CanChangeRegistration => !IsConnecting;
    [ObservableProperty] public partial bool IsServerRunning { get; set; }
    [ObservableProperty] public partial bool IsVirtualBusy { get; set; }
    [ObservableProperty] public partial bool IsInstallingDriver { get; set; }
    [ObservableProperty] public partial string SelectedVirtualMode { get; set; } = "NS2 Pro · 原生 USB";
    [ObservableProperty] public partial string DriverInstallResult { get; set; } = "安装和卸载会打开官方向导；可能需要管理员权限或重启";
    [ObservableProperty] public partial string SelectedTesterSource { get; set; } = "NS2 Pro · 蓝牙输入";
    [ObservableProperty] public partial GamepadDevice? SelectedWindowsGamepad { get; set; }
    [ObservableProperty] public partial string TesterSummary { get; set; } = "等待手柄输入";
    [ObservableProperty] public partial string TesterMotionText { get; set; } = "暂无传感器数据";
    [ObservableProperty] public partial string TesterDeviceInfo { get; set; } = "连接 USB 或唤醒已配对的蓝牙手柄";
    [ObservableProperty] public partial string TesterRateText { get; set; } = "上传速率 — Hz";
    [ObservableProperty] public partial string TesterRateHint { get; set; } = "等待输入报文，按最近 1 秒统计。";
    [ObservableProperty] public partial string RumbleResult { get; set; } = "";
    [ObservableProperty] public partial ControllerButtons TesterButtons { get; set; }
    [ObservableProperty] public partial bool TesterIsOnline { get; set; }
    [ObservableProperty] public partial bool TesterHasMotion { get; set; }
    [ObservableProperty] public partial int TesterBatteryPercent { get; set; } = -1;
    [ObservableProperty] public partial string Ns2BatteryText { get; set; } = "未连接";
    [ObservableProperty] public partial double TesterLeftX { get; set; }
    [ObservableProperty] public partial double TesterLeftY { get; set; }
    [ObservableProperty] public partial double TesterRightX { get; set; }
    [ObservableProperty] public partial double TesterRightY { get; set; }
    [ObservableProperty] public partial double TesterLeftTrigger { get; set; }
    [ObservableProperty] public partial double TesterRightTrigger { get; set; }
    [ObservableProperty] public partial double TesterPitch { get; set; }
    [ObservableProperty] public partial double TesterRoll { get; set; }
    partial void OnSelectedCandidateChanged(BleCandidate? value)
    {
        OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(ConnectActionText));
    }
    partial void OnIsScanningChanged(bool value) { OnPropertyChanged(nameof(ConnectionStatusLabel)); OnPropertyChanged(nameof(CanStopConnecting)); }
    partial void OnIsConnectingChanged(bool value) { OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(ConnectionStatusLabel)); OnPropertyChanged(nameof(CanStopConnecting)); OnPropertyChanged(nameof(CanChangeRegistration)); }
    partial void OnIsConnectedChanged(bool value) { OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(CanCalibrateSticks)); OnPropertyChanged(nameof(ConnectionStatusLabel)); }
    private void NotifyDriverAvailability()
    {
        OnPropertyChanged(nameof(CanManageDriver));
        OnPropertyChanged(nameof(CanUninstallDriver));
    }
    partial void OnIsVirtualBusyChanged(bool value) { OnPropertyChanged(nameof(CanStartVirtual)); NotifyDriverAvailability(); OnPropertyChanged(nameof(CanEditMode)); }
    partial void OnIsInstallingDriverChanged(bool value) { OnPropertyChanged(nameof(CanStartVirtual)); NotifyDriverAvailability(); }
    partial void OnIsServerRunningChanged(bool value)
    {
        NotifyDriverAvailability();
        OnPropertyChanged(nameof(CanStartVirtual)); OnPropertyChanged(nameof(CanEditMode));
        OnPropertyChanged(nameof(VirtualModeStatus)); UpdateOverview();
        OnPropertyChanged(nameof(RouteConfigurationTitle));
        UpdateMappingComparison();
        OnPropertyChanged(nameof(IsMapping));
        foreach (var card in ModeCards) card.IsRunning = value && card.Profile.Mode == output.Mode;
    }
    partial void OnSelectedTesterSourceChanged(string value)
    {
        ResetTesterRate();
        ResetStickTrace();
        bleCalibration.Cancel("已切换输入源");
        if (SelectedWindowsGamepad is { } pad) gamepads.Calibration(pad.Id).Cancel("已切换输入源");
        _ = StopRumbleSafelyAsync(SelectedWindowsGamepad);
        OnPropertyChanged(nameof(IsWindowsTester));
        OnPropertyChanged(nameof(IsMappedTester)); UpdateTesterCardSelection();
        UpdateNs2UsbAccess();
    }
    partial void OnSelectedWindowsGamepadChanged(GamepadDevice? oldValue, GamepadDevice? newValue)
    {
        ResetTesterRate();
        ResetStickTrace();
        UpdateTesterCardSelection();
        if (oldValue is null) return;
        gamepads.Calibration(oldValue.Id).Cancel("已切换设备");
        _ = StopRumbleSafelyAsync(oldValue);
    }
    partial void OnAutoConnectChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStopConnecting));
        if (value) { scanRequested = true; nextScanAt = default; BeginScan(); }
        else
        {
            if (IsConnecting) connectionCancellation?.Cancel();
            if (IsScanning) ControllerStage = "自动连接已关闭；可选择扫描到的手柄手动连接";
        }
    }
    [RelayCommand] private void Navigate(string? page) { if (page is not null) SelectedPage = page; }
    private async Task InitializeAsync()
    {
        await CheckAdapterAsync(); if (!closing && AutoConnect) BeginScan();
        if (!closing && bridgeOptions.AutoStartOutput && CanStartVirtual) await StartServerAsync();
    }
    [RelayCommand] private async Task CheckAdapterAsync()
    {
        try { AdapterState = await transport.CheckAdapterAsync(); }
        catch (Exception ex) { AdapterState = $"检查失败：{ex.Message}"; }
    }
    [RelayCommand] private void Scan()
    {
        if (disconnecting || closing || IsConnecting || IsConnected) return;
        Candidates.Clear(); SelectedCandidate = null;
        if (!IsNs2UsbConnected) usbHandoverAddress = 0;
        connectionFailures.Clear();
        transport.StopScan(); IsScanning = false;
        scanRequested = true; BeginScan(); SelectedPage = "NS2 Pro 连接";
    }
    private void BeginScan()
    {
        if (closing || disconnecting || !scanRequested || IsConnected || IsConnecting) return;
        var now = DateTimeOffset.Now;
        try
        {
            if (!IsScanning)
            {
                transport.StartScan(); IsScanning = true;
                nextScanDiagnosticAt = now.AddSeconds(20);
                ControllerStage = !AutoConnect ? "正在扫描；自动连接已关闭，请选择手柄手动连接" : RememberedDevices.Count == 0
                    ? "首次连接请长按顶部 SYNC；正在搜索 NS2 Pro"
                        : "正在寻找已连接过的 NS2 Pro；普通唤醒无响应时请长按 SYNC";
            }
            if (now >= nextScanDiagnosticAt)
            {
                transport.ReportScanDiagnostics(); nextScanDiagnosticAt = now.AddSeconds(20);
            }
            // Only OnCandidateSeen starts automatic connections. Saved addresses
            // are discovery hints, never evidence that a controller is awake.
        }
        catch (Exception ex) { ControllerStage = $"扫描失败：{ex.Message}"; nextScanAt = DateTimeOffset.Now.AddSeconds(10); }
    }
    [RelayCommand] private void StopScan()
    {
        scanRequested = false; AutoConnect = false; connectionCancellation?.Cancel();
        transport.StopScan(); IsScanning = false;
        ControllerStage = "已停止搜索和连接";
    }
    partial void OnRegisterHostOnSyncChanged(bool value)
    {
        if (loadingBluetoothSettings) return;
        try
        {
            var next = bridgeOptions with { RegisterHostOnSync = value };
            next.Save(); bridgeOptions = next;
            RegistrationSettingsStatus = value ? "已保存：下次 SYNC 连接时注册本机" : "已保存：不写入手柄注册信息；已有注册仍可回连";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loadingBluetoothSettings = true;
            RegisterHostOnSync = bridgeOptions.RegisterHostOnSync;
            loadingBluetoothSettings = false;
            RegistrationSettingsStatus = $"设置未保存：{ex.Message}";
        }
    }
    [RelayCommand] private Task ConnectAsync()
    {
        if (closing || disconnecting || !CanConnect || SelectedCandidate is not { } candidate) return Task.CompletedTask;
        if (!BleCandidateLifetime.IsVisible(candidate, DateTimeOffset.Now, activeBleCandidate, IsConnected))
        {
            RemoveCandidate(candidate);
            ControllerStage = "该设备已不在当前扫描范围内，请唤醒手柄后重新扫描";
            return Task.CompletedTask;
        }
        if (!bridgeOptions.BleDevices.AllowsManualConnect(candidate, DateTimeOffset.Now))
        {
            scanRequested = true; BeginScan();
            ControllerStage = "此手柄已删除。请长按顶部 SYNC；开启自动连接时，发现新的配对广播后会自动连接";
            return Task.CompletedTask;
        }
        scanRequested = true;
        usbHandoverAddress = candidate.Address;
        connectingTask = ConnectCandidateAsync(candidate);
        return connectingTask;
    }
    private async Task ConnectCandidateAsync(BleCandidate candidate)
    {
        activeBleCandidate = candidate;
        IsConnecting = true; IsScanning = false;
        connectionCancellation?.Dispose();
        connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        ResetInput("正在连接 NS2 Pro");
        if (calibratedBleDevice != candidate.Address) { bleCalibration.Reset(); calibratedBleDevice = candidate.Address; }
        activeStickProfile = bridgeOptions.StickProfiles.GetValueOrDefault(candidate.Address.ToString("X12"));
        Volatile.Write(ref acceptingFrames, 1);
        try
        {
            transport.StopScan();
            AddLog("蓝牙", $"连接候选设备：{candidate.MaskedAddress}，地址类型 {candidate.AddressType}；" +
                $"广播={(candidate.Advertisement is null ? "未知" : candidate.Advertisement.IsPairing ? "SYNC 配对" : "普通唤醒")}；" +
                $"可连接提示={candidate.IsConnectable}；目标主机={DescribeAdvertisementTarget(candidate)}；最后发现={candidate.LastSeen:O}");
            await transport.ConnectAsync(candidate, connectionCancellation.Token, registerHostOnSync: RegisterHostOnSync);
            connectionCancellation.Token.ThrowIfCancellationRequested();
            IsConnected = true; ControllerStage = transport.Registration is not null
                ? "NS2 Pro 已连接，主机注册已确认；可关闭手柄后测试普通按键回连"
                : "NS2 Pro 已连接，输入正常";
            AddLog("蓝牙", $"收到有效输入：{candidate.MaskedAddress}");
            connectionFailures.Remove((candidate.Address, candidate.AddressType));
            RememberConnectedDevice(candidate);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref acceptingFrames, 0);
            await transport.DisconnectAsync();
            string reason = connectionCancellation.IsCancellationRequested ? "连接已取消"
                : $"连接超时（{ControllerStage}），将继续扫描；仍无响应时请长按 SYNC";
            ResetInput(reason); AddLog("蓝牙", reason);
            RecordConnectionFailure(candidate);
        }
        catch (Exception ex)
        {
            await transport.DisconnectAsync();
            Volatile.Write(ref acceptingFrames, 0); ResetInput($"连接失败：{ex.Message}");
            RecordConnectionFailure(candidate);
            AddLog("蓝牙", $"{candidate.MaskedAddress}：{ex.Message}；等待新的广播后重试");
        }
        finally
        {
            string resultStage = ControllerStage;
            IsConnecting = false;
            nextScanAt = default;
            BeginScan();
            if (IsScanning) ControllerStage = $"{resultStage}；正在等待新的广播";
        }
    }
    private void RecordConnectionFailure(BleCandidate candidate) =>
        connectionFailures[(candidate.Address, candidate.AddressType)] = new(DateTimeOffset.Now,
            BleDeviceHistory.IsFreshPairing(candidate, candidate.LastSeen));
    private void RememberConnectedDevice(BleCandidate candidate)
    {
        var identity = BleDeviceIdentity.FromCandidate(candidate);
        if (!identity.IsValid) return;
        var previous = bridgeOptions.BleDevices.Remembered.FirstOrDefault(x => x.Matches(candidate.Address, candidate.AddressType));
        identity = identity with { Registration = transport.Registration ??
            (previous?.Registration is { IsValid: true } registration && registration.HostAddress == transport.LocalAddress &&
                candidate.Advertisement?.TargetHost == transport.LocalAddress ? registration : null) };
        bridgeOptions = bridgeOptions with { LastBleDevice = null, BleDevices = bridgeOptions.BleDevices.Remember(identity) };
        RefreshRememberedDevices();
        try { bridgeOptions.Save(); AddLog("蓝牙", "已记住本次有效连接，下次启动可尝试唤醒回连"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RememberedDevicesStatus = "设备记录保存失败，仅本次运行有效";
            AddLog("蓝牙", $"设备记录保存失败，本次运行仍可回连：{ex.Message}");
        }
    }
    private void RefreshRememberedDevices()
    {
        var selected = SelectedRememberedDevice;
        RememberedDevices.Clear();
        foreach (var device in bridgeOptions.BleDevices.Remembered)
            RememberedDevices.Add(device with { Alias = bridgeOptions.BleDevices.GetAlias(device.Address, device.AddressType) });
        SelectedRememberedDevice = RememberedDevices.FirstOrDefault(x => x.Matches(selected?.Address ?? 0, selected?.AddressType ?? default))
            ?? RememberedDevices.FirstOrDefault();
        transport.RememberDevices(RememberedDevices);
        OnPropertyChanged(nameof(ConnectActionText));
        RememberedDevicesStatus = RememberedDevices.Count == 0 ? "尚无成功连接记录" : $"已记住 {RememberedDevices.Count} 个手柄 · 最近连接的在前";
    }
    private BleCandidate WithAlias(BleCandidate candidate) => candidate with
    { Alias = bridgeOptions.BleDevices.GetAlias(candidate.Address, candidate.AddressType) };

    [RelayCommand] private void BeginRenameRememberedDevice(BleDeviceIdentity? device)
    {
        if (device is null || !bridgeOptions.BleDevices.Remembered.Any(x => x.Matches(device.Address, device.AddressType))) return;
        RenamingDevice = device;
        RememberedAliasDraft = bridgeOptions.BleDevices.GetAlias(device.Address, device.AddressType) ?? device.Name;
        RememberedAliasStatus = "仅修改本地显示名称；留空保存可恢复原名";
    }
    [RelayCommand] private void CancelRenameRememberedDevice() { RenamingDevice = null; RememberedAliasDraft = ""; }
    [RelayCommand] private void SaveRememberedDeviceName()
    {
        if (RenamingDevice is not { } device || !bridgeOptions.BleDevices.Remembered.Any(x => x.Matches(device.Address, device.AddressType))) return;
        try
        {
            var selected = SelectedCandidate;
            var next = bridgeOptions with { BleDevices = bridgeOptions.BleDevices.Rename(device.ToCandidate(), RememberedAliasDraft) };
            next.Save(); bridgeOptions = next;
            for (int i = 0; i < Candidates.Count; i++) Candidates[i] = WithAlias(Candidates[i]);
            if (selected is not null)
                SelectedCandidate = Candidates.FirstOrDefault(x => x.Address == selected.Address && x.AddressType == selected.AddressType);
            RefreshRememberedDevices();
            RememberedDevicesStatus = string.IsNullOrWhiteSpace(RememberedAliasDraft) ? "已恢复原始名称" : "名称已保存，重启后保留";
            CancelRenameRememberedDevice();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { RememberedAliasStatus = $"名称未保存：{ex.Message}"; }
    }
    [RelayCommand] private async Task ForgetRememberedDeviceAsync(BleDeviceIdentity? selected)
    {
        if (selected is null || closing || disconnecting ||
            !bridgeOptions.BleDevices.Remembered.Any(x => x.Matches(selected.Address, selected.AddressType))) return;
        var next = bridgeOptions with { LastBleDevice = null, BleDevices = bridgeOptions.BleDevices.Forget(selected) };
        try { next.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RememberedDevicesStatus = $"删除未保存，请重试：{ex.Message}";
            return;
        }
        bridgeOptions = next;
        if (RenamingDevice is { } renaming && selected.Matches(renaming.Address, renaming.AddressType)) CancelRenameRememberedDevice();
        disconnecting = true; IsForgettingDevice = true;
        try
        {
            // Persist the exclusion before awaiting cancellation so a completed
            // connection cannot restore this row during the delete operation.
            if (activeBleCandidate is { } active && selected.Matches(active.Address, active.AddressType))
            {
                connectionCancellation?.Cancel(); Volatile.Write(ref acceptingFrames, 0);
                if (connectingTask is not null) await connectingTask;
                await transport.DisconnectAsync();
                activeBleCandidate = null;
                ResetInput("已删除此手柄；再次使用请长按 SYNC 重新配对");
            }
            for (int i = Candidates.Count - 1; i >= 0; i--)
                if (selected.Matches(Candidates[i].Address, Candidates[i].AddressType)) Candidates.RemoveAt(i);
            if (SelectedCandidate is { } candidate && selected.Matches(candidate.Address, candidate.AddressType)) SelectedCandidate = null;
            connectionFailures.Remove((selected.Address, selected.AddressType));
            RefreshRememberedDevices();
            RememberedDevicesStatus = "记录和名称已删除，正在清理 Windows 配对…";
            await transport.ForgetSystemPairingAsync(selected, lifetime.Token);
            RememberedDevicesStatus = "已删除记录、名称及 Windows 配对；再次使用请长按 SYNC，开启自动连接时会自动连接";
            AddLog("蓝牙", $"已删除手柄并清理系统配对：{selected.ToCandidate().MaskedAddress}");
        }
        catch (Exception ex)
        {
            RefreshRememberedDevices();
            RememberedDevicesStatus = $"本地记录和名称已删除；系统配对未能确认清理：{ex.Message}。请在 Windows 蓝牙设置中检查并移除此设备。";
            AddLog("蓝牙", RememberedDevicesStatus);
        }
        finally { disconnecting = false; IsForgettingDevice = false; nextScanAt = DateTimeOffset.Now.AddSeconds(1); }
    }
    [RelayCommand] private async Task DisconnectAsync()
    {
        if (disconnecting) return;
        disconnecting = true;
        try
        {
            StopScan(); Volatile.Write(ref acceptingFrames, 0);
            if (connectingTask is not null) await connectingTask;
            await transport.DisconnectAsync(); ResetInput("已手动断开；自动连接已暂停");
            if (activeBleCandidate is { } previous) RemoveCandidate(previous);
            AddLog("蓝牙", "用户断开连接；自动连接已暂停");
        }
        finally { disconnecting = false; }
    }
    private async Task HandleLostConnectionAsync()
    {
        if (!IsConnected || closing || disconnecting) return;
        disconnecting = true;
        try
        {
            Volatile.Write(ref acceptingFrames, 0); ResetInput("实体手柄已断开，等待重新唤醒");
            if (activeBleCandidate is { } previous) RemoveCandidate(previous);
            await transport.DisconnectAsync();
            nextScanAt = default;
        }
        finally { disconnecting = false; }
    }
    private void OnCandidateSeen(BleCandidate candidate) => Ui(() =>
    {
        if (!IsScanning || disconnecting) return;
        candidate = WithAlias(candidate);
        bool wasSelected = SelectedCandidate is { } selected && selected.Address == candidate.Address && selected.AddressType == candidate.AddressType;
        int i = Candidates.ToList().FindIndex(x => x.Address == candidate.Address && x.AddressType == candidate.AddressType);
        if (i >= 0) Candidates[i] = candidate;
        else { if (Candidates.Count >= 32) Candidates.RemoveAt(0); Candidates.Add(candidate); }
        if (wasSelected || SelectedCandidate is null) SelectedCandidate = candidate;
        if (IsNs2UsbConnected || IsNs2UsbBusy || (usbHandoverAddress != 0 && candidate.Address != usbHandoverAddress)) return;
        if (!BleAutoConnectPolicy.CanAttempt(candidate, bridgeOptions.BleDevices, AutoConnect, IsConnected, IsConnecting,
            connectionFailures.GetValueOrDefault((candidate.Address, candidate.AddressType)), DateTimeOffset.Now)) return;
        if (candidate.Advertisement?.TargetsOtherHost(transport.LocalAddress) == true)
        {
            if (DateTimeOffset.Now >= nextHostDiagnosticAt)
            {
                nextHostDiagnosticAt = DateTimeOffset.Now.AddSeconds(20);
                AddLog("蓝牙", $"{candidate.MaskedAddress} 的广播目标字段与本机不同，仅供诊断，仍尝试连接");
            }
        }
        // Advertisement metadata is a hint, not a reliable gate across adapters/firmware.
        // In particular, false/unknown connectability must not suppress SYNC discovery.
        AddLog("蓝牙", $"广播模式：{(candidate.Advertisement is null ? "未知" : candidate.Advertisement.IsPairing ? "SYNC 配对" : "回连广播")}；可连接提示={candidate.IsConnectable}");
        SelectedCandidate = candidate; connectingTask = ConnectCandidateAsync(candidate);
    });
    private void OnFrameReceived(byte[] data, DateTimeOffset at)
    {
        if (Volatile.Read(ref acceptingFrames) == 0) return;
        lock (frameGate) rawSample = data.Take(80).ToArray();
        if (!decoder.TryDecode(data, at, out var state)) { Interlocked.Increment(ref invalidFrames); return; }
        lock (frameGate) lastFrame = state;
        double sampleAt = GyroCalibration.Now;
        if (lastMotionTimestamp != state.MotionTimestampMicroseconds)
        {
            lastMotionTimestamp = state.MotionTimestampMicroseconds;
            bleCalibration.Observe(new(sampleAt, MotionCoordinates.FromNs2(new Vector3(state.AccelX, state.AccelY, state.AccelZ) / 4096f),
                MotionCoordinates.FromNs2(new Vector3(state.GyroX, state.GyroY, state.GyroZ) / 16.384f)), sampleAt);
        }
        var motion = bleCalibration.Read(sampleAt);
        if (motion.Available)
        {
            var nativeGyro = MotionCoordinates.ToNs2(motion.FilteredGyro);
            state = state with { GyroX = RawGyro(nativeGyro.X), GyroY = RawGyro(nativeGyro.Y), GyroZ = RawGyro(nativeGyro.Z) };
        }
        var calibrated = stickCalibration.Observe(state);
        var address = calibratedBleDevice;
        if (calibrated is not null) Ui(() => StoreStickProfile(calibrated, address));
        Interlocked.Increment(ref validFrames);
        inputBridge.Ble(state, Volatile.Read(ref activeStickProfile));
    }
    private static short RawGyro(float dps) => (short)Math.Clamp(Math.Round(dps * 16.384), short.MinValue, short.MaxValue);
    [RelayCommand] private async Task StartServerAsync()
    {
        if (!CanStartVirtual) return;
        IsVirtualBusy = true;
        try
        {
            VirtualState = "正在创建虚拟 USB…";
            if (IsWindowsBridgeInput && (SelectedBridgeGamepad is not { } source || !inputGuard.Allows(source) || gamepads.Read(source) is null))
            { VirtualState = "输入设备未就绪，请选择已连接的实体手柄"; return; }
            if ((IsWindowsBridgeInput && SelectedBridgeGamepad?.Layout == ControllerLayout.Switch2Pro || !IsWindowsBridgeInput && IsNs2UsbConnected) && SelectedModeCard!.Profile.Mode == VirtualControllerMode.Ns2Pro)
            { VirtualState = "NS2 USB 测试接口需要独占。输出 NS2 身份时请改用 NS2 直连蓝牙输入，或选择其他输入手柄。"; return; }
            if (!ApplyBridgeConfiguration()) { VirtualState = ConfigurationResult; return; }
            inputGuard.Begin(gamepads.Latest.Devices, SelectedModeCard!.Profile.Mode);
            await output.StartAsync(SelectedModeCard.Profile.Mode, lifetime.Token);
            IsServerRunning = true;
            if (!IsWindowsBridgeInput) { scanRequested = true; BeginScan(); }
            RefreshGamepads();
        }
        catch (Exception ex) { inputGuard.End(); VirtualState = $"虚拟设备启动失败：{ex.Message}"; AddLog("USB/IP", VirtualState); }
        finally { IsVirtualBusy = false; }
    }
    [RelayCommand] private async Task StopServerAsync()
    {
        if (IsVirtualBusy) return;
        IsVirtualBusy = true;
        try { await output.StopAsync(); inputGuard.Observe(gamepads.Latest.Devices); inputGuard.End(); gamepads.ClearFeedback(); IsServerRunning = false; VirtualState = "虚拟 USB 已停止"; }
        catch (Exception ex) { VirtualState = $"停止失败：{ex.Message}"; }
        finally { IsVirtualBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanManageDriver))] private void RefreshDriverState()
    {
        var status = driverInstaller.Inspect(); InstallerState = status.Installer; DriverState = status.Driver;
        driverRemovable = status.CanUninstall; driverReady = status.Ready;
        OnPropertyChanged(nameof(CanUninstallDriver)); OnPropertyChanged(nameof(CanStartVirtual));
        OnPropertyChanged(nameof(IsDriverReady));
    }
    [RelayCommand(CanExecute = nameof(CanManageDriver))] private Task InstallDriverAsync() => ManageDriverAsync(false);
    [RelayCommand(CanExecute = nameof(CanUninstallDriver))] private Task UninstallDriverAsync() => ManageDriverAsync(true);
    private async Task ManageDriverAsync(bool uninstall)
    {
        if (!CanManageDriver) return;
        if (uninstall && !driverInstaller.Inspect().CanUninstall) { RefreshDriverState(); return; }
        IsInstallingDriver = true;
        try
        {
            DriverInstallResult = uninstall ? "等待 USB/IP 卸载向导" : "验证安装器并打开 USB/IP 安装向导";
            DriverInstallResult = uninstall ? await driverInstaller.UninstallAsync() : await driverInstaller.InstallAsync();
        }
        catch (Exception ex) { DriverInstallResult = UsbIpDriverInstaller.IsUserCancellation(ex) ? "已取消系统权限请求" : ex.Message; }
        finally { RefreshDriverState(); IsInstallingDriver = false; AddLog("驱动", DriverInstallResult); }
    }
    [RelayCommand] private void RefreshGamepads()
    {
        gamepads.RequestRefresh(); SyncGamepads();
    }
    private void SyncGamepads()
    {
          var devices = gamepads.Latest.Devices; SyncBridgeInputs(devices); SyncNs2Usb(devices); uint? selected = SelectedWindowsGamepad?.Id;
        OnPropertyChanged(nameof(TesterBackend));
        if (!WindowsGamepads.SequenceEqual(devices))
        {
            WindowsGamepads.Clear(); foreach (var device in devices) WindowsGamepads.Add(device);
              SelectedWindowsGamepad = WindowsGamepads.FirstOrDefault(x => x.Id == selected);
          }
          SyncTesterCards(devices);
    }
    [RelayCommand] private async Task TestRumbleAsync()
    {
        var settings = RumbleSettings.Create(RumbleLow, RumbleHigh, (double)RumbleDuration);
        try
        {
              if (IsWindowsTester) { RumbleResult = await gamepads.RumbleAsync(SelectedWindowsGamepad, settings); return; }
              if (IsMappedTester && IsWindowsBridgeInput) { RumbleResult = await gamepads.RumbleAsync(SelectedBridgeGamepad, settings); return; }
            if (!transport.CanRumble) { RumbleResult = "该输入源没有可用的实体震动通道"; return; }
            rumbleTestCancellation?.Cancel(); rumbleTestCancellation?.Dispose();
            rumbleTestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = rumbleTestCancellation.Token;
            var packet = Pro2OutputPacketMapper.BuildOrdinaryPacket((byte)(settings.High / 257), (byte)(settings.Low / 257), "tester");
            var until = DateTimeOffset.Now.AddMilliseconds(settings.DurationMs);
            RumbleResult = $"BLE 震动测试中 · {settings.DurationMs} ms";
            try
            {
                while (DateTimeOffset.Now < until)
                {
                    token.ThrowIfCancellationRequested(); transport.QueueRumble(packet);
                    await Task.Delay((int)Math.Clamp((until - DateTimeOffset.Now).TotalMilliseconds, 1, 200), token);
                }
                RumbleResult = "BLE 震动测试完成";
            }
            finally { transport.QueueRumble(Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "tester-stop")); }
        }
        catch (OperationCanceledException) { RumbleResult = "震动测试已停止"; }
        catch (TimeoutException) { RumbleResult = "设备响应超时，请重连后再试"; }
    }
    [RelayCommand] private Task StopRumbleAsync()
    {
        if (!IsWindowsTester && !(IsMappedTester && IsWindowsBridgeInput))
            transport.QueueRumble(Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "tester-stop"));
        return StopRumbleSafelyAsync(IsMappedTester && IsWindowsBridgeInput ? SelectedBridgeGamepad : SelectedWindowsGamepad);
    }
    private async Task StopRumbleSafelyAsync(GamepadDevice? pad)
    {
        rumbleTestCancellation?.Cancel();
        if (pad is null) return;
        try { RumbleResult = await gamepads.StopRumbleAsync(pad); }
        catch (TimeoutException) { RumbleResult = "停止指令超时；震动将在设定时长后结束"; }
    }
    private GyroCalibration? CurrentCalibration => IsWindowsTester
        ? SelectedWindowsGamepad is { } pad ? gamepads.Calibration(pad.Id) : null
        : IsMappedTester && IsWindowsBridgeInput ? SelectedBridgeGamepad is { } source ? gamepads.Calibration(source.Id) : null : bleCalibration;
    private void ResetTesterRate()
    {
        rateAt = DateTimeOffset.Now;
        rateBaseline = Interlocked.Read(ref validFrames);
        TesterRateText = "上传速率 — Hz";
        TesterRateHint = "等待输入报文，按最近 1 秒统计。";
    }
    [RelayCommand] private void CalibrateGyro() { if (CanCalibrate) CurrentCalibration?.Start(GyroCalibration.Now); }
    [RelayCommand] private void ResetGyro() => CurrentCalibration?.Reset();
    [RelayCommand] private void CancelCalibration() => CurrentCalibration?.Cancel();
    private void UpdatePreview()
    {
        if (closing) return;
        var now = DateTimeOffset.Now;
        if (now >= nextCandidateExpiryAt)
        {
            ExpireCandidates(now);
            nextCandidateExpiryAt = now.AddSeconds(1);
        }
        if (now >= nextDevicesAt) { SyncGamepads(); nextDevicesAt = now.AddMilliseconds(250); }
        if (scanRequested && now >= nextScanAt) BeginScan();
        ControllerState state;
        lock (frameGate) { state = lastFrame; RawSampleText = Convert.ToHexString(rawSample); }
        long count = Interlocked.Read(ref validFrames);
        FramesText = $"有效 {count} · 无效 {Interlocked.Read(ref invalidFrames)} · 主机反馈 {Interlocked.Read(ref feedbackCount)}";
        bool live = IsConnected && count > 0 && now - state.ReceivedAt < TimeSpan.FromSeconds(2);
        if (live)
        {
            ControllerStateText = "输入正常"; ButtonsText = state.Buttons.ToString();
            SticksText = $"L ({state.LeftX}, {state.LeftY}) · R ({state.RightX}, {state.RightY})";
            ImuText = $"加速度 {state.AccelX}, {state.AccelY}, {state.AccelZ} · 角速度 {state.GyroX}, {state.GyroY}, {state.GyroZ}";
            LastInputText = $"最后输入 {state.ReceivedAt:HH:mm:ss.fff}";
        }
        else if (IsConnected) _ = HandleLostConnectionAsync();
        if (now - rateAt >= TimeSpan.FromSeconds(1))
        {
            BleConnectionTiming = transport.ConnectionTiming;
            double inputHz = (count - rateBaseline) / (now - rateAt).TotalSeconds;
            if (IsConnected && now >= nextBleRateLogAt)
            {
                AddLog("BLE", $"有效输入 {inputHz:F1} Hz；{BleConnectionTiming}；累计无效报文 {Interlocked.Read(ref invalidFrames)}");
                nextBleRateLogAt = now.AddSeconds(10);
            }
            if (!IsWindowsTester)
            {
                TesterRateText = $"上传速率 {inputHz:F1} Hz";
                TesterRateHint = $"按实际有效 BLE 报文计数，不代表虚拟 USB 速率。{BleConnectionTiming}。";
            }
            rateAt = now; rateBaseline = count;
        }
        if (IsWindowsTester)
        {
            var pad = SelectedWindowsGamepad;
            var snapshot = pad is null ? null : gamepads.Read(pad);
            if (snapshot is null) { ClearTester("未连接手柄 · 插入 USB 或唤醒已配对的蓝牙手柄"); return; }
            string rate = snapshot.InputRateHz is { } hz ? $"{hz:F1}" : "—";
            TesterRateText = snapshot.RateUsesSensorReports ? $"上传速率 ≈ {rate} Hz" : $"上传速率 — Hz · 状态更新 {rate} Hz";
            TesterRateHint = snapshot.RateUsesSensorReports
                ? "按最近 1 秒传感器报文接收时间估测，同一报文的多个采样只计一次；不是界面刷新率。"
                : "此设备未提供可统计的连续报文。显示 SDL 状态变化频率，静止时可为 0；不代表硬件上传率。";
            TesterSummary = pad!.Name;
            int[] buttonIndices = Enumerable.Range(0, snapshot.RawButtons.Length).Where(i => snapshot.SupportedButtons[i]).ToArray();
            TesterDeviceInfo = $"{pad.Vendor:X4}:{pad.Product:X4} · {pad.Layout.Name()} · {(snapshot.Standard ? "标准布局" : "原始 HID 布局（按钮编号）")} · {buttonIndices.Length} 按键 / {snapshot.Axes.Length} 轴";
            TesterIsOnline = true; TesterLayout = pad.Layout; TesterButtons = snapshot.Buttons; TesterBatteryPercent = snapshot.Battery;
            TesterLeftX = snapshot.Axes.ElementAtOrDefault(0); TesterLeftY = -snapshot.Axes.ElementAtOrDefault(1);
            TesterRightX = snapshot.Axes.ElementAtOrDefault(2); TesterRightY = -snapshot.Axes.ElementAtOrDefault(3);
            TesterLeftTrigger = snapshot.Triggers.Left; TesterRightTrigger = snapshot.Triggers.Right;
            string[] buttonLabels = pad.Layout.ButtonLabels();
            string[] axisLabels = snapshot.Standard ? ["左 X", "左 Y", "右 X", "右 Y", pad.Nintendo ? "ZL" : pad.Layout.IsPlayStation() ? "L2" : "LT", pad.Nintendo ? "ZR" : pad.Layout.IsPlayStation() ? "R2" : "RT"] : [];
            SetValues(TesterButtonValues,
                buttonIndices.Select(i => snapshot.RawButtons[i] ? 1.0 : 0.0).Concat(snapshot.Standard ? new[] { TesterLeftTrigger, TesterRightTrigger } : []).ToArray(), "B",
                buttonIndices.Select(i => snapshot.Standard ? buttonLabels[i] : $"B{i}").Concat(snapshot.Standard ? axisLabels.Skip(4) : []).ToArray());
            SetValues(TesterAxisValues, snapshot.Axes.Concat(snapshot.Hats.Select(x => (double)x)).ToArray(), "轴 ",
                Enumerable.Range(0, snapshot.Axes.Length).Select(i => axisLabels.ElementAtOrDefault(i) ?? $"轴 {i}").Concat(Enumerable.Range(0, snapshot.Hats.Length).Select(i => $"Hat {i}")).ToArray());
            UpdateMotion(gamepads.Calibration(pad.Id), snapshot.Gyro is not null && snapshot.Accel is not null);
            return;
        }
        if (IsMappedTester) { UpdateMappedTester(); return; }
        if (!live) { ClearTester("等待 NS2 Pro · 已连接过可尝试 L + R 唤醒；首次或回连失败请长按 SYNC"); return; }
        TesterIsOnline = true; TesterLayout = ControllerLayout.Switch2Pro;
        TesterButtons = state.Buttons;
        var triggers = TriggerLevels.FromDigital(TesterButtons);
        TesterLeftTrigger = triggers.Left; TesterRightTrigger = triggers.Right;
        TesterLeftX = StickCoordinates.Unit(state.LeftX); TesterLeftY = StickCoordinates.Unit(state.LeftY);
        TesterRightX = StickCoordinates.Unit(state.RightX); TesterRightY = StickCoordinates.Unit(state.RightY);
        TesterBatteryPercent = Volatile.Read(ref ns2BatteryPercent);
        TesterSummary = "NS2 Pro · 实体蓝牙输入";
        TesterDeviceInfo = "实时按键、摇杆和传感器原始数据";
        var buttons = Enum.GetValues<ControllerButtons>().Skip(1).Where(b => b != ControllerButtons.Touchpad).ToArray();
        SetValues(TesterButtonValues, buttons.Select(b => (TesterButtons & b) != 0 ? 1.0 : 0.0).ToArray(), "B", buttons.Select(b => b.ToString()).ToArray());
        SetValues(TesterAxisValues, [TesterLeftX, TesterLeftY, TesterRightX, TesterRightY], "轴 ");
        UpdateMotion(bleCalibration, true);
    }
    private void UpdateMotion(GyroCalibration? calibration, bool supported)
    {
        var reading = calibration?.Read(GyroCalibration.Now);
        bool available = supported && reading is { Available: true };
        CanCalibrate = available && !reading!.Collecting;
        IsCalibrating = supported && reading is { Collecting: true };
        CalibrationProgress = reading?.Progress ?? 0;
        GyroStatus = !supported ? "当前输入源未提供陀螺仪和加速度计" : !available ? "等待新的传感器数据 · " + reading?.Status : reading!.Status;
        GyroRawText = available ? VectorText(reading!.RawGyro) : "—";
        GyroCorrectedText = available ? VectorText(reading!.CorrectedGyro) : "—";
        GyroBiasText = reading is { Calibrated: true } ? VectorText(reading.Bias) : "未设置零偏";
        UpdateMotionPanel(reading, available);
        SetMotion(available ? [reading!.AccelG.X, reading.AccelG.Y, reading.AccelG.Z] : null,
            available ? $"加速度 {VectorText(reading!.AccelG)} g" : "无传感器数据");
    }
    private static string VectorText(Vector3 v) => $"X {v.X:F2}  Y {v.Y:F2}  Z {v.Z:F2}";
    private void SetMotion(double[]? accel, string text)
    {
        TesterHasMotion = accel is { Length: 3 };
        TesterMotionText = TesterHasMotion ? text : "当前输入源未提供姿态数据";
    }
    private static void SetValues(ObservableCollection<TesterValue> target, double[] values, string prefix, string[]? labels = null)
    {
        while (target.Count > values.Length) target.RemoveAt(target.Count - 1);
        for (int i = 0; i < values.Length; i++)
        {
            string label = labels?.ElementAtOrDefault(i) ?? prefix + i;
            if (i >= target.Count) target.Add(new(label));
            else if (target[i].Label != label) target[i] = new(label);
            target[i].Value = values[i];
        }
    }
    private void ClearTester(string message)
    {
        TesterRateText = "上传速率 0 Hz";
        TesterRateHint = "当前没有连接的输入设备。";
        TesterSummary = message; TesterDeviceInfo = "等待设备 · NS1 / Xbox / PlayStation / 通用 HID"; TesterIsOnline = false;
        TesterButtons = ControllerButtons.None; TesterBatteryPercent = -1;
        TesterLeftX = TesterLeftY = TesterRightX = TesterRightY = 0;
        TesterLeftTrigger = TesterRightTrigger = 0;
        SetValues(TesterButtonValues, new double[18], "B"); SetValues(TesterAxisValues, new double[4], "轴 ");
        CurrentCalibration?.Cancel("输入已断开"); UpdateMotion(null, false);
    }
    private string DescribeAdvertisementTarget(BleCandidate candidate) => candidate.Advertisement switch
    {
        null => "未读取到",
        { IsPairing: true } => "未指定（SYNC）",
        _ when transport.LocalAddress == 0 => "本机适配器地址未知，无法比较",
        var advertisement => advertisement.TargetHost == transport.LocalAddress ? "与本机一致" : "与本机不同"
    };
    private void RemoveCandidate(BleCandidate device)
    {
        for (int i = Candidates.Count - 1; i >= 0; i--)
            if (Candidates[i].Address == device.Address && Candidates[i].AddressType == device.AddressType) Candidates.RemoveAt(i);
        if (SelectedCandidate is { } selected && selected.Address == device.Address && selected.AddressType == device.AddressType)
            SelectedCandidate = null;
    }
    private void ExpireCandidates(DateTimeOffset now)
    {
        for (int i = Candidates.Count - 1; i >= 0; i--)
            if (!BleCandidateLifetime.IsVisible(Candidates[i], now, activeBleCandidate, IsConnected))
                RemoveCandidate(Candidates[i]);
    }
    private void ResetInput(string stage)
    {
        IsConnected = false; ControllerStage = stage; ControllerStateText = "尚未收到输入";
        bleCalibration.Cancel("蓝牙输入已断开");
        stickCalibration.Cancel("输入未连接；连接 NS2 BLE 后可校准中心与行程。");
        ButtonsText = "无"; LastInputText = "等待有效输入"; ImuText = "暂无数据";
        Interlocked.Exchange(ref validFrames, 0); Interlocked.Exchange(ref invalidFrames, 0); rateBaseline = 0; rateAt = DateTimeOffset.Now;
        lock (frameGate) { lastFrame = ControllerState.Neutral(DateTimeOffset.Now); rawSample = []; lastMotionTimestamp = null; }
        inputBridge?.DisconnectBle();
    }
    private void Ui(Action action) => Dispatcher.UIThread.Post(() => { if (!closing) action(); });
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(CanUninstallDriver)) UninstallDriverCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(CanManageDriver))
        {
            InstallDriverCommand.NotifyCanExecuteChanged();
            RefreshDriverStateCommand.NotifyCanExecuteChanged();
        }
    }
    private void AddLog(string area, string message)
    {
        ApplicationLog.Current.Write(area, message);
        OnPropertyChanged(nameof(LogWriteStatus));
        Logs.Insert(0, $"{DateTimeOffset.Now:HH:mm:ss} [{area}] {message}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    }
    public async Task ShutdownAsync()
    {
        if (closing) return;
        closing = true; lifetime.Cancel(); connectionCancellation?.Cancel(); timer.Stop(); transport.StopScan(); Volatile.Write(ref acceptingFrames, 0);
        rumbleTestCancellation?.Cancel();
        gamepads.FrameUpdated -= OnWindowsBridgeFrame; gamepads.ClearFeedback();
        if (connectingTask is not null) await connectingTask;
        try { await transport.DisconnectAsync(); }
        finally
        {
            try { await output.StopAsync(); }
            finally { await gamepads.DisposeAsync(); Dispose(); }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; transport.Dispose(); connectionCancellation?.Dispose(); lifetime.Dispose();
    }
}

public partial class TesterValue(string label) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] public partial double Value { get; set; }
}
