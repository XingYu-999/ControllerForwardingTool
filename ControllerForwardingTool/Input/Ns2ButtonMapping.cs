using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public sealed record Ns2ButtonMapping
{
    // Kept under the existing settings name for compatibility; applies to every input source.
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
        byte? Trigger(ControllerButtons trigger)
        {
            if (state.AnalogLeftTrigger is null && state.AnalogRightTrigger is null) return null;
            // Trigger-to-trigger mappings preserve partial travel, even below the digital press threshold.
            byte level = 0;
            if (Target(ControllerButtons.ZL) == trigger) level = state.LeftTriggerValue;
            if (Target(ControllerButtons.ZR) == trigger) level = Math.Max(level, state.RightTriggerValue);
            if (Bindings.Any(x => x.Key is not (ControllerButtons.ZL or ControllerButtons.ZR) &&
                                  x.Value == trigger && (state.Buttons & x.Key) != 0)) level = 255;
            return level;
        }
        return state with { Buttons = buttons,
            AnalogLeftTrigger = Trigger(ControllerButtons.ZL),
            AnalogRightTrigger = Trigger(ControllerButtons.ZR) };
    }
}
