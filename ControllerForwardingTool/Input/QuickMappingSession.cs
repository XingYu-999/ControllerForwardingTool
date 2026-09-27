using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public sealed record QuickMappingStep(ControllerButtons Button = ControllerButtons.None,
    StickDirection? Direction = null, StickSource? Stick = null);

/// <summary>Transactional wizard input. Only a completed session may be copied into the editor draft.</summary>
public sealed class QuickMappingSession
{
    private bool waitingForRelease = true;
    private readonly HashSet<int> usedKeys = [];
    private readonly HashSet<ControllerButtons> usedButtons = [];
    public bool Keyboard { get; }
    public IReadOnlyList<QuickMappingStep> Steps { get; }
    public int Index { get; private set; }
    public QuickMappingStep? Current => Active ? Steps[Index] : null;
    public bool Active { get; private set; } = true;
    public bool Completed { get; private set; }
    public string Hint { get; private set; } = "先松开按键，并让摇杆回中。";
    public Dictionary<int, ControllerButtons> KeyboardButtons { get; } = [];
    public Dictionary<StickDirection, int> KeyboardSticks { get; } = [];
    public Dictionary<ControllerButtons, ControllerButtons> ControllerButtons { get; } = [];
    public Dictionary<StickSource, StickSource> ControllerSticks { get; } = [];
    public int CapturedCount => KeyboardButtons.Count + KeyboardSticks.Count + ControllerButtons.Count + ControllerSticks.Count;

    public QuickMappingSession(bool keyboard, VirtualControllerMode mode, Core.ControllerButtons? onlyButton = null)
    {
        Keyboard = keyboard;
        var steps = keyboard
            ? Enum.GetValues<StickDirection>().Select(d => new QuickMappingStep(Direction: d)).ToList()
            : new List<QuickMappingStep> { new(Stick: StickSource.Left), new(Stick: StickSource.Right) };
        Core.ControllerButtons[] buttons = [Core.ControllerButtons.B, Core.ControllerButtons.A, Core.ControllerButtons.Y, Core.ControllerButtons.X,
            Core.ControllerButtons.L, Core.ControllerButtons.R, Core.ControllerButtons.ZL, Core.ControllerButtons.ZR,
            Core.ControllerButtons.LeftStick, Core.ControllerButtons.RightStick, Core.ControllerButtons.Up, Core.ControllerButtons.Down,
            Core.ControllerButtons.Left, Core.ControllerButtons.Right, Core.ControllerButtons.Plus, Core.ControllerButtons.Minus,
            Core.ControllerButtons.Home, Core.ControllerButtons.Capture, Core.ControllerButtons.C, Core.ControllerButtons.GL,
            Core.ControllerButtons.GR, Core.ControllerButtons.Touchpad];
        var outputs = new HashSet<Core.ControllerButtons>();
        foreach (var button in buttons)
        {
            var output = MappingPreview.OutputButtons(button, mode);
            if (output != Core.ControllerButtons.None && outputs.Add(output)) steps.Add(new(Button: button));
        }
        Steps = onlyButton is { } target ? steps.Where(s => s.Button == target).ToArray() : steps;
        if (Steps.Count == 0) throw new ArgumentException("Output button is not supported", nameof(onlyButton));
    }

    public void RequireRelease() { waitingForRelease = true; Hint = "请松开按键，并让摇杆回中，再继续录入。"; }
    public void Skip() { if (Active) Advance(); }
    public void BindMouseLeft()
    {
        if (Current is not { Stick: null } step) return;
        if (!usedKeys.Add(1)) { Hint = "鼠标左键已在本次向导中使用，请跳过或使用其他源键。"; return; }
        if (step.Direction is { } direction) KeyboardSticks[direction] = 1;
        else KeyboardButtons[1] = step.Button;
        Advance();
    }
    public void Cancel()
    {
        Active = false; Completed = false;
        KeyboardButtons.Clear(); KeyboardSticks.Clear(); ControllerButtons.Clear(); ControllerSticks.Clear();
    }
    private void Advance()
    {
        Index++;
        Active = Index < Steps.Count;
        Completed = !Active;
        RequireRelease();
    }
    private static double Magnitude(ushort x, ushort y) => Math.Max(Math.Abs(StickCoordinates.Unit(x)), Math.Abs(StickCoordinates.Unit(y)));

    public void Observe(IReadOnlyCollection<int> keys, ControllerState? source)
    {
        if (!Active) return;
        if (keys.Contains(27)) { Cancel(); return; }
        if (keys.Contains(1))
        { RequireRelease(); Hint = "鼠标左键请点击独立绑定按钮，普通界面点击不会被录入。"; return; }
        double left = source is null ? 0 : Magnitude(source.LeftX, source.LeftY);
        double right = source is null ? 0 : Magnitude(source.RightX, source.RightY);
        bool centered = left < .25 && right < .25;
        bool released = keys.Count == 0 && (Keyboard || (source?.Buttons ?? Core.ControllerButtons.None) == Core.ControllerButtons.None && centered);
        if (waitingForRelease)
        {
            if (released) { waitingForRelease = false; Hint = Keyboard ? "请按一个源键；鼠标按键也可使用。" : "摇杆使用源手柄；按钮可按手柄按键或键盘 / 鼠标按键。"; }
            return;
        }
        var step = Current!;
        if (Keyboard || step.Stick is null && keys.Count > 0)
        {
            if (keys.Count == 0) return;
            if (!Keyboard && (!centered || source?.Buttons is { } held && held != Core.ControllerButtons.None))
            { RequireRelease(); Hint = "请松开手柄，一次只录入一个键盘或鼠标按键。"; return; }
            if (keys.Count != 1 || !KeyboardMapping.CanBind(keys.First()))
            { RequireRelease(); Hint = "一次只按一个可绑定的键；Esc 取消，F8 / Alt / Windows 键保留。"; return; }
            int key = keys.First();
            if (!usedKeys.Add(key)) { RequireRelease(); Hint = "此键已在本次向导中使用，请换一个源键或跳过。"; return; }
            if (step.Direction is { } direction) KeyboardSticks[direction] = key;
            else KeyboardButtons[key] = step.Button;
            Advance();
        }
        else if (source is not null)
        {
            if (step.Stick is { } target)
            {
                if (source.Buttons != Core.ControllerButtons.None || keys.Count > 0)
                { RequireRelease(); Hint = "摇杆只能由摇杆映射；请松开按钮，再推动一个源摇杆。"; return; }
                if (left >= .65 && right < .25) ControllerSticks[target] = StickSource.Left;
                else if (right >= .65 && left < .25) ControllerSticks[target] = StickSource.Right;
                else { if (left >= .65 && right >= .65) RequireRelease(); return; }
                Advance();
            }
            else
            {
                var buttons = Enum.GetValues<Core.ControllerButtons>().Where(b => b != Core.ControllerButtons.None && source.Buttons.HasFlag(b)).ToArray();
                if (buttons.Length == 0) return;
                if (!centered || buttons.Length != 1) { RequireRelease(); Hint = "请让摇杆回中，一次只按一个源按键。"; return; }
                if (!usedButtons.Add(buttons[0])) { RequireRelease(); Hint = "此按键已在本次向导中使用，请换一个源按键或跳过。"; return; }
                ControllerButtons[buttons[0]] = step.Button;
                Advance();
            }
        }
    }
}
