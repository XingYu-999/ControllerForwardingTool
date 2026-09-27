using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public sealed record MappingOrigins(ControllerButtons Buttons, int[] Keys);

public static class MappingLookup
{
    // Compare displayed output buttons so Nintendo/Xbox face positions and shared output aliases agree.
    public static MappingOrigins Find(ControllerButtons displayed, VirtualControllerMode mode, bool keyboard,
        Ns2ButtonMapping mapping, IReadOnlyDictionary<int, ControllerButtons> bindings,
        IReadOnlyDictionary<StickDirection, int> sticks)
    {
        if (displayed == ControllerButtons.None) return new(ControllerButtons.None, []);
        var buttons = ControllerButtons.None;
        if (!keyboard)
            foreach (var source in Enum.GetValues<ControllerButtons>())
                if (source != ControllerButtons.None && MappingPreview.OutputButtons(mapping.Target(source), mode) == displayed) buttons |= source;
        var keys = new List<int>();
        foreach (int key in Enumerable.Range(1, 254).Where(KeyboardMapping.CanBind))
            if (MappingPreview.OutputButtons(KeyTarget(key, keyboard, mapping, bindings, sticks), mode) == displayed) keys.Add(key);
        return new(buttons, keys.ToArray());
    }

    public static ControllerButtons KeyTarget(int key, bool keyboard, Ns2ButtonMapping mapping,
        IReadOnlyDictionary<int, ControllerButtons> bindings, IReadOnlyDictionary<StickDirection, int> sticks) =>
        keyboard ? KeyboardMapping.Apply(ControllerState.Neutral(DateTimeOffset.MinValue), new HashSet<int> { key }, mapping, bindings, sticks).Buttons
        : bindings.GetValueOrDefault(key);
}
