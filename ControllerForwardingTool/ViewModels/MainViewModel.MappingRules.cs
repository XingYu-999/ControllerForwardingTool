using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private MappingEditSession? mappingEditor;
    private bool mappingEditorConfirmPending;
    private int[] mappingEditorKeys = [];
    public IReadOnlyList<MappingRuleGroup> AllMappingRules
        => GroupMappingRules(BuildMappingRules(IsKeyboardMouseInput, EditedOutputMode, ButtonMappings,
            CaptureButtonMappings(), keyboardDraft, keyboardStickDraft), EditedOutputMode);
    internal static IReadOnlyList<MappingRuleGroup> GroupMappingRules(IEnumerable<MappingRuleRow> rules, VirtualControllerMode mode)
    {
        MappingDestination Target(MappingRuleRow row)
        {
            if (row.Destination.Direction is not null) return row.Destination;
            var displayed = MappingPreview.OutputButtons(row.Destination.Button, mode);
            // Keep unsupported targets separate from deliberately disabled sources. Output aliases
            // such as Capture/Touchpad must share one card for the same actual output button.
            if (displayed == ControllerButtons.None) return row.Destination;
            return new(Enum.GetValues<ControllerButtons>().First(b => b != ControllerButtons.None &&
                MappingPreview.OutputButtons(b, mode) == displayed));
        }
        return rules.GroupBy(Target)
            .OrderBy(g => g.Key.Direction is not null ? 1 : MappingPreview.OutputButtons(g.Key.Button, mode) != ControllerButtons.None ? 0 : 2)
            .ThenBy(g => g.Key.Direction is { } direction ? (uint)direction : (uint)g.Key.Button)
            .Select(g => new MappingRuleGroup(g.Key.Direction is { } d ? KeyboardStickMapping.Name(d)
                : new ButtonMappingChoice(g.Key.Button, mode).ToString(), g.Key, g.ToArray())).ToArray();
    }
    internal static IReadOnlyList<MappingRuleRow> BuildMappingRules(bool keyboardOnly, VirtualControllerMode mode,
        IEnumerable<ButtonMappingRow> rows, Ns2ButtonMapping mapping, IReadOnlyDictionary<int, ControllerButtons> bindings,
        IReadOnlyDictionary<StickDirection, int> sticks)
    {
        var rules = new List<MappingRuleRow>();
        if (!keyboardOnly)
            rules.AddRange(rows.Select(r => new MappingRuleRow("手柄 · " + r.Label, r.Target.ToString(), new(r.Target.Button), r.Source)));
        foreach (int key in Enumerable.Range(1, 254).Where(KeyboardMapping.CanBind))
        {
            var button = MappingLookup.KeyTarget(key, keyboardOnly, mapping, bindings, sticks);
            string source = (key is 1 or 2 or 4 or 5 or 6 ? "" : "键盘 · ") + KeyboardMapping.KeyName(key);
            if (button != ControllerButtons.None || bindings.ContainsKey(key))
                rules.Add(new(source, new ButtonMappingChoice(button, mode).ToString(), new(button), KeyboardKey: key));
            else if (keyboardOnly)
                foreach (var direction in Enum.GetValues<StickDirection>())
                    if (KeyboardStickMapping.KeyFor(direction, sticks) == key)
                        rules.Add(new(source, KeyboardStickMapping.Name(direction), new(Direction: direction), KeyboardKey: key));
        }
        return rules;
    }
    [ObservableProperty] public partial string MappingEditorTitle { get; set; } = "配置按键";
    [ObservableProperty] public partial string MappingEditorHint { get; set; } = "";
    [ObservableProperty] public partial bool MappingEditorTargetEditable { get; set; }
    [ObservableProperty] public partial IReadOnlyList<MappingTargetChoice> MappingEditorTargets { get; set; } = [];
    [ObservableProperty] public partial MappingTargetChoice? MappingEditorTarget { get; set; }
    [ObservableProperty] public partial bool MappingEditorMouseLeft { get; set; }
    public ObservableCollection<MappingEditorSource> EditorControllerSources { get; } = [];
    public ObservableCollection<MappingEditorSource> EditorKeyboardSources { get; } = [];
    public bool CanEditPhysicalSource => !IsKeyboardMouseInput && mappingEditor?.Target.Direction is null;
    public int EditorKeyboardColumn => CanEditPhysicalSource ? 1 : 0;
    public int EditorKeyboardSpan => CanEditPhysicalSource ? 1 : 2;
    partial void OnMappingEditorTargetChanged(MappingTargetChoice? value)
    {
        if (mappingEditor is null || value is null) return;
        mappingEditor.Target = value.Destination;
        RefreshMappingEditorSources();
        OnPropertyChanged(nameof(HighlightedMappingTarget)); OnPropertyChanged(nameof(CanEditPhysicalSource));
        OnPropertyChanged(nameof(EditorKeyboardColumn)); OnPropertyChanged(nameof(EditorKeyboardSpan));
    }
    partial void OnMappingEditorMouseLeftChanged(bool value)
    {
        mappingEditor?.SetMouseLeft(value);
        if (value) { mappingStickEditor?.RemoveKey(1); RefreshStickMappingEditor(); }
        RefreshMappingEditorSources();
    }

    internal bool BeginMappingEditor(MappingRuleGroup? rule = null)
    {
        if (!IsMapping || IsListeningForMapping) return false;
        var button = Enum.GetValues<ControllerButtons>().FirstOrDefault(b => b != ControllerButtons.None &&
            MappingPreview.OutputButtons(b, EditedOutputMode) == SelectedOutputButton);
        if (rule is null && button == ControllerButtons.None) return false;
        var target = rule?.Destination ?? new(button);
        var origins = SelectedOrigins;
        var sourceButtons = Enum.GetValues<ControllerButtons>().Where(b => b != ControllerButtons.None && origins.Buttons.HasFlag(b));
        mappingEditor = rule?.CreateEditor(IsKeyboardMouseInput) ?? new(IsKeyboardMouseInput, target, sourceButtons, origins.Keys);
        mappingStickEditor = rule is null && button is ControllerButtons.LeftStick or ControllerButtons.RightStick
            ? new(IsKeyboardMouseInput, button == ControllerButtons.LeftStick ? StickSource.Left : StickSource.Right,
                new() { ControllerSticks = controllerStickDraft, KeyboardStickBindings = new(keyboardStickDraft), KeyboardOverrides = new(keyboardDraft) }) : null;
        MappingEditorTab = 0;
        RefreshStickMappingEditor();
        mappingEditorConfirmPending = false; mappingEditorKeys = [];
        MappingEditorTargetEditable = rule is not null;
        var choices = Enum.GetValues<ControllerButtons>().Where(b => b == ControllerButtons.None ||
            MappingPreview.OutputButtons(b, EditedOutputMode) != ControllerButtons.None || b == target.Button)
            .Select(b => new MappingTargetChoice(new(b), new ButtonMappingChoice(b, EditedOutputMode).ToString())).ToList();
        if (IsKeyboardMouseInput && rule is not null && (rule.Destination.Direction is not null || rule.KeyboardKeys.Length == 1))
            choices.AddRange(Enum.GetValues<StickDirection>().Select(d => new MappingTargetChoice(new(Direction: d), KeyboardStickMapping.Name(d))));
        MappingEditorTargets = choices;
        MappingEditorTarget = choices.First(c => c.Destination == target);
        MappingEditorTitle = rule is null ? $"配置 {new ButtonMappingChoice(button, EditedOutputMode)}" : $"配置输出 · {rule.TargetLabel}";
        if (mappingStickEditor is { } stickEditor) MappingEditorTitle = stickEditor.Target == StickSource.Left ? "配置左摇杆" : "配置右摇杆";
        MappingEditorHint = "可以录入手柄和键鼠来源；录入不会自动关闭，点击确认后提交到草稿。";
        RefreshMappingEditorSources();
        IsListeningForMapping = true;
        OnPropertyChanged(nameof(CanEditPhysicalSource)); OnPropertyChanged(nameof(HighlightedMappingTarget));
        OnPropertyChanged(nameof(EditorKeyboardColumn)); OnPropertyChanged(nameof(EditorKeyboardSpan));
        return true;
    }
    internal void ObserveMappingEditor(int[] keys)
    {
        if (mappingEditor is not { } editor || !IsListeningForMapping) return;
        if (!IsMapping || keys.Contains(27)) { CancelMappingCapture(); return; }
        mappingEditorKeys = keys;
        if (mappingEditorConfirmPending)
        {
            if (keys.All(k => k == 1)) CommitMappingEditor();
            return;
        }
        var source = IsKeyboardMouseInput ? null : inputBridge?.Comparison.Source;
        if (source is not null && DateTimeOffset.Now - source.ReceivedAt > TimeSpan.FromMilliseconds(250)) source = null;
        if (MappingEditorTab == 1 && mappingStickEditor is { } stick)
        {
            if (stick.Observe(keys, source)) SyncStickEditorKeys();
            MappingStickEditorHint = stick.Hint;
        }
        else
        {
            if (editor.Observe(keys, source))
            {
                foreach (int key in editor.Keys) mappingStickEditor?.RemoveKey(key);
                RefreshMappingEditorSources(); RefreshStickMappingEditor();
            }
            MappingEditorHint = editor.Hint;
        }
    }
    private void RefreshMappingEditorSources()
    {
        if (mappingEditor is not { } editor) return;
        EditorControllerSources.Clear(); EditorKeyboardSources.Clear();
        foreach (var source in editor.Buttons.Order())
            EditorControllerSources.Add(new(new ButtonMappingRow(source, source) { SourceLayout = MappingSourceLayout }.Label, source));
        foreach (var key in editor.Keys.Where(k => k != 1).Order()) EditorKeyboardSources.Add(new(KeyboardMapping.KeyName(key), Key: key));
        MappingEditorMouseLeft = editor.Keys.Contains(1);
    }
    [RelayCommand] private void RemoveMappingEditorSource(MappingEditorSource source)
    {
        if (source.Key is { } key) mappingEditor?.RemoveKey(key);
        if (source.Button is { } button) mappingEditor?.RemoveButton(button);
        RefreshMappingEditorSources();
    }
    [RelayCommand] private void ConfirmMappingEditor()
    {
        mappingEditorConfirmPending = true;
        if (mappingEditorKeys.All(k => k == 1)) CommitMappingEditor();
        else MappingEditorHint = MappingStickEditorHint = "请松开键盘和鼠标按键后完成确认，避免按键继续操作主窗口。";
    }
    private void CommitMappingEditor()
    {
        if (mappingEditor is not { } editor || !IsMapping) return;
        var result = editor.Apply(new BridgeOptions { Ns2Buttons = CaptureButtonMappings(), KeyboardOverrides = new(keyboardDraft), KeyboardStickBindings = new(keyboardStickDraft), ControllerSticks = controllerStickDraft });
        if (mappingStickEditor is { } stickEditor) result = stickEditor.Apply(result);
        foreach (var row in ButtonMappings) row.Target = row.Choices.First(c => c.Button == result.Ns2Buttons.Target(row.Source));
        keyboardDraft.Clear(); foreach (var entry in result.KeyboardOverrides) keyboardDraft[entry.Key] = entry.Value;
        keyboardStickDraft = result.KeyboardStickBindings;
        controllerStickDraft = result.ControllerSticks;
        inspectedOutput = MappingPreview.OutputButtons(editor.Target.Button, EditedOutputMode);
        RefreshKeyboardBindings(); OnPropertyChanged(nameof(MappingStickSummary));
        HasMappingChanges = true;
        ButtonMappingStatus = MappingCaptureStatus = "已确认配置，可立即在本页测试；点击右上角“应用并保存”保留配置。";
        mappingEditorConfirmPending = false; mappingEditor = null; mappingStickEditor = null; IsListeningForMapping = false;
        RefreshStickMappingEditor();
        NotifyMappingSelection();
    }
}

public sealed record MappingRuleRow(string SourceLabel, string TargetLabel, MappingDestination Destination,
    ControllerButtons? ControllerSource = null, int? KeyboardKey = null);
public sealed record MappingRuleGroup(string TargetLabel, MappingDestination Destination, IReadOnlyList<MappingRuleRow> Sources)
{
    public ControllerButtons[] ControllerSources => Sources.Where(r => r.ControllerSource is not null).Select(r => r.ControllerSource!.Value).Distinct().ToArray();
    public int[] KeyboardKeys => Sources.Where(r => r.KeyboardKey is not null).Select(r => r.KeyboardKey!.Value).Distinct().ToArray();
    private static bool IsMouse(int key) => key is 1 or 2 or 4 or 5 or 6;
    public bool HasControllerSources => ControllerSources.Length > 0;
    public bool HasKeyboardSources => KeyboardKeys.Any(k => !IsMouse(k));
    public bool HasMouseSources => KeyboardKeys.Any(IsMouse);
    public string ControllerSummary => "手柄：" + string.Join("、", Sources.Where(r => r.ControllerSource is not null)
        .Select(r => r.SourceLabel.StartsWith("手柄 · ") ? r.SourceLabel[5..] : r.SourceLabel).Distinct());
    public string KeyboardSummary => "键盘：" + string.Join("、", KeyboardKeys.Where(k => !IsMouse(k)).Select(KeyboardMapping.KeyName));
    public string MouseSummary => "鼠标：" + string.Join("、", KeyboardKeys.Where(IsMouse).Select(k => KeyboardMapping.KeyName(k)[2..]));
    public string SourceCountLabel => $"{ControllerSources.Length + KeyboardKeys.Length} 个来源";
    public MappingEditSession CreateEditor(bool keyboardOnly) => new(keyboardOnly, Destination, ControllerSources, KeyboardKeys);
}
public sealed record MappingTargetChoice(MappingDestination Destination, string Label)
{
    public override string ToString() => Label;
}
public sealed record MappingEditorSource(string Label, ControllerButtons? Button = null, int? Key = null);
