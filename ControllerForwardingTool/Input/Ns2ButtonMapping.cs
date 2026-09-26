using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public sealed record Ns2ButtonMapping
{
    public Dictionary<ControllerButtons, ControllerButtons> Bindings { get; init; } = new()
    {
        [ControllerButtons.GL] = ControllerButtons.LeftStick,
        [ControllerButtons.GR] = ControllerButtons.RightStick
    };
    public static bool IsButton(ControllerButtons button) => button != ControllerButtons.None && Enum.IsDefined(button);
    public Ns2ButtonMapping Normalize() => this with
    {
        Bindings = (Bindings ?? new Ns2ButtonMapping().Bindings)
            .Where(x => IsButton(x.Key) && (x.Value == ControllerButtons.None || IsButton(x.Value)))
            .ToDictionary(x => x.Key, x => x.Value)
    };
    public ControllerButtons Target(ControllerButtons source) => Bindings.GetValueOrDefault(source, source);
    public ControllerState Apply(ControllerState state)
    {
        // Evaluate against the original frame: swaps do not cascade and multiple
        // physical buttons mapped to one target are combined until all are released.
        var buttons = state.Buttons;
        foreach (var rule in Bindings) buttons &= ~rule.Key;
        foreach (var rule in Bindings)
            if ((state.Buttons & rule.Key) != 0) buttons |= rule.Value;
        byte? Trigger(ControllerButtons trigger, byte? original)
        {
            bool added = Bindings.Any(x => x.Key != trigger && x.Value == trigger && (state.Buttons & x.Key) != 0);
            return added ? (byte)255 : Target(trigger) == trigger ? original : null;
        }
        return state with { Buttons = buttons,
            AnalogLeftTrigger = Trigger(ControllerButtons.ZL, state.AnalogLeftTrigger),
            AnalogRightTrigger = Trigger(ControllerButtons.ZR, state.AnalogRightTrigger) };
    }
}
