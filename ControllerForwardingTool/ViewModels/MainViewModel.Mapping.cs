using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    public bool IsMapping => SelectedPage == "按键映射";
    public bool CanMapSource => !IsWindowsBridgeInput || SelectedBridgeGamepad?.Layout == ControllerLayout.Switch2Pro;
    [ObservableProperty] public partial ButtonMappingRow? SelectedMapping { get; set; }
    [ObservableProperty] public partial MappingDiagram MappingSource { get; set; } = MappingDiagram.Empty;
    [ObservableProperty] public partial MappingDiagram MappingOutput { get; set; } = MappingDiagram.Empty;
    [ObservableProperty] public partial string MappingSourceName { get; set; } = "NS2 Pro";
    [ObservableProperty] public partial string MappingOutputName { get; set; } = "映射结果";
    [ObservableProperty] public partial bool HasMappingChanges { get; set; }
    public ControllerButtons SelectedSourceButton => SelectedMapping?.Source ?? ControllerButtons.None;
    public ControllerButtons SelectedOutputButton => MappingPreview.OutputButtons(SelectedMapping?.Target.Button ?? ControllerButtons.None, EditedOutputMode);
    public string MappingEditHint => CanMapSource ? "点击左侧图形选择源按键，再点击右侧目标按键；也可使用下方下拉框。橙色为实时按下，蓝色框为当前配置项。" : "当前输入不是 NS2：可查看输入与输出对照。自定义按键映射目前适用于 NS2 输入。";
    partial void OnSelectedMappingChanged(ButtonMappingRow? value) => NotifyMappingSelection();
    private void NotifyMappingSelection()
    { OnPropertyChanged(nameof(SelectedSourceButton)); OnPropertyChanged(nameof(SelectedOutputButton)); }
    [RelayCommand] private void SelectMappingButton(ControllerButtons button)
    { if (CanMapSource) SelectedMapping = ButtonMappings.FirstOrDefault(x => x.Source == button) ?? SelectedMapping; }
    [RelayCommand] private void AssignMappingTarget(ControllerButtons button)
    {
        if (!CanMapSource || SelectedMapping is null) return;
        var native = ControllerLayouts.RemapFaceButtons(button, MappingPreview.Layout(EditedOutputMode).IsNintendo(), true);
        SelectedMapping.Target = SelectedMapping.Choices.First(x => x.Button == native);
    }
    private void UpdateMappingComparison()
    {
        if (!IsMapping) return;
        var pair = inputBridge?.Comparison ?? new BridgeComparison(ControllerState.Neutral(DateTimeOffset.MinValue), ControllerState.Neutral(DateTimeOffset.MinValue));
        bool live = IsServerRunning && DateTimeOffset.Now - pair.Source.ReceivedAt < TimeSpan.FromMilliseconds(250);
        var sourceLayout = IsWindowsBridgeInput ? SelectedBridgeGamepad?.Layout ?? ControllerLayout.Generic : ControllerLayout.Switch2Pro;
        MappingSourceName = IsWindowsBridgeInput ? SelectedBridgeGamepad?.Name ?? "等待输入手柄" : "NS2 Pro · 蓝牙原始输入";
        MappingOutputName = VirtualProfile.Get(EditedOutputMode).Name + (IsServerRunning ? " · 已应用映射" : " · 离线配置");
        MappingSource = new(pair.Source, sourceLayout, live, ControllerLayouts.RemapFaceButtons(pair.Source.Buttons, true, sourceLayout.IsNintendo()));
        MappingOutput = new(pair.Output, MappingPreview.Layout(EditedOutputMode), live, MappingPreview.OutputButtons(pair.Output.Buttons, EditedOutputMode));
    }
    public ObservableCollection<ButtonMappingRow> ButtonMappings { get; } = [];
    [ObservableProperty] public partial string ButtonMappingStatus { get; set; } = "默认：GL → 左摇杆按下，GR → 右摇杆按下；其他按键保持原功能";
    private void LoadButtonMappings(Ns2ButtonMapping mapping)
    {
        ButtonMappings.Clear();
        var sources = new[] { ControllerButtons.GL, ControllerButtons.GR }
            .Concat(Enum.GetValues<ControllerButtons>().Where(x => x is not (ControllerButtons.None or ControllerButtons.GL or ControllerButtons.GR or ControllerButtons.Touchpad)));
        foreach (var source in sources)
        {
            var row = new ButtonMappingRow(source, mapping.Target(source), EditedOutputMode);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ButtonMappingRow.Target)) return;
                HasMappingChanges = true;
                ButtonMappingStatus = "有未应用的修改；右侧实时状态仍显示已保存映射。点击应用并保存后生效。";
                NotifyMappingSelection();
            };
            ButtonMappings.Add(row);
        }
        SelectedMapping = ButtonMappings.FirstOrDefault();
        HasMappingChanges = false;
        ButtonMappingStatus = "已载入保存的映射。修改后点击应用并保存；右侧实时显示已应用的输出。";
        OnPropertyChanged(nameof(CanMapSource)); OnPropertyChanged(nameof(MappingEditHint));
    }
    [RelayCommand] private void SaveButtonMappings()
        => SaveRouteButtonMappings();

    private Ns2ButtonMapping CaptureButtonMappings() =>
        new Ns2ButtonMapping { Bindings = ButtonMappings.ToDictionary(x => x.Source, x => x.Target.Button) }.Normalize();

    internal void SaveRouteButtonMappings(Action<BridgeOptions>? save = null)
    {
        try
        {
            var route = bridgeOptions.Route(EditedOutputMode) with { Ns2Buttons = CaptureButtonMappings() };
            var next = bridgeOptions.SaveRoute(EditedOutputMode, route);
            if (save is null) next.Save(); else save(next);
            bridgeOptions = next; output.Options = next; transport.RumbleGain = next.RumbleGain;
            HasMappingChanges = false;
            ButtonMappingStatus = $"{RouteConfigurationTitle}：映射已保存；输出运行时下一帧生效，NS2 原始输入显示不变";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ButtonMappingStatus = $"映射未应用：{ex.Message}"; }
    }
    [RelayCommand] private void ResetButtonMappings() { LoadButtonMappings(new()); HasMappingChanges = true; SaveButtonMappings(); }
    [RelayCommand] private void OpenMappingPreview()
    {
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
    public string Label => new ButtonMappingChoice(Source).ToString();
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
