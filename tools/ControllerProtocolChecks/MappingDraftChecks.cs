using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class MappingDraftChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var now = DateTimeOffset.Now;
        var neutral = ControllerState.Neutral(now);
        var raw = neutral with { Buttons = ControllerButtons.A, LeftX = 3100, RightX = 900, GyroX = 120 };
        var saved = new BridgeOptions { Mode = VirtualControllerMode.Ns1Pro, KeyboardMouseSupplementEnabled = true,
            Ns2Buttons = new() { Bindings = new() { [ControllerButtons.A] = ControllerButtons.B } } };
        var draft = saved with { Ns2Buttons = new() { Bindings = new() { [ControllerButtons.A] = ControllerButtons.X } },
            KeyboardOverrides = new() { ['P'] = ControllerButtons.ZR },
            ControllerSticks = new() { Left = StickSource.Right, Right = StickSource.Left } };
        var keys = new HashSet<int> { 'P' };
        var preview = MappingDraftPreview.Create(raw, neutral, keys, false, draft, null, true, now);
        Check(preview.Output.Buttons == (ControllerButtons.X | ControllerButtons.ZR), "unsaved physical and supplemental mappings are immediately testable");
        Check(preview.Output.LeftX == 900 && preview.Output.RightX == 3100, "unsaved physical stick swap is visible");
        Check(preview.Source == raw && saved.Ns2Buttons.Apply(raw).Buttons == ControllerButtons.B,
            "draft preview preserves raw source and cannot mutate saved/output settings");
        Check(preview.Output.RightTriggerValue == 255 && preview.Output.GyroX == raw.GyroX,
            "draft preview keeps digital trigger and default physical gyro semantics");
        var mouse = KeyboardMouseMapper.Map(_ => false, 10, -5, .02, MouseEmulationMode.Gyroscope, now);
        preview = MappingDraftPreview.Create(raw, mouse, keys, false, draft with { GyroSource = GyroInputSource.Mouse }, null, true, now);
        Check(preview.Output.GyroX == mouse.GyroX && preview.Output.LeftX == 900, "unsaved mouse gyro selection preserves physical stick mapping");
        preview = MappingDraftPreview.Create(raw, mouse, keys, false,
            draft with { KeyboardMouseSupplementEnabled = false, GyroSource = GyroInputSource.Mouse }, null, true, now);
        Check(preview.Output.Buttons == ControllerButtons.X && preview.Output.RightTriggerValue == 0 &&
            preview.Output.GyroX == raw.GyroX && preview.Output.LeftX == 900,
            "disabled draft supplement excludes keys and mouse gyro while preserving physical mapping and motion");
        preview = MappingDraftPreview.Create(raw, mouse, keys, false, draft, null, false, now);
        Check(preview.Output.GyroX == mouse.GyroX, "draft preview falls back to mouse when physical gyro is missing");
        preview = MappingDraftPreview.Create(raw, neutral, keys, false, saved, null, true, now);
        Check(preview.Output.Buttons == ControllerButtons.B && preview.Output.LeftX == raw.LeftX, "reverted draft immediately previews restored mapping");
        preview = MappingDraftPreview.Create(raw with { ReceivedAt = now.AddSeconds(-1) }, mouse, keys, false, draft, null, false, now);
        Check(preview.Output == ControllerState.Neutral(DateTimeOffset.MinValue), "disconnected physical source neutralizes draft hybrid preview");
        preview = MappingDraftPreview.Create(raw, mouse with { ReceivedAt = now.AddSeconds(-1) }, keys, false, draft, null, true, now);
        Check(preview.Output.Buttons == ControllerButtons.X, "stale keyboard preview cannot leave supplemental buttons held");

        var keyboardDraft = new BridgeOptions { Mode = VirtualControllerMode.Ns1Pro,
            KeyboardOverrides = new() { ['J'] = ControllerButtons.Y }, KeyboardStickBindings = new() { [StickDirection.LeftUp] = 'T' } };
        var keyboardKeys = new HashSet<int> { 'J', 'T' };
        var keyboard = KeyboardMouseMapper.Map(keyboardKeys.Contains, 8, 0, .02, MouseEmulationMode.RightStick, now);
        preview = MappingDraftPreview.Create(raw, keyboard, keyboardKeys, true, keyboardDraft, null, false, now);
        Check(preview.Output.Buttons == ControllerButtons.Y && preview.Output.LeftY == 4095, "unsaved keyboard buttons and direction keys are immediately testable");
        Check(preview.Output.RightX == keyboard.RightX && preview.Source == keyboard, "keyboard draft retains mouse axis and original input diagram");
        preview = MappingDraftPreview.Create(raw, null, keyboardKeys, true, keyboardDraft, null, false, now);
        Check(preview.Output.Buttons == ControllerButtons.None && preview.Output.LeftY == 2048, "unfocused keyboard preview clears held keys and axes");
        var filtered = draft with { StickDeadzone = .3 };
        preview = MappingDraftPreview.Create(raw with { LeftX = 2150, RightX = 2200 }, neutral, new HashSet<int>(), false, filtered, null, true, now);
        Check(preview.Output.LeftX == 2048 && preview.Output.RightX == 2048, "draft preview applies the route's deadzone before swapping sticks");
        return count;
    }
}
