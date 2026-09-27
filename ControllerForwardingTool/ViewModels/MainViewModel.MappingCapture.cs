using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private QuickMappingSession? quickMapping;
    private ControllerState? mappingKeyboardPreview;
    private readonly Dictionary<int, ControllerButtons> keyboardDraft = [];
    private Dictionary<StickDirection, int> keyboardStickDraft = [];
    private StickMapping controllerStickDraft = new();
    public ObservableCollection<KeyboardBindingRow> KeyboardBindings { get; } = [];
    [ObservableProperty] public partial bool IsListeningForMapping { get; set; }
    [ObservableProperty] public partial string MappingCaptureStatus { get; set; } = "双击输出按键仅配置该按键；点击“快速配置”开始完整向导。";
    [ObservableProperty] public partial string QuickMappingPrompt { get; set; } = "";
    [ObservableProperty] public partial string QuickMappingHint { get; set; } = "";
    [ObservableProperty] public partial string QuickMappingProgress { get; set; } = "";
    public bool CanQuickBindMouseLeft => IsListeningForMapping && quickMapping?.Current is { Stick: null };
    [RelayCommand] private void QuickBindMouseLeft()
    { quickMapping?.BindMouseLeft(); FinishOrRefreshQuickMapping(released: false); }
    [ObservableProperty] public partial int[] MappingPressedKeys { get; set; } = [];
    [ObservableProperty] public partial Vector MappingMouseDelta { get; set; }
    [ObservableProperty] public partial int MappingSelectedKey { get; set; }
    [ObservableProperty] public partial string MappingKeyboardStatus { get; set; } = "等待键鼠输入 · 本地预览不会自动开启转发";
    public int MappingSourceColumnSpan => IsKeyboardMouseInput ? 2 : 1;
    public int MappingOutputColumn => IsKeyboardMouseInput ? 0 : 1;
    public int MappingOutputRow => IsKeyboardMouseInput ? 1 : 0;
    public ControllerButtons HighlightedMappingTarget
    {
        get
        {
            if (mappingEditor is { } editor)
                return editor.Target.Direction is { } d ? (d < StickDirection.RightUp ? ControllerButtons.LeftStick : ControllerButtons.RightStick)
                    : MappingPreview.OutputButtons(editor.Target.Button, EditedOutputMode);
            var step = quickMapping?.Current;
            var button = step?.Direction is { } direction ? ((int)direction < 4 ? ControllerButtons.LeftStick : ControllerButtons.RightStick)
                : step?.Stick is { } stick ? (stick == StickSource.Left ? ControllerButtons.LeftStick : ControllerButtons.RightStick)
                : step?.Button ?? ControllerButtons.None;
            return step is null ? SelectedOutputButton : MappingPreview.OutputButtons(button, EditedOutputMode);
        }
    }
    public string MappingStickSummary => IsKeyboardMouseInput
        ? string.Join(" · ", Enum.GetValues<StickDirection>().Select(d => $"{KeyboardStickMapping.Name(d)}：{StickKeyName(d)}")) + "。右摇杆方向键松开时，鼠标仍按本页所选模式工作。"
        : $"左摇杆 ← 源{(controllerStickDraft.Left == StickSource.Left ? "左" : "右")}摇杆；右摇杆 ← 源{(controllerStickDraft.Right == StickSource.Right ? "右" : "左")}摇杆。摇杆按下（L3 / R3）单独配置。";
    private string StickKeyName(StickDirection direction)
    {
        int key = KeyboardStickMapping.KeyFor(direction, keyboardStickDraft);
        return key == 0 ? "未绑定" : KeyboardMapping.KeyName(key);
    }
    partial void OnIsListeningForMappingChanged(bool value)
    { OnPropertyChanged(nameof(HighlightedMappingTarget)); OnPropertyChanged(nameof(CanQuickBindMouseLeft)); ConfigureKeyboardMouse(); }

    internal bool BeginQuickMapping(bool selectedButtonOnly = false)
    {
        if (!IsMapping || IsListeningForMapping) return false;
        var target = selectedButtonOnly ? Enum.GetValues<ControllerButtons>().FirstOrDefault(b =>
            b != ControllerButtons.None && MappingPreview.OutputButtons(b, EditedOutputMode) == SelectedOutputButton) : (ControllerButtons?)null;
        if (target == ControllerButtons.None) return false;
        quickMapping = new(IsKeyboardMouseInput, EditedOutputMode, target);
        IsListeningForMapping = true;
        RefreshQuickMappingStep();
        return true;
    }
    private void RefreshQuickMappingStep()
    {
        OnPropertyChanged(nameof(CanQuickBindMouseLeft));
        if (quickMapping?.Current is not { } step) return;
        string target = step.Direction is { } direction ? KeyboardStickMapping.Name(direction)
            : step.Stick is { } stick ? stick == StickSource.Left ? "左摇杆" : "右摇杆"
            : new ButtonMappingChoice(step.Button, EditedOutputMode).ToString();
        QuickMappingPrompt = $"请按下{target}映射源";
        QuickMappingHint = quickMapping.Hint + (step.Stick is not null ? " 推动要使用的源摇杆，按钮和摇杆按下不能代替摇杆。"
            : step.Direction is not null ? " 分别为上、下、左、右录入一个方向键。" : "");
        QuickMappingProgress = $"{(IsKeyboardMouseInput ? "键盘 / 鼠标" : "手柄 + 键鼠补充")} · 第 {quickMapping.Index + 1} / {quickMapping.Steps.Count} 项";
        OnPropertyChanged(nameof(HighlightedMappingTarget));
    }
    [RelayCommand] private void SkipQuickMapping()
    {
        quickMapping?.Skip();
        FinishOrRefreshQuickMapping(released: false);
    }
    internal void IgnoreQuickMappingClick() => quickMapping?.RequireRelease();
    internal void ObserveQuickMapping(int[] keys)
    {
        if (!IsListeningForMapping || quickMapping is null) return;
        if (!IsMapping || keys.Contains(27)) { CancelMappingCapture(); return; }
        var raw = inputBridge?.Comparison.Source;
        if (raw is not null && DateTimeOffset.Now - raw.ReceivedAt > TimeSpan.FromMilliseconds(250)) raw = null;
        quickMapping.Observe(keys, raw);
        bool released = keys.Length == 0 && (raw is null || raw.Buttons == ControllerButtons.None);
        FinishOrRefreshQuickMapping(released);
    }
    private void FinishOrRefreshQuickMapping(bool released)
    {
        if (quickMapping is not { } session) return;
        if (!session.Completed) { RefreshQuickMappingStep(); return; }
        if (!released)
        {
            QuickMappingPrompt = "录入完成，请松开按键";
            QuickMappingHint = "松开后返回原来的页面位置，录入按键不会继续操作主窗口。";
            OnPropertyChanged(nameof(CanQuickBindMouseLeft));
            return;
        }
        // Commit only at completion. Esc/close leaves the pre-existing editor draft untouched.
        foreach (var (direction, key) in session.KeyboardSticks)
        {
            foreach (var other in Enum.GetValues<StickDirection>())
                if (other != direction && KeyboardStickMapping.KeyFor(other, keyboardStickDraft) == key) keyboardStickDraft[other] = 0;
            keyboardStickDraft[direction] = key;
            keyboardDraft.Remove(key);
            MappingSelectedKey = key;
        }
        foreach (var (key, target) in session.KeyboardButtons) { keyboardDraft[key] = target; MappingSelectedKey = key; }
        foreach (var (source, target) in session.ControllerButtons)
        {
            var row = ButtonMappings.First(x => x.Source == source);
            row.Target = row.Choices.First(x => x.Button == target);
        }
        controllerStickDraft = controllerStickDraft with
        {
            Left = session.ControllerSticks.GetValueOrDefault(StickSource.Left, controllerStickDraft.Left),
            Right = session.ControllerSticks.GetValueOrDefault(StickSource.Right, controllerStickDraft.Right)
        };
        if (session.Steps.Count == 1 && session.CapturedCount > 0)
            inspectedOutput = MappingPreview.OutputButtons(session.Steps[0].Button, EditedOutputMode);
        RefreshKeyboardBindings(); OnPropertyChanged(nameof(MappingStickSummary)); NotifyMappingSelection();
        HasMappingChanges |= session.CapturedCount > 0;
        MappingCaptureStatus = session.CapturedCount > 0 ? $"已录入 {session.CapturedCount} 项，可立即测试；点击右上角“应用并保存”保留配置。" : "快速配置完成，全部跳过，原配置保持不变。";
        ButtonMappingStatus = MappingCaptureStatus;
        quickMapping = null; IsListeningForMapping = false;
    }
    [RelayCommand] private void CancelMappingCapture()
    {
        bool singleButton = mappingEditor is not null;
        quickMapping?.Cancel(); quickMapping = null;
        mappingEditor?.Cancel(); mappingEditor = null;
        mappingStickEditor?.Cancel(); mappingStickEditor = null; RefreshStickMappingEditor();
        mappingEditorConfirmPending = false; mappingEditorKeys = [];
        if (IsListeningForMapping) ButtonMappingStatus = MappingCaptureStatus = singleButton
            ? "已取消按键配置，未确认的修改已丢弃。" : "已取消快速配置，本次向导未修改映射。";
        IsListeningForMapping = false;
    }
    internal void ObserveMappingKeyboard(int[] keys, Vector delta, bool focused)
    {
        if (!IsMapping || IsListeningForMapping) return;
        if (!focused)
        {
            MappingPressedKeys = []; MappingMouseDelta = default; mappingKeyboardPreview = null;
            MappingKeyboardStatus = "窗口未激活 · 已暂停键鼠预览";
            return;
        }
        MappingPressedKeys = keys; MappingMouseDelta = delta;
        mappingKeyboardPreview = KeyboardMouseMapper.Map(keys.Contains, delta.X, delta.Y, .033,
            IsKeyboardMouseInput ? CurrentMouseMode : MouseEmulationMode.Gyroscope, DateTimeOffset.Now);
        string pressed = keys.Length == 0 ? "无按键按下" : string.Join(" + ", keys.Select(KeyboardMapping.KeyName));
        MappingKeyboardStatus = $"{pressed} · 鼠标 ΔX {delta.X:F0} / ΔY {delta.Y:F0} · {(IsKeyboardMouseInput ? SelectedMouseMode : "键鼠补充")}";
    }
    private void LoadKeyboardBindings(IReadOnlyDictionary<int, ControllerButtons> bindings)
    {
        CancelMappingCapture(); keyboardDraft.Clear();
        foreach (var binding in bindings) keyboardDraft[binding.Key] = binding.Value;
        RefreshKeyboardBindings();
    }
    private void LoadStickMappings(IReadOnlyDictionary<StickDirection, int> keyboard, StickMapping controller)
    {
        keyboardStickDraft = new(keyboard); controllerStickDraft = controller with { };
        OnPropertyChanged(nameof(MappingStickSummary));
        OnPropertyChanged(nameof(AllMappingRules));
    }
    private void RefreshKeyboardBindings()
    {
        KeyboardBindings.Clear();
        foreach (var binding in keyboardDraft.OrderBy(p => p.Key))
            KeyboardBindings.Add(new(binding.Key, $"{KeyboardMapping.KeyName(binding.Key)} → {new ButtonMappingChoice(binding.Value, EditedOutputMode)}"));
        NotifyMappingSelection();
        OnPropertyChanged(nameof(AllMappingRules));
    }
    [RelayCommand] private void RemoveKeyboardBinding(KeyboardBindingRow row)
    {
        if (!keyboardDraft.Remove(row.Key)) return;
        RefreshKeyboardBindings(); HasMappingChanges = true;
        ButtonMappingStatus = IsKeyboardMouseInput ? "已移除自定义键鼠绑定，保存后该键恢复默认行为。" : "已移除键鼠补充绑定，保存后此键不再输出。";
    }
    internal void LeaveMappingPage()
    {
        CancelMappingCapture(); mappingKeyboardPreview = null; MappingPressedKeys = []; MappingMouseDelta = default;
    }
}
public sealed record KeyboardBindingRow(int Key, string Label);
