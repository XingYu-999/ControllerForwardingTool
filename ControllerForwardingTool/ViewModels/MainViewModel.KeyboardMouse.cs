using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;
using System.Numerics;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private KeyboardMouseMonitor? keyboardMouse;
    public IReadOnlyList<string> MouseModes { get; } = ["右摇杆（默认）", "陀螺仪"];
    [ObservableProperty] public partial string SelectedMouseMode { get; set; } = "右摇杆（默认）";
    private MouseEmulationMode CurrentMouseMode => SelectedMouseMode == MouseModes[1] ? MouseEmulationMode.Gyroscope : MouseEmulationMode.RightStick;
    public IReadOnlyList<string> GyroSources { get; } = ["自动（优先手柄，无体感时使用鼠标）", "实体手柄", "鼠标"];
    [ObservableProperty] public partial string SelectedGyroSource { get; set; } = "自动（优先手柄，无体感时使用鼠标）";
    [ObservableProperty] public partial bool KeyboardMouseSupplementEnabled { get; set; }
    private bool CanUseKeyboardMouse => IsKeyboardMouseInput || KeyboardMouseSupplementEnabled;
    public string KeyboardSupplementHint => KeyboardMouseSupplementEnabled
        ? "补充已开启：已配置的键鼠按键和鼠标体感参与本页测试。实际转发按 F8 开始，Esc / F8 暂停；摇杆始终来自手柄。"
        : "默认关闭：仅使用实体手柄输入，键鼠补充绑定仍保留。开启后可测试已配置的键鼠按键和鼠标体感。";
    partial void OnKeyboardMouseSupplementEnabledChanged(bool value)
    {
        HasMappingChanges = true;
        // This is an input enable/disable switch, not an apply of other pending mapping edits.
        // Clear any held supplementary input immediately; saving retains the preference per route.
        if (IsServerRunning && !IsKeyboardMouseInput)
        {
            output.Options = output.Options with { KeyboardMouseSupplementEnabled = value };
            inputBridge?.ClearSupplement();
        }
        ConfigureKeyboardMouse();
        OnPropertyChanged(nameof(KeyboardSupplementHint)); OnPropertyChanged(nameof(HybridGyroStatus));
        OnPropertyChanged(nameof(KeyboardMouseStatus));
        UpdateKeyboardMouseTestState();
        ButtonMappingStatus = value ? "键鼠补充已开启；点击应用并保存可保留开关设置。" : "键鼠补充已关闭，实体手柄继续工作；点击应用并保存可保留设置。";
    }
    private GyroInputSource CurrentGyroSource => SelectedGyroSource == GyroSources[2] ? GyroInputSource.Mouse
        : SelectedGyroSource == GyroSources[1] ? GyroInputSource.Controller : GyroInputSource.Automatic;
    private bool BridgeControllerHasMotion => !IsKeyboardMouseInput && (IsWindowsBridgeInput
        ? SelectedBridgeGamepad is { } pad && gamepads.Read(pad) is { Accel: not null, Gyro: not null }
        : IsConnected || IsNs2UsbConnected);
    public bool CanConfigureHybridGyro => !IsKeyboardMouseInput && EditedOutputMode != VirtualControllerMode.Xbox360;
    private bool UsesHybridMouseGyro => output.Options.KeyboardMouseSupplementEnabled && HybridInputMapper.UsesMouse(output.Options.GyroSource, BridgeControllerHasMotion, output.Mode);
    public string HybridGyroStatus => !KeyboardMouseSupplementEnabled ? "键鼠补充已关闭 · 保留实体手柄的按键、摇杆和可用体感。"
        : EditedOutputMode == VirtualControllerMode.Xbox360 ? "此输出不支持陀螺仪。键盘仍可补充按钮，摇杆仅使用实体手柄。"
        : HybridInputMapper.UsesMouse(CurrentGyroSource, BridgeControllerHasMotion, EditedOutputMode)
            ? "配置来源：鼠标 → 陀螺仪。保存后按 F8 开启键鼠补充；摇杆仍由实体手柄控制。"
            : BridgeControllerHasMotion ? "配置来源：实体手柄陀螺仪。可改为鼠标；修改后应用并保存。"
            : "配置来源：实体手柄，但当前没有可用体感。可选择自动或鼠标。";
    public string KeyboardBindingHint => IsKeyboardMouseInput
        ? "新绑定优先于该键的默认功能。移除并保存可恢复默认；WASD 绑定为按键后，该键不再控制左摇杆。"
        : "双击输出按键或映射卡片配置键鼠来源。仅转发已配置的补充按键；关闭补充不会删除绑定。";
    private string ActiveMouseLabel => IsKeyboardMouseInput ? SelectedMouseMode : UsesHybridMouseGyro ? "陀螺仪" : "未用于体感";
    partial void OnSelectedGyroSourceChanged(string value)
    { HasMappingChanges = true; OnPropertyChanged(nameof(HybridGyroStatus)); }
    public bool IsKeyboardMouseCaptured => keyboardMouse?.IsCaptured == true;
    public string KeyboardMouseStatus => !CanUseKeyboardMouse ? "键鼠补充已关闭 · 在按键映射页开启后才可使用 F8 或键鼠测试。"
        : !IsServerRunning ? "键鼠输入已就绪 · 启动后按 F8 开始控制"
        : IsKeyboardMouseCaptured ? $"键鼠{(IsKeyboardMouseInput ? "控制" : "补充")}中 · 鼠标 → {ActiveMouseLabel} · F8 / Esc 释放"
        : IsKeyboardMouseInput ? "键鼠控制已暂停 · 按 F8 开始，或在手柄测试页点击开始键鼠测试"
        : "实体手柄正常转发 · 键鼠补充已暂停，按 F8 开始，或在手柄测试页点击开始键鼠测试";
    partial void OnSelectedMouseModeChanged(string value) { HasMappingChanges = true; ConfigureKeyboardMouse(); }
    private void ConfigureKeyboardMouse() => keyboardMouse?.Configure(IsServerRunning && CanUseKeyboardMouse && !IsVirtualBusy && !IsListeningForMapping && !closing,
        IsKeyboardMouseInput ? CurrentMouseMode : MouseEmulationMode.Gyroscope);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartKeyboardMouseTestCommand))]
    public partial bool CanStartKeyboardMouseTest { get; set; }
    [ObservableProperty] public partial string TesterKeyboardMouseStatus { get; set; } = "";
    private string keyboardMouseTestError = "";
    private bool IsTestingCurrentVirtualOutput => IsWindowsTester && SelectedWindowsGamepad is { } pad &&
        inputGuard.IsOwned(pad) && pad.Vendor == VirtualProfile.Get(output.Mode).Vendor &&
        pad.Product == VirtualProfile.Get(output.Mode).Product && gamepads.Read(pad) is not null;

    private void UpdateKeyboardMouseTestState()
    {
        bool captured = IsKeyboardMouseCaptured;
        bool ready = IsTester && IsServerRunning && !IsVirtualBusy &&
            CanUseKeyboardMouse && !IsListeningForMapping && !closing && IsTestingCurrentVirtualOutput;
        CanStartKeyboardMouseTest = ready && !captured;
        if (captured || !ready) keyboardMouseTestError = "";
        TesterKeyboardMouseStatus = !IsServerRunning ? "虚拟输出未启动 · 请先在虚拟手柄页启动输出，再选择本应用的模拟手柄。"
            : IsVirtualBusy ? "正在准备虚拟输出，请稍候…"
            : !IsTestingCurrentVirtualOutput ? "请在上方选择当前输出的「模拟手柄 · 本应用」；键鼠不会改变其他系统手柄的状态。"
            : !CanUseKeyboardMouse ? "键鼠补充已关闭 · 请在按键映射页开启补充开关后再测试。"
            : captured ? $"正在发送键鼠输入 → {VirtualProfile.Get(output.Mode).Name} · 下方显示 Windows 实际接收状态 · Esc / F8 结束"
            : keyboardMouseTestError.Length > 0 ? keyboardMouseTestError
            : "键鼠转发已暂停 · 点击开始键鼠测试或按 F8，按键与鼠标移动才会发送到虚拟手柄。";
        OnPropertyChanged(nameof(KeyboardMouseStatus));
        OnPropertyChanged(nameof(HybridGyroStatus));
        OnPropertyChanged(nameof(CanConfigureHybridGyro));
    }

    [RelayCommand(CanExecute = nameof(CanStartKeyboardMouseTest))]
    private void StartKeyboardMouseTest()
    {
        UpdateKeyboardMouseTestState();
        if (!CanStartKeyboardMouseTest) return;
        keyboardMouseTestError = keyboardMouse?.TryStartCapture() == true ? ""
            : "未能开启键鼠测试 · 请保持本窗口在前台，松开 Esc / Alt / Windows 键后重试。";
        UpdateKeyboardMouseTestState();
    }

    internal void StopKeyboardMouseCapture()
    {
        keyboardMouse?.StopCapture();
        UpdateKeyboardMouseTestState();
    }

    private void SelectAvailableInput(BridgeInputKind kind)
    {
        bool available = kind switch
        {
            BridgeInputKind.WindowsGamepad => SelectedBridgeGamepad is { } pad && inputGuard.Allows(pad) && gamepads.Read(pad) is not null,
            BridgeInputKind.Ns2Ble => IsConnected || IsNs2UsbConnected,
            _ => true
        };
        BridgeInputSelection = BridgeInputKinds[available ? (int)kind : 2];
    }

    private void UpdateKeyboardMouseMotion(ControllerState state)
    {
        UpdateMotion(null, false);
        if ((IsKeyboardMouseInput ? CurrentMouseMode != MouseEmulationMode.Gyroscope : !UsesHybridMouseGyro) || TesterPreviewMode == VirtualControllerMode.Xbox360) return;
        var gyro = MotionCoordinates.FromNs2(new Vector3(state.GyroX, state.GyroY, state.GyroZ) / 16.384f);
        var accel = MotionCoordinates.FromNs2(new Vector3(state.AccelX, state.AccelY, state.AccelZ) / 4096f);
        GyroPitchDps = gyro.X; GyroYawDps = gyro.Y; GyroRollDps = gyro.Z;
        GyroStatus = "鼠标模拟陀螺仪 · 无需实体传感器校准";
        GyroRawText = GyroCorrectedText = VectorText(gyro);
        GyroBiasText = "键鼠模拟无零偏";
        SetMotion([accel.X, accel.Y, accel.Z], "鼠标模拟体感");
    }

    private static IImage KeyboardMouseImage() => new DrawingImage
    {
        Drawing = new GeometryDrawing
        {
            Brush = Brushes.WhiteSmoke,
            Pen = new Pen(Brush.Parse("#526784"), 2),
            Geometry = Geometry.Parse("M 4,8 L 104,8 104,58 4,58 Z M 14,19 L 24,19 M 32,19 L 42,19 M 50,19 L 60,19 M 68,19 L 78,19 M 86,19 L 96,19 M 14,31 L 24,31 M 32,31 L 42,31 M 50,31 L 60,31 M 68,31 L 78,31 M 86,31 L 96,31 M 26,45 L 82,45 M 132,8 C 112,8 112,22 112,33 C 112,52 118,58 132,58 C 146,58 152,52 152,33 C 152,22 152,8 132,8 Z M 132,8 L 132,29 M 112,29 L 152,29")
        }
    };
}
