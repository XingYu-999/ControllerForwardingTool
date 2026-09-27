using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

/// <summary>Evaluate the editor's current route from raw input, independently of saved/live output.</summary>
public static class MappingDraftPreview
{
    public static BridgeComparison Create(ControllerState physical, ControllerState? keyboard, IReadOnlySet<int> keys,
        bool keyboardOnly, BridgeOptions draft, StickProfile? profile, bool controllerHasMotion, DateTimeOffset now)
    {
        var neutral = ControllerState.Neutral(DateTimeOffset.MinValue);
        bool Fresh(ControllerState? state) => state is not null && state.ReceivedAt <= now &&
            now - state.ReceivedAt < TimeSpan.FromMilliseconds(250);
        bool keyboardFresh = Fresh(keyboard);
        var keyboardState = keyboardFresh ? keyboard! : neutral;
        var pressed = keyboardFresh ? keys : new HashSet<int>();
        if (keyboardOnly)
        {
            var mapped = KeyboardMapping.Apply(keyboardState, pressed, draft.Ns2Buttons, draft.KeyboardOverrides, draft.KeyboardStickBindings);
            return new(keyboardState, StickMath.Apply(mapped, null, draft));
        }
        // Match forwarding's disconnect behavior; keyboard supplement cannot keep a lost pad alive.
        if (!Fresh(physical)) return new(neutral, neutral);
        var output = draft.ControllerSticks.Apply(StickMath.Apply(draft.Ns2Buttons.Apply(physical), profile, draft));
        return new(physical, HybridInputMapper.Merge(output, keyboardState, pressed, draft, controllerHasMotion));
    }
}
