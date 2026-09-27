using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

/// <summary>Uncommitted axis edits for one stick; its click remains a separate button mapping.</summary>
public sealed class StickMappingEditSession
{
    private readonly Dictionary<StickDirection, int> keys = [];
    private readonly Dictionary<StickDirection, int> changes = [];
    private bool ready;
    public bool KeyboardOnly { get; }
    public StickSource Target { get; }
    public StickSource Source { get; set; }
    public StickDirection? Recording { get; private set; }
    public bool Cancelled { get; private set; }
    public IReadOnlyDictionary<StickDirection, int> Keys => keys;
    public IEnumerable<int> AssignedKeys => changes.Values.Where(k => k != 0);
    public string Hint { get; private set; }

    public StickMappingEditSession(bool keyboardOnly, StickSource target, BridgeOptions options)
    {
        KeyboardOnly = keyboardOnly; Target = target;
        Source = target == StickSource.Left ? options.ControllerSticks.Left : options.ControllerSticks.Right;
        foreach (var direction in Enum.GetValues<StickDirection>().Where(d => ((int)d < 4) == (target == StickSource.Left)))
        {
            int key = KeyboardStickMapping.KeyFor(direction, options.KeyboardStickBindings);
            keys[direction] = options.KeyboardOverrides.ContainsKey(key) ? 0 : key;
        }
        Hint = keyboardOnly ? "点击一个方向，再按下源按键；四个方向分别配置。" : "选择源摇杆，或先回中再推动一个源摇杆；上下左右会整体映射。";
    }
    public void RequireRelease() { ready = false; Recording = null; }
    public void BeginDirection(StickDirection direction)
    {
        if (!KeyboardOnly || Cancelled || !keys.ContainsKey(direction)) return;
        Recording = direction; ready = false;
        Hint = $"请按下{KeyboardStickMapping.Name(direction)}的源按键；鼠标左键使用下方独立按钮。";
    }
    private void SetKey(StickDirection direction, int key)
    {
        if (key != 0) RemoveKey(key);
        keys[direction] = changes[direction] = key;
        Recording = null; ready = false;
    }
    public void RemoveKey(int key)
    {
        if (key == 0) return;
        foreach (var direction in keys.Where(p => p.Value == key).Select(p => p.Key).ToArray())
            keys[direction] = changes[direction] = 0;
    }
    public void ClearDirection(StickDirection direction)
    {
        if (!KeyboardOnly || Cancelled || !keys.ContainsKey(direction)) return;
        SetKey(direction, 0); Hint = "已清除此方向的源键，确认后生效。";
    }
    public void BindMouseLeft()
    {
        if (Cancelled || Recording is not { } direction) return;
        SetKey(direction, 1); Hint = "已绑定鼠标左键，确认后生效。";
    }
    public bool Observe(IReadOnlyCollection<int> pressed, ControllerState? state)
    {
        if (Cancelled) return false;
        if (pressed.Contains(27)) { Cancel(); return true; }
        if (KeyboardOnly)
        {
            if (Recording is not { } direction) return false;
            if (pressed.Count == 0) { ready = true; return false; }
            if (!ready) return false;
            ready = false;
            if (pressed.Contains(1)) { Hint = "界面点击不会被录入；鼠标左键请使用独立按钮。"; return false; }
            if (pressed.Count != 1 || !KeyboardMapping.CanBind(pressed.First()))
            { Hint = "一次只按一个源键；Esc 取消，F8 / Alt / Windows 键保留。"; return false; }
            SetKey(direction, pressed.First()); Hint = "方向已录入。可继续选择其他方向，最后点击确认。";
            return true;
        }
        if (state is null) { ready = false; return false; }
        double left = Math.Max(Math.Abs(StickCoordinates.Unit(state.LeftX)), Math.Abs(StickCoordinates.Unit(state.LeftY)));
        double right = Math.Max(Math.Abs(StickCoordinates.Unit(state.RightX)), Math.Abs(StickCoordinates.Unit(state.RightY)));
        if (pressed.Count == 0 && state.Buttons == ControllerButtons.None && left < .25 && right < .25)
        { ready = true; return false; }
        if (!ready) return false;
        if (pressed.Count > 0 || state.Buttons != ControllerButtons.None)
        { ready = false; Hint = "摇杆轴只能由实体摇杆提供；请松开按键并让摇杆回中。"; return false; }
        if (left >= .65 && right < .25) Source = StickSource.Left;
        else if (right >= .65 && left < .25) Source = StickSource.Right;
        else { if (left >= .65 && right >= .65) { ready = false; Hint = "请只推动一个源摇杆。"; } return false; }
        ready = false; Hint = $"已选择源{(Source == StickSource.Left ? "左" : "右")}摇杆，确认后整体映射上下左右。";
        return true;
    }
    public void Cancel() { Cancelled = true; Recording = null; changes.Clear(); }
    public BridgeOptions Apply(BridgeOptions current)
    {
        if (Cancelled) return current;
        if (!KeyboardOnly)
            return current with { ControllerSticks = Target == StickSource.Left
                ? current.ControllerSticks with { Left = Source } : current.ControllerSticks with { Right = Source } };
        var directions = new Dictionary<StickDirection, int>(current.KeyboardStickBindings);
        var buttons = new Dictionary<int, ControllerButtons>(current.KeyboardOverrides);
        // Clear changed destinations before assigning keys so swaps cannot erase one another.
        foreach (var direction in changes.Keys) directions[direction] = 0;
        foreach (var (direction, key) in changes)
        {
            if (key == 0) continue;
            foreach (var other in Enum.GetValues<StickDirection>())
                if (KeyboardStickMapping.KeyFor(other, directions) == key) directions[other] = 0;
            buttons.Remove(key); directions[direction] = key;
        }
        return current with { KeyboardStickBindings = directions, KeyboardOverrides = buttons };
    }
}
