using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private bool isMappingAvailable;
    public bool IsMappingAvailable => isMappingAvailable;
    public bool IsMapping => IsMappingAvailable && SelectedPage == "按键映射";
    public bool CanMapSource => IsMappingAvailable;
    private void UpdateMappingAvailability()
    {
        var profile = VirtualProfile.Get(output.Mode);
        bool available = IsServerRunning && output.IsRunning && !IsVirtualBusy &&
            gamepads.Latest.Devices.Any(pad => inputGuard.IsOwned(pad) &&
                pad.Vendor == profile.Vendor && pad.Product == profile.Product);
        if (!SetProperty(ref isMappingAvailable, available, nameof(IsMappingAvailable))) return;
        OpenMappingPreviewCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanMapSource));
        OnPropertyChanged(nameof(CanCaptureSelectedOutput));
        if (!available && SelectedPage == "按键映射") SelectedPage = "虚拟手柄";
        OnPropertyChanged(nameof(IsMapping));
    }
    private ControllerLayout MappingSourceLayout => IsKeyboardMouseInput ? ControllerLayout.Xbox
        : IsWindowsBridgeInput ? SelectedBridgeGamepad?.Layout ?? ControllerLayout.Generic : ControllerLayout.Switch2Pro;
    [ObservableProperty] public partial ButtonMappingRow? SelectedMapping { get; set; }
    [ObservableProperty] public partial MappingDiagram MappingSource { get; set; } = MappingDiagram.Empty;
    [ObservableProperty] public partial MappingDiagram MappingOutput { get; set; } = MappingDiagram.Empty;
    [ObservableProperty] public partial string MappingSourceName { get; set; } = "NS2 Pro";
    [ObservableProperty] public partial string MappingOutputName { get; set; } = "映射结果";
    [ObservableProperty] public partial bool HasMappingChanges { get; set; }
    private ControllerButtons? inspectedOutput;
    private MappingOrigins SelectedOrigins => MappingLookup.Find(SelectedOutputButton, EditedOutputMode, IsKeyboardMouseInput,
        CaptureButtonMappings(), keyboardDraft, keyboardStickDraft);
    public ControllerButtons SelectedSourceButton => ControllerLayouts.RemapFaceButtons(inspectedOutput is null
        ? SelectedMapping?.Source ?? ControllerButtons.None : SelectedOrigins.Buttons, true, MappingSourceLayout.IsNintendo());
    public ControllerButtons SelectedOutputButton => inspectedOutput ?? MappingPreview.OutputButtons(SelectedMapping?.Target.Button ?? ControllerButtons.None, EditedOutputMode);
    public int[] MappingSelectedKeys => SelectedOrigins.Keys;
    public bool CanCaptureSelectedOutput => CanMapSource && SelectedOutputButton != ControllerButtons.None &&
        Enum.GetValues<ControllerButtons>().Any(b => MappingPreview.OutputButtons(b, EditedOutputMode) == SelectedOutputButton);
    public string MappingSelectionText
    {
        get
        {
            var origins = SelectedOrigins;
            var labels = ButtonMappings.Where(r => origins.Buttons.HasFlag(r.Source)).Select(r => "手柄 " + r.Label)
                .Concat(origins.Keys.Select(KeyboardMapping.KeyName)).ToArray();
            return "所选输出按钮的输入源（当前配置）：" + (labels.Length == 0 ? "未绑定" : string.Join("、", labels));
        }
    }
    public string MappingEditHint => IsKeyboardMouseInput
        ? "上方显示键鼠实时状态。双击下方输出按键可单独录入；点击快速配置，依次录入摇杆四个方向和按键。也可使用下拉框编辑默认按键映射。"
        : "单击输出按钮查看对应输入源，双击只配置该按键；点击快速配置可逐项配置。摇杆仅接受实体摇杆；按钮可使用手柄、键盘或鼠标按键。";
    partial void OnSelectedMappingChanged(ButtonMappingRow? value) { inspectedOutput = null; NotifyMappingSelection(); }
    private void NotifyMappingSelection()
    {
        OnPropertyChanged(nameof(SelectedSourceButton)); OnPropertyChanged(nameof(SelectedOutputButton)); OnPropertyChanged(nameof(HighlightedMappingTarget));
        OnPropertyChanged(nameof(MappingSelectedKeys)); OnPropertyChanged(nameof(MappingSelectionText)); OnPropertyChanged(nameof(CanCaptureSelectedOutput));
    }
    [RelayCommand] private void SelectMappingButton(ControllerButtons button)
    {
        inspectedOutput = null; MappingSelectedKey = 0;
        var native = ControllerLayouts.RemapFaceButtons(button, MappingSourceLayout.IsNintendo(), true);
        SelectedMapping = ButtonMappings.FirstOrDefault(x => x.Source == native) ?? SelectedMapping;
        NotifyMappingSelection();
    }
    [RelayCommand] private void SelectOutputMappingButton(ControllerButtons button)
    {
        var row = ButtonMappings.FirstOrDefault(r => MappingPreview.OutputButtons(r.Target.Button, EditedOutputMode) == button);
        if (row is not null) SelectedMapping = row;
        inspectedOutput = button; MappingSelectedKey = 0;
        NotifyMappingSelection();
    }
    [RelayCommand] private void SelectMappingKey(int key)
    {
        inspectedOutput = MappingPreview.OutputButtons(MappingLookup.KeyTarget(key, IsKeyboardMouseInput,
            CaptureButtonMappings(), keyboardDraft, keyboardStickDraft), EditedOutputMode);
        MappingSelectedKey = key;
        NotifyMappingSelection();
    }
    private void UpdateMappingComparison()
    {
        if (!IsMapping) return;
        var now = DateTimeOffset.Now;
        var raw = inputBridge?.Comparison.Source ?? ControllerState.Neutral(DateTimeOffset.MinValue);
        var draft = CaptureRouteDraft().ApplyTo(bridgeOptions) with { Mode = EditedOutputMode };
        var keyboard = IsKeyboardMouseCaptured ? keyboardMouse?.Latest : mappingKeyboardPreview;
        var keys = IsKeyboardMouseCaptured ? keyboardMouse!.PressedKeys : MappingPressedKeys.ToHashSet();
        var profile = !IsWindowsBridgeInput && !IsNs2UsbConnected ? activeStickProfile : null;
        var pair = MappingDraftPreview.Create(raw, keyboard, keys, IsKeyboardMouseInput, draft, profile, BridgeControllerHasMotion, now);
        bool live = now - pair.Source.ReceivedAt < TimeSpan.FromMilliseconds(250);
        var sourceLayout = MappingSourceLayout;
        MappingSourceName = IsKeyboardMouseInput ? "键盘 / 鼠标" : IsWindowsBridgeInput ? SelectedBridgeGamepad?.Name ?? "Windows 手柄 · 离线配置" : "NS2 Pro · USB / 蓝牙输入";
        MappingOutputName = VirtualProfile.Get(EditedOutputMode).Name + " · 当前配置测试";
        MappingSource = new(pair.Source, sourceLayout, live, ControllerLayouts.RemapFaceButtons(pair.Source.Buttons, true, sourceLayout.IsNintendo()));
        MappingOutput = new(pair.Output, MappingPreview.Layout(EditedOutputMode), live, MappingPreview.OutputButtons(pair.Output.Buttons, EditedOutputMode));
    }
    public ObservableCollection<ButtonMappingRow> ButtonMappings { get; } = [];
    [ObservableProperty] public partial string ButtonMappingStatus { get; set; } = "默认：GL → 左摇杆按下，GR → 右摇杆按下；其他按键保持原功能";
    private void LoadButtonMappings(Ns2ButtonMapping mapping)
    {
        ButtonMappings.Clear();
        var sources = Enum.GetValues<ControllerButtons>().Where(x => x != ControllerButtons.None);
        foreach (var source in sources)
        {
            var row = new ButtonMappingRow(source, mapping.Target(source), EditedOutputMode)
                { SourceLayout = MappingSourceLayout, KeyboardSource = IsKeyboardMouseInput };
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ButtonMappingRow.Target)) return;
                HasMappingChanges = true;
                ButtonMappingStatus = "当前修改已用于本页测试；点击右上角“应用并保存”保存到此线路。";
                inspectedOutput = null; NotifyMappingSelection();
                OnPropertyChanged(nameof(AllMappingRules));
            };
            ButtonMappings.Add(row);
        }
        SelectedMapping = ButtonMappings.FirstOrDefault(x => x.Source == ControllerButtons.B);
        HasMappingChanges = false;
        ButtonMappingStatus = "已载入保存的映射。修改后点击应用并保存；右侧实时显示已应用的输出。";
        OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint));
        OnPropertyChanged(nameof(AllMappingRules));
    }
    private void RefreshMappingSourceLabels()
    {
        OnPropertyChanged(nameof(MappingStickSummary));
        foreach (var row in ButtonMappings)
        {
            row.SourceLayout = MappingSourceLayout;
            row.KeyboardSource = IsKeyboardMouseInput;
        }
        NotifyMappingSelection();
        OnPropertyChanged(nameof(AllMappingRules));
    }
    [RelayCommand] private void SaveButtonMappings()
        => SaveRouteButtonMappings();

    private Ns2ButtonMapping CaptureButtonMappings() =>
        new Ns2ButtonMapping { Bindings = ButtonMappings.ToDictionary(x => x.Source, x => x.Target.Button) }.Normalize();

    internal void SaveRouteButtonMappings(Action<BridgeOptions>? save = null)
    {
        try
        {
            CancelMappingCapture();
            var route = bridgeOptions.Route(EditedOutputMode) with { Ns2Buttons = CaptureButtonMappings(), KeyboardOverrides = new(keyboardDraft), GyroSource = CurrentGyroSource,
                KeyboardMouseSupplementEnabled = KeyboardMouseSupplementEnabled, MouseMode = CurrentMouseMode,
                KeyboardStickBindings = new(keyboardStickDraft), ControllerSticks = controllerStickDraft with { } };
            var next = bridgeOptions.SaveRoute(EditedOutputMode, route);
            if (save is null) next.Save(); else save(next);
            bridgeOptions = next; output.Options = next; transport.RumbleGain = next.RumbleGain;
            HasMappingChanges = false;
            ButtonMappingStatus = $"{RouteConfigurationTitle}：映射已保存；所有输入方式在下一帧应用，原始输入显示不变";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ButtonMappingStatus = $"映射未应用：{ex.Message}"; }
    }
    [RelayCommand] private void ResetButtonMappings()
    {
        var defaults = OutputRouteOptions.CreateDefault(EditedOutputMode);
        KeyboardMouseSupplementEnabled = defaults.KeyboardMouseSupplementEnabled;
        SelectedGyroSource = GyroSources[(int)defaults.GyroSource];
        SelectedMouseMode = MouseModes[defaults.MouseMode == MouseEmulationMode.Gyroscope ? 1 : 0];
        LoadButtonMappings(defaults.Ns2Buttons); LoadKeyboardBindings(defaults.KeyboardOverrides);
        LoadStickMappings(defaults.KeyboardStickBindings, defaults.ControllerSticks);
        HasMappingChanges = true; SaveButtonMappings();
    }
    [RelayCommand(CanExecute = nameof(IsMappingAvailable))] private void OpenMappingPreview()
    {
        UpdateMappingAvailability();
        if (!IsMappingAvailable) return;
        SelectedPage = "按键映射";
        UpdateMappingComparison();
    }
}

public sealed record ButtonMappingChoice(ControllerButtons Button, VirtualControllerMode? Mode = null)
{
    public override string ToString()
    {
        if (Mode is { } mode && Button != ControllerButtons.None)
        {
            var displayed = MappingPreview.OutputButtons(Button, mode);
            if (displayed == ControllerButtons.None) return DefaultLabel + "（此输出不支持）";
            if (mode is VirtualControllerMode.DualSense or VirtualControllerMode.DualSenseEdge)
                return displayed switch {
                    ControllerButtons.Y => "△", ControllerButtons.B => "○", ControllerButtons.A => "×", ControllerButtons.X => "□",
                    ControllerButtons.Capture or ControllerButtons.Touchpad => "触摸板按下",
                    ControllerButtons.L => "L1", ControllerButtons.R => "R1", ControllerButtons.ZL => "L2", ControllerButtons.ZR => "R2",
                    ControllerButtons.Minus => "Create", ControllerButtons.Plus => "Options", ControllerButtons.Home => "PS",
                    ControllerButtons.GL => "左背键（L4）", ControllerButtons.GR => "右背键（R4）", _ => DefaultLabel };
            if (mode == VirtualControllerMode.Xbox360)
                return displayed switch { ControllerButtons.L => "LB", ControllerButtons.R => "RB", ControllerButtons.ZL => "LT", ControllerButtons.ZR => "RT",
                    ControllerButtons.Plus => "Menu", ControllerButtons.Minus => "View", ControllerButtons.A or ControllerButtons.B or ControllerButtons.X or ControllerButtons.Y => displayed.ToString(), _ => DefaultLabel };
        }
        return DefaultLabel;
    }
    private string DefaultLabel => Button switch
    {
        ControllerButtons.None => "不输出",
        ControllerButtons.LeftStick => "左摇杆按下（L3）",
        ControllerButtons.RightStick => "右摇杆按下（R3）",
        ControllerButtons.GL => "左背键（GL）", ControllerButtons.GR => "右背键（GR）",
        ControllerButtons.Touchpad => "触摸板按下", ControllerButtons.Up => "方向 ↑", ControllerButtons.Down => "方向 ↓",
        ControllerButtons.Left => "方向 ←", ControllerButtons.Right => "方向 →", _ => Button.ToString()
    };
}

public partial class ButtonMappingRow : ObservableObject
{
    public ControllerButtons Source { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial ControllerLayout SourceLayout { get; set; } = ControllerLayout.Switch2Pro;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial bool KeyboardSource { get; set; }
    public string Label
    {
        get
        {
            var displayed = ControllerLayouts.RemapFaceButtons(Source, true, SourceLayout.IsNintendo());
            var label = new ButtonMappingChoice(displayed).ToString();
            if (SourceLayout.IsPlayStation()) label = displayed switch
            {
                ControllerButtons.A => "×", ControllerButtons.B => "○", ControllerButtons.X => "□", ControllerButtons.Y => "△",
                ControllerButtons.L => "L1", ControllerButtons.R => "R1", ControllerButtons.ZL => "L2", ControllerButtons.ZR => "R2",
                ControllerButtons.Plus => "Options / Start", ControllerButtons.Minus => "Create / Share / Select",
                ControllerButtons.Home => "PS", _ => label
            };
            else if (!SourceLayout.IsNintendo()) label = displayed switch
            {
                ControllerButtons.L => "LB", ControllerButtons.R => "RB", ControllerButtons.ZL => "LT", ControllerButtons.ZR => "RT",
                ControllerButtons.Plus => "Menu", ControllerButtons.Minus => "View", _ => label
            };
            if (!KeyboardSource) return label;
            var key = Source switch
            {
                ControllerButtons.B => "J / 空格", ControllerButtons.A => "K", ControllerButtons.Y => "U", ControllerButtons.X => "I",
                ControllerButtons.L => "Q", ControllerButtons.R => "E", ControllerButtons.ZL => "鼠标右键", ControllerButtons.ZR => "鼠标左键",
                ControllerButtons.LeftStick => "Shift", ControllerButtons.RightStick => "Ctrl", ControllerButtons.Plus => "Enter", ControllerButtons.Minus => "Tab",
                ControllerButtons.Up => "↑", ControllerButtons.Down => "↓", ControllerButtons.Left => "←", ControllerButtons.Right => "→",
                ControllerButtons.Home => "F1", ControllerButtons.Capture => "C", _ => "未绑定键鼠"
            };
            return $"{key} → {label}";
        }
    }
    public IReadOnlyList<ButtonMappingChoice> Choices { get; }
    [ObservableProperty] public partial ButtonMappingChoice Target { get; set; }
    public ButtonMappingRow(ControllerButtons source, ControllerButtons target, VirtualControllerMode? mode = null)
    {
        Choices = Enum.GetValues<ControllerButtons>().Select(x => new ButtonMappingChoice(x, mode)).ToArray();
        Source = source; Target = Choices.First(x => x.Button == target);
    }
    public override string ToString() => Label;
}

public sealed record MappingDiagram(ControllerState State, ControllerLayout Layout, bool Online, ControllerButtons DisplayButtons)
{
    public static MappingDiagram Empty { get; } = new(ControllerState.Neutral(DateTimeOffset.MinValue), ControllerLayout.Switch2Pro, false, ControllerButtons.None);
    public ControllerButtons Buttons => Online ? DisplayButtons : ControllerButtons.None;
    public double LeftX => Online ? StickCoordinates.Unit(State.LeftX) : 0;
    public double LeftY => Online ? StickCoordinates.Unit(State.LeftY) : 0;
    public double RightX => Online ? StickCoordinates.Unit(State.RightX) : 0;
    public double RightY => Online ? StickCoordinates.Unit(State.RightY) : 0;
    public double LeftTrigger => Online ? State.LeftTriggerValue / 255.0 : 0;
    public double RightTrigger => Online ? State.RightTriggerValue / 255.0 : 0;
}
