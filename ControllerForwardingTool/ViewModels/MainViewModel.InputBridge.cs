using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private ControllerInputBridge inputBridge = null!;
    private readonly BridgeInputGuard inputGuard = new();
    private sealed record FeedbackTarget(BridgeInputKind Kind, GamepadDevice? Device);
    private FeedbackTarget feedbackTarget = new(BridgeInputKind.Ns2Ble,null);
    public IReadOnlyList<string> BridgeInputKinds { get; } = ["NS2 Pro · 直连蓝牙", "Windows 手柄 · USB / 系统蓝牙"];
    public ObservableCollection<GamepadDevice> BridgeInputDevices { get; } = [];
    [ObservableProperty] public partial string BridgeInputSelection { get; set; } = "NS2 Pro · 直连蓝牙";
    [ObservableProperty] public partial GamepadDevice? SelectedBridgeGamepad { get; set; }
    [ObservableProperty] public partial string BridgeSourceStatus { get; set; } = "等待输入源";
    [ObservableProperty] public partial bool IsBridgeSourceReady { get; set; }
    public bool IsWindowsBridgeInput => BridgeInputSelection == BridgeInputKinds[1];
    public string BridgeSourceHint => IsWindowsBridgeInput
        ? "先选择实体手柄，再启动输出。按物理位置映射面键；缺少的体感、背键等保持无输入。运行中锁定输入选择，断开后自动归零。"
        : "首次连接请长按顶部 SYNC。已连接过可尝试 L + R 唤醒回连；无响应时再长按 SYNC。";
    private void InitializeInputBridge()
    {
        inputBridge = new(output.Publish, () => output.Options, id=>gamepads.Calibration(id).Read(GyroCalibration.Now));
        BridgeInputSelection = BridgeInputKinds[bridgeOptions.InputKind == BridgeInputKind.WindowsGamepad ? 1 : 0];
        ConfigureInputBridge();
        gamepads.FrameUpdated += OnWindowsBridgeFrame;
    }
    private void OnWindowsBridgeFrame(GamepadFrame frame) => inputBridge.Windows(frame,GyroCalibration.Now);
    partial void OnBridgeInputSelectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsWindowsBridgeInput)); OnPropertyChanged(nameof(BridgeSourceHint));
        OnPropertyChanged(nameof(CanStartVirtual)); ConfigureInputBridge();
        OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint));
    }
    partial void OnSelectedBridgeGamepadChanged(GamepadDevice? value)
    { OnPropertyChanged(nameof(CanStartVirtual)); ConfigureInputBridge(); OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint)); }
    private void ConfigureInputBridge()
    {
        if (inputBridge is null) return;
        gamepads.ClearFeedback();
        transport.QueueRumble(Pro2OutputPacketMapper.BuildOrdinaryPacket(0,0,"input-switch"));
        var kind = IsWindowsBridgeInput ? BridgeInputKind.WindowsGamepad : BridgeInputKind.Ns2Ble;
        Volatile.Write(ref feedbackTarget,new(kind,SelectedBridgeGamepad));
        inputBridge.Select(kind,SelectedBridgeGamepad);
        UpdateNs2UsbAccess();
    }
    private void UpdateNs2UsbAccess() => gamepads.EnableNs2Usb(
        IsWindowsBridgeInput && (SelectedBridgeGamepad is null || SelectedBridgeGamepad.Layout == ControllerLayout.Switch2Pro) ||
        IsTesterSection);
    private void SyncBridgeInputs(IReadOnlyList<GamepadDevice> devices)
    {
        inputGuard.Observe(devices);
        var allowed = devices.Where(inputGuard.Allows).ToList();
        // Retain a disconnected running source in the selector. Never silently fall back to another pad.
        var selected = SelectedBridgeGamepad;
        if (!CanEditMode && selected is not null && allowed.All(d=>d.Id != selected.Id)) allowed.Insert(0,selected);
        if (!BridgeInputDevices.SequenceEqual(allowed))
        {
            BridgeInputDevices.Clear(); foreach(var pad in allowed) BridgeInputDevices.Add(pad);
            SelectedBridgeGamepad = allowed.FirstOrDefault(d=>d.Id == selected?.Id);
        }
        var current = SelectedBridgeGamepad;
        var snapshot = current is null ? null : gamepads.Read(current);
        IsBridgeSourceReady = IsWindowsBridgeInput ? snapshot is not null : IsConnected;
        BridgeSourceStatus = !IsWindowsBridgeInput ? ControllerStage : current is null ? "请选择输入手柄；不自动选择虚拟设备"
            : snapshot is null ? $"{current.Name} 已断开 · 输出保持归零" : $"{current.Name} · 输入就绪 · {(snapshot.Accel is not null && snapshot.Gyro is not null ? "含体感" : "无体感，相关输出为零")}";
    }
    private void OnBridgeRumble(Pro2OutputPacket packet)
    {
        var target = Volatile.Read(ref feedbackTarget);
        if (target.Kind == BridgeInputKind.Ns2Ble) transport.QueueRumble(packet);
        else if (target.Device is { } pad) gamepads.QueueFeedback(pad,WindowsRumble.FromPacket(packet,output.Options.RumbleGain));
    }
}
