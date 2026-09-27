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
    public IReadOnlyList<string> BridgeInputKinds { get; } = ["NS2 Pro · USB / 蓝牙", "Windows 手柄 · USB / 系统蓝牙", "键盘 / 鼠标"];
    public ObservableCollection<GamepadDevice> BridgeInputDevices { get; } = [];
    [ObservableProperty] public partial string BridgeInputSelection { get; set; } = "键盘 / 鼠标";
    [ObservableProperty] public partial GamepadDevice? SelectedBridgeGamepad { get; set; }
    [ObservableProperty] public partial string BridgeSourceStatus { get; set; } = "等待输入源";
    [ObservableProperty] public partial bool IsBridgeSourceReady { get; set; }
    public bool IsWindowsBridgeInput => BridgeInputSelection == BridgeInputKinds[1];
    public bool IsKeyboardMouseInput => BridgeInputSelection == BridgeInputKinds[2];
    private BridgeInputKind CurrentInputKind => IsKeyboardMouseInput ? BridgeInputKind.KeyboardMouse
        : IsWindowsBridgeInput ? BridgeInputKind.WindowsGamepad : BridgeInputKind.Ns2Ble;
    public string BridgeSourceHint => IsKeyboardMouseInput
        ? "无需实体手柄。启动输出后切换到游戏，按 F8 开始 / 暂停键鼠控制，Esc 释放鼠标；切换窗口自动暂停。键鼠不支持振动。"
        : IsWindowsBridgeInput
        ? "按物理位置映射面键，摇杆由实体手柄控制。运行中锁定输入选择，断开后自动归零；连接成功后可在按键映射页配置输入。"
        : "在 NS2 Pro 连接页选择 USB 手柄，或通过蓝牙连接。USB 注册需手动点击；拔线后开启自动连接即可等待蓝牙回连。";
    private void InitializeInputBridge()
    {
        inputBridge = new(output.Publish, () => output.Options, id=>gamepads.Calibration(id).Read(GyroCalibration.Now));
        keyboardMouse = new KeyboardMouseMonitor(state =>
        {
            var keys = keyboardMouse?.PressedKeys ?? new HashSet<int>();
            inputBridge.KeyboardMouse(state, keys);
            inputBridge.Supplement(state, keys);
        });
        SelectAvailableInput(bridgeOptions.InputKind);
        ConfigureInputBridge();
        gamepads.FrameUpdated += OnWindowsBridgeFrame;
    }
    private void OnWindowsBridgeFrame(GamepadFrame frame) => inputBridge.Windows(frame,GyroCalibration.Now);
    partial void OnBridgeInputSelectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsWindowsBridgeInput)); OnPropertyChanged(nameof(IsKeyboardMouseInput)); OnPropertyChanged(nameof(BridgeSourceHint));
        BridgeInputRumbleResult = "";
        OnPropertyChanged(nameof(CanStartVirtual)); ConfigureInputBridge();
        OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint));
    }
    partial void OnSelectedBridgeGamepadChanged(GamepadDevice? value)
    { OnPropertyChanged(nameof(CanStartVirtual)); ConfigureInputBridge(); OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint)); }
    private void ConfigureInputBridge()
    {
        CancelMappingCapture();
        RefreshMappingSourceLabels();
        OnPropertyChanged(nameof(KeyboardBindingHint)); OnPropertyChanged(nameof(CanConfigureHybridGyro)); OnPropertyChanged(nameof(HybridGyroStatus));
        OnPropertyChanged(nameof(MappingSourceColumnSpan)); OnPropertyChanged(nameof(MappingOutputColumn)); OnPropertyChanged(nameof(MappingOutputRow));
        UpdateBridgeInputCardSelection();
        if (inputBridge is null) return;
        gamepads.ClearFeedback();
        transport.QueueRumble(Pro2OutputPacketMapper.BuildOrdinaryPacket(0,0,"input-switch"));
        var kind = CurrentInputKind;
        Volatile.Write(ref feedbackTarget,new(kind,SelectedBridgeGamepad));
        inputBridge.Select(kind,SelectedBridgeGamepad);
        ConfigureKeyboardMouse();
        UpdateNs2UsbAccess();
    }
    private void UpdateNs2UsbAccess() => gamepads.EnableNs2Usb(IsBluetooth || !IsWindowsBridgeInput ||
        IsWindowsBridgeInput && (SelectedBridgeGamepad is null || SelectedBridgeGamepad.Layout == ControllerLayout.Switch2Pro) ||
        IsTesterSection || IsVirtual);
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
        IsBridgeSourceReady = IsKeyboardMouseInput || (IsWindowsBridgeInput ? snapshot is not null : IsConnected || IsNs2UsbConnected);
        BridgeSourceStatus = IsKeyboardMouseInput ? KeyboardMouseStatus : !IsWindowsBridgeInput ? IsNs2UsbConnected ? "NS2 Pro · USB 输入就绪" : ControllerStage : current is null ? "未选择实体手柄；启动时将使用键鼠输入"
            : snapshot is null ? $"{current.Name} 已断开 · 输出保持归零" : $"{current.Name} · 输入就绪 · {(snapshot.Accel is not null && snapshot.Gyro is not null ? "含体感" : "无体感")}";
        SyncBridgeInputCards();
    }
    private void OnBridgeRumble(Pro2OutputPacket packet)
    {
        var target = Volatile.Read(ref feedbackTarget);
        if (target.Kind == BridgeInputKind.Ns2Ble)
        {
            var usb = Volatile.Read(ref ns2UsbFeedbackDevice);
            if (usb is not null && gamepads.Read(usb) is not null) gamepads.QueueFeedback(usb, WindowsRumble.FromPacket(packet, output.Options.RumbleGain));
            else transport.QueueRumble(packet);
        }
        else if (target.Kind == BridgeInputKind.WindowsGamepad && target.Device is { } pad) gamepads.QueueFeedback(pad,WindowsRumble.FromPacket(packet,output.Options.RumbleGain));
    }
}
