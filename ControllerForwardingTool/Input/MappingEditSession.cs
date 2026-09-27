using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public sealed record MappingDestination(ControllerButtons Button = ControllerButtons.None, StickDirection? Direction = null);

/// <summary>A single rule/target editor. Input only changes this draft; Apply requires explicit confirmation.</summary>
public sealed class MappingEditSession
{
    private readonly HashSet<ControllerButtons> initialButtons, buttons;
    private readonly HashSet<int> initialKeys, keys;
    private bool keyboardReady, controllerReady;
    public bool KeyboardOnly { get; }
    public bool Cancelled { get; private set; }
    private MappingDestination target = new();
    public MappingDestination Target
    {
        get => target;
        set
        {
            target = value;
            if (value.Direction is not null && keys.Count > 1)
            {
                int keep = keys.First(); keys.Clear(); keys.Add(keep);
                Hint = "一个摇杆方向使用一个键；录入新键会替换当前方向键。";
            }
        }
    }
    public IReadOnlyCollection<ControllerButtons> Buttons => buttons;
    public IReadOnlyCollection<int> Keys => keys;
    public string Hint { get; private set; } = "松开按键后录入；可以依次添加多个来源，最后点击确认。";

    public MappingEditSession(bool keyboardOnly, MappingDestination target, IEnumerable<ControllerButtons> sources, IEnumerable<int> keyboard)
    {
        KeyboardOnly = keyboardOnly;
        initialButtons = sources.ToHashSet(); buttons = new(initialButtons);
        initialKeys = keyboard.ToHashSet(); keys = new(initialKeys);
        Target = target;
    }
    public void RemoveButton(ControllerButtons button) => buttons.Remove(button);
    public void RemoveKey(int key) => keys.Remove(key);
    public void RequireRelease() { keyboardReady = false; controllerReady = false; }
    private bool AddKey(int key)
    {
        if (Target.Direction is not null && !keys.Contains(key)) keys.Clear();
        return keys.Add(key);
    }
    public void SetMouseLeft(bool enabled) { if (enabled) AddKey(1); else keys.Remove(1); }
    public void Cancel() { Cancelled = true; buttons.Clear(); keys.Clear(); }
    public bool Observe(IReadOnlyCollection<int> pressed, ControllerState? source)
    {
        if (Cancelled) return false;
        if (pressed.Contains(27)) { Cancel(); return true; }
        bool changed = false;
        // A primary mouse click is always UI input. It can only be enabled by SetMouseLeft.
        if (pressed.Count == 0) keyboardReady = true;
        else if (keyboardReady)
        {
            keyboardReady = false;
            if (pressed.Contains(1)) Hint = "鼠标左键请使用独立勾选项，点击界面不会被录入。";
            else if (pressed.Count == 1 && KeyboardMapping.CanBind(pressed.First()))
            {
                changed |= AddKey(pressed.First());
                Hint = "已加入键鼠来源。可继续按手柄或键鼠按键，点击确认后提交。";
            }
            else Hint = "一次只录入一个键鼠按键；Esc 取消，F8 / Alt / Windows 键保留。";
        }
        if (!KeyboardOnly && Target.Direction is null && source is not null)
        {
            bool centered = new[] { source.LeftX, source.LeftY, source.RightX, source.RightY }.All(v => Math.Abs(StickCoordinates.Unit(v)) < .25);
            if (source.Buttons == ControllerButtons.None && centered) controllerReady = true;
            else if (controllerReady)
            {
                controllerReady = false;
                if (centered && Ns2ButtonMapping.IsButton(source.Buttons))
                {
                    changed |= buttons.Add(source.Buttons);
                    Hint = "已加入手柄来源。可继续按键盘或手柄按键，点击确认后提交。";
                }
                else Hint = "请让摇杆回中，每次按一个手柄按钮；摇杆轴不能替代按钮。";
            }
        }
        return changed;
    }

    public BridgeOptions Apply(BridgeOptions current)
    {
        if (Cancelled) return current;
        if (Target.Direction is not null && (!KeyboardOnly || buttons.Count > 0))
            throw new InvalidOperationException("Only keyboard input can supply a keyboard stick direction.");
        var mapped = new Dictionary<ControllerButtons, ControllerButtons>(current.Ns2Buttons.Bindings);
        var keyboard = new Dictionary<int, ControllerButtons>(current.KeyboardOverrides);
        var sticks = new Dictionary<StickDirection, int>(current.KeyboardStickBindings);
        foreach (var source in initialButtons) mapped[source] = ControllerButtons.None;
        foreach (var key in initialKeys)
        {
            if (KeyboardOnly) keyboard[key] = ControllerButtons.None;
            else keyboard.Remove(key);
            if (KeyboardOnly)
                foreach (var direction in Enum.GetValues<StickDirection>())
                    if (KeyboardStickMapping.KeyFor(direction, sticks) == key) sticks[direction] = 0;
        }
        foreach (var source in buttons) mapped[source] = Target.Button;
        foreach (var key in keys)
        {
            if (Target.Direction is { } direction)
            {
                keyboard.Remove(key);
                foreach (var other in Enum.GetValues<StickDirection>())
                    if (KeyboardStickMapping.KeyFor(other, sticks) == key) sticks[other] = 0;
                sticks[direction] = key;
            }
            else keyboard[key] = Target.Button;
        }
        return current with { Ns2Buttons = new() { Bindings = mapped }, KeyboardOverrides = keyboard, KeyboardStickBindings = sticks };
    }
}
