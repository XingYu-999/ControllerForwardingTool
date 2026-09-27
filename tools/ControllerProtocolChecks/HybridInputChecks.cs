using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class HybridInputChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var neutral = ControllerState.Neutral(DateTimeOffset.Now);
        var physical = neutral with { Buttons = ControllerButtons.B, LeftX = 3100, LeftY = 1200,
            RightX = 1800, RightY = 2500, AnalogLeftTrigger = 85, GyroX = 111, AccelZ = 4000 };
        var mouse = KeyboardMouseMapper.Map(_ => true, 12, -8, .01, MouseEmulationMode.Gyroscope, DateTimeOffset.Now);
        Check(!new BridgeOptions().KeyboardMouseSupplementEnabled && !new OutputRouteOptions().KeyboardMouseSupplementEnabled,
            "keyboard/mouse supplement defaults off for new configurations and output routes");
        var legacy = JsonSerializer.Deserialize<BridgeOptions>("{\"GyroSource\":2,\"KeyboardOverrides\":{\"80\":1}}")!.Normalize();
        Check(!legacy.KeyboardMouseSupplementEnabled && legacy.KeyboardOverrides.ContainsKey('P') &&
            !JsonSerializer.Deserialize<OutputRouteOptions>("{}")!.KeyboardMouseSupplementEnabled,
            "legacy settings retain bindings but do not opt in to supplementary input");
        var options = new BridgeOptions { Mode = VirtualControllerMode.Ns1Pro, KeyboardMouseSupplementEnabled = true, KeyboardOverrides = new()
            { ['P'] = ControllerButtons.X, ['T'] = ControllerButtons.ZL, ['W'] = ControllerButtons.Plus } };
        foreach (var source in Enum.GetValues<GyroInputSource>())
            Check(HybridInputMapper.Merge(physical, mouse, new HashSet<int> { 'P', 'T', 'W' },
                options with { KeyboardMouseSupplementEnabled = false, GyroSource = source }, true) == physical,
                $"disabled supplement preserves physical buttons, analog triggers, sticks and motion with {source} gyro preference");
        var merged = HybridInputMapper.Merge(physical, mouse, new HashSet<int> { 'P', 'T', 'W', 'J', 1 }, options, false);
        Check(merged.Buttons == (ControllerButtons.B | ControllerButtons.X | ControllerButtons.ZL | ControllerButtons.Plus),
            "hybrid enables only explicit keyboard bindings, no keyboard-only defaults");
        Check(merged.LeftX == physical.LeftX && merged.LeftY == physical.LeftY && merged.RightX == physical.RightX && merged.RightY == physical.RightY,
            "WASD and mouse never replace either physical stick");
        Check(merged.LeftTriggerValue == 255, "supplemental digital trigger overrides partial analog travel");
        var released = HybridInputMapper.Merge(physical, neutral, new HashSet<int>(), options, false);
        Check(released.Buttons == physical.Buttons && released.LeftTriggerValue == 85, "keyboard release restores physical buttons and analog travel");
        Check(merged.GyroX == mouse.GyroX && merged.AccelZ == mouse.AccelZ, "automatic gyro falls back to mouse for a controller without motion");
        merged = HybridInputMapper.Merge(physical, mouse, new HashSet<int>(), options, true);
        Check(merged.GyroX == physical.GyroX && merged.AccelZ == physical.AccelZ, "automatic gyro prioritizes real controller motion");
        Check(HybridInputMapper.Merge(physical with { GyroX = 0 }, mouse, new HashSet<int>(), options, true).GyroX == 0,
            "stationary physical gyro is not mistaken for missing capability");
        foreach (var mode in Enum.GetValues<VirtualControllerMode>())
        {
            Check(HybridInputMapper.UsesMouse(GyroInputSource.Automatic, false, mode) == (mode != VirtualControllerMode.Xbox360), $"{mode} only uses mouse gyro when supported");
            Check(!HybridInputMapper.UsesMouse(GyroInputSource.Automatic, true, mode), $"{mode} prefers physical sensor by default");
            Check(HybridInputMapper.UsesMouse(GyroInputSource.Mouse, true, mode) == (mode != VirtualControllerMode.Xbox360), $"{mode} allows explicit mouse override when supported");
        }
        Check(HybridInputMapper.Merge(physical, mouse, new HashSet<int>(), options with { GyroSource = GyroInputSource.Mouse }, true).GyroX == mouse.GyroX,
            "explicit mouse gyro overrides an available physical sensor");
        Check(HybridInputMapper.Merge(physical, mouse, new HashSet<int>(), options with { GyroSource = GyroInputSource.Controller }, false).GyroX == physical.GyroX,
            "explicit controller choice does not silently select mouse");

        // Exercise real callback interleaving and the encoded NS1 output, not only the merge helper.
        var device = new UsbIpNs1Device();
        byte[] enable = new byte[49]; enable[0] = 1; enable[10] = 0x40; enable[11] = 1;
        device.HandleInterruptOut(enable); device.ReadInput(64);
        ControllerState latest = neutral;
        var bridge = new ControllerInputBridge(s => { latest = s; device.Publish(s); }, () => options, _ => null);
        var pad = new GamepadDevice(12, "Physical Xbox", 0x45e, 0x28e, true, ControllerLayout.Xbox);
        var snapshot = new GamepadSnapshot(ControllerButtons.A, [.5, -.25, -.4, .3, .25, 0], [], [], 100, true, null, null, []);
        var frame = new GamepadFrame([pad], new Dictionary<uint, GamepadSnapshot> { [pad.Id] = snapshot }, "", 1);
        bridge.Select(BridgeInputKind.WindowsGamepad, pad); bridge.Windows(frame, 1);
        var raw = bridge.Comparison.Source;
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P', 'T' });
        Check(latest.Buttons == (ControllerButtons.B | ControllerButtons.X | ControllerButtons.ZL), "Windows source and keyboard buttons reach the same bridge output");
        Check(bridge.Comparison.Source == raw, "hybrid merge preserves the raw physical source diagram");
        Check(latest.LeftX == raw.LeftX && latest.RightY == raw.RightY && latest.GyroX == mouse.GyroX, "hybrid bridge sends physical axes with mouse gyro");
        var report = device.ReadInput(64);
        Check((report[3] & 6) == 6 && (report[5] & 0x80) != 0, "actual NS1 report contains physical and supplemental buttons");
        Check(report.AsSpan(13, 36).ToArray().Any(x => x != 0), "actual NS1 IMU report contains mouse motion");
        bridge.Windows(frame, 1);
        Check(latest.Buttons.HasFlag(ControllerButtons.X), "next controller frame cannot erase a held supplemental key");
        var beforeDisable = bridge.Comparison.Source;
        options = options with { KeyboardMouseSupplementEnabled = false };
        bridge.ClearSupplement();
        Check(latest.Buttons == ControllerButtons.B && latest.LeftTriggerValue == raw.LeftTriggerValue &&
            latest.GyroX == raw.GyroX && bridge.Comparison.Source == beforeDisable,
            "disabling supplement immediately releases held keys and mouse motion without changing physical input");
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P', 'T' });
        Check(latest.Buttons == ControllerButtons.B && latest.GyroX == raw.GyroX,
            "late supplementary callbacks remain suppressed while disabled");
        options = options with { KeyboardMouseSupplementEnabled = true };
        bridge.Windows(frame, 1);
        Check(latest.Buttons == ControllerButtons.B && latest.GyroX == 0,
            "re-enabling supplement does not replay keys or motion observed while disabled");
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P', 'T' });
        Check(latest.Buttons.HasFlag(ControllerButtons.X) && latest.GyroX == mouse.GyroX,
            "fresh input resumes configured supplement after enabling it");
        bridge.Supplement(ControllerState.Neutral(DateTimeOffset.Now), new HashSet<int>());
        Check(latest.Buttons == ControllerButtons.B && latest.GyroX == 0 && latest.LeftTriggerValue == raw.LeftTriggerValue,
            "capture release clears only supplemental input and restores analog triggers");
        options = options with { KeyboardOverrides = new() { ['P'] = ControllerButtons.B } };
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P' });
        bridge.Windows(frame with { Inputs = new Dictionary<uint, GamepadSnapshot> { [pad.Id] = snapshot with { Buttons = ControllerButtons.None } } }, 1);
        Check(latest.Buttons == ControllerButtons.B, "keyboard keeps shared target down after physical button release");
        bridge.Windows(new([], new Dictionary<uint, GamepadSnapshot>(), "", 2), 2);
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P' });
        Check(latest == ControllerState.Neutral(DateTimeOffset.MinValue), "physical disconnect neutralizes hybrid output despite held keyboard input");
        bridge.Windows(frame, 1);
        Check(latest.GyroX == 0, "reconnecting physical input does not replay old mouse motion");
        bridge.Select(BridgeInputKind.Ns2Ble, null); bridge.Ble(physical with { ReceivedAt = DateTimeOffset.Now }, null);
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P' });
        Check(latest.GyroX == physical.GyroX && latest.LeftX == physical.LeftX, "native BLE keeps physical gyro and sticks by default");
        options = options with { GyroSource = GyroInputSource.Mouse };
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int>());
        Check(latest.GyroX == mouse.GyroX, "saved gyro source changes apply on the next supplemental frame");
        bridge.DisconnectBle();
        Check(latest == ControllerState.Neutral(DateTimeOffset.MinValue), "BLE disconnect clears all hybrid state");
        bridge.Ble(physical with { ReceivedAt = DateTimeOffset.Now.AddSeconds(-1) }, null);
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int> { 'P' });
        Check(latest == ControllerState.Neutral(DateTimeOffset.MinValue), "supplementary timer cannot keep stale BLE input alive");
        bridge.Select(BridgeInputKind.KeyboardMouse, null); bridge.KeyboardMouse(physical);
        var keyboardOutput = latest; bridge.Supplement(mouse, new HashSet<int> { 'P' });
        Check(latest == keyboardOutput, "late supplemental callback cannot overwrite primary keyboard input");
        options = options with { KeyboardMouseSupplementEnabled = false };
        bridge.KeyboardMouse(physical); bridge.ClearSupplement();
        Check(latest == keyboardOutput, "disabled supplement does not block or clear primary keyboard/mouse control");

        options = options with { GyroSource = GyroInputSource.Automatic, KeyboardMouseSupplementEnabled = true };
        bridge.Select(BridgeInputKind.WindowsGamepad, pad);
        var sensorFrame = frame with { Inputs = new Dictionary<uint, GamepadSnapshot>
            { [pad.Id] = snapshot with { Accel = [0, 1, 0], Gyro = [0, 0, 0] } } };
        bridge.Windows(sensorFrame, 1);
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int>());
        Check(latest.GyroX == 0, "SDL sensor capability prevents mouse fallback while physical motion is stationary or awaiting calibration");
        bridge.Select(BridgeInputKind.Ns2Ble, null); bridge.SelectNs2Usb(pad); bridge.Windows(sensorFrame, 1);
        options = options with { GyroSource = GyroInputSource.Mouse };
        bridge.Supplement(mouse with { ReceivedAt = DateTimeOffset.Now }, new HashSet<int>());
        Check(latest.GyroX == mouse.GyroX && latest.LeftX == raw.LeftX, "native NS2 USB also supports mouse gyro with physical axes");
        bridge.DisconnectBle();
        Check(latest.GyroX == mouse.GyroX, "BLE disconnect cannot clear an active USB hybrid source");
        bridge.SelectNs2Usb(null);
        Check(latest == ControllerState.Neutral(DateTimeOffset.MinValue), "USB source removal clears supplemental state");

        var mapping = new Ns2ButtonMapping { Bindings = new()
            { [ControllerButtons.B] = ControllerButtons.X, [ControllerButtons.A] = ControllerButtons.X, [ControllerButtons.X] = ControllerButtons.None } };
        var keys = new Dictionary<int, ControllerButtons> { ['P'] = ControllerButtons.X, ['J'] = ControllerButtons.Y };
        foreach (var mode in Enum.GetValues<VirtualControllerMode>())
        {
            var displayed = MappingPreview.OutputButtons(ControllerButtons.X, mode);
            var origins = MappingLookup.Find(displayed, mode, false, mapping, keys, new Dictionary<StickDirection, int>());
            Check(origins.Buttons == (ControllerButtons.B | ControllerButtons.A) && origins.Keys.SequenceEqual(new[] { (int)'P' }),
                $"{mode} reverse lookup includes all physical and supplemental sources at the correct face position");
            origins = MappingLookup.Find(displayed, mode, true, mapping, keys, new Dictionary<StickDirection, int>());
            Check(origins.Buttons == ControllerButtons.None && origins.Keys.Order().SequenceEqual(new[] { 32, (int)'K', (int)'P' }),
                $"{mode} keyboard reverse lookup respects default keys and overridden keys");
        }
        Check(MappingLookup.Find(ControllerButtons.X, VirtualControllerMode.Ns1Pro, false,
            new() { Bindings = new() { [ControllerButtons.X] = ControllerButtons.None } }, new Dictionary<int, ControllerButtons>(), new Dictionary<StickDirection, int>()).Buttons == ControllerButtons.None,
            "unbound output does not fall back to an unrelated source button");

        var wizard = new QuickMappingSession(false, VirtualControllerMode.Ns1Pro, ControllerButtons.B);
        wizard.Observe([], neutral); wizard.Observe(['P'], neutral);
        Check(wizard.Completed && wizard.KeyboardButtons['P'] == ControllerButtons.B, "single-button wizard accepts keyboard in physical mode");
        wizard = new(false, VirtualControllerMode.Ns1Pro, ControllerButtons.B);
        wizard.Observe([], neutral); wizard.Observe(['P'], physical);
        Check(wizard.CapturedCount == 0, "hybrid wizard rejects simultaneous keyboard and physical input");
        wizard.Observe([], neutral); wizard.Observe([4], neutral);
        Check(wizard.Completed && wizard.KeyboardButtons[4] == ControllerButtons.B, "hybrid wizard accepts a mouse button");
        wizard = new(false, VirtualControllerMode.Ns1Pro, ControllerButtons.LeftStick);
        wizard.Observe([], neutral); wizard.Observe(['Q'], neutral);
        Check(wizard.Completed && wizard.KeyboardButtons['Q'] == ControllerButtons.LeftStick, "keyboard can replace stick click without replacing its axes");
        wizard = new(false, VirtualControllerMode.Ns1Pro);
        wizard.Observe([], neutral); wizard.Observe(['W'], neutral with { LeftX = 4095 });
        Check(wizard.Index == 0 && wizard.CapturedCount == 0, "physical stick wizard refuses combined keyboard and axis input");
        wizard.Observe([], neutral); wizard.Observe([], neutral with { LeftX = 4095 });
        Check(wizard.ControllerSticks[StickSource.Left] == StickSource.Left, "physical stick wizard still accepts a clean axis movement");
        wizard.Cancel(); Check(wizard.CapturedCount == 0, "hybrid wizard escape rollback remains transactional");

        var saved = new BridgeOptions().SaveRoute(VirtualControllerMode.Ns1Pro, new() { GyroSource = GyroInputSource.Mouse, KeyboardOverrides = keys, KeyboardMouseSupplementEnabled = true });
        var restored = JsonSerializer.Deserialize<BridgeOptions>(JsonSerializer.Serialize(saved))!;
        Check(restored.SelectRoute(VirtualControllerMode.Ns1Pro).GyroSource == GyroInputSource.Mouse && restored.SelectRoute(VirtualControllerMode.Ns1Pro).KeyboardOverrides['P'] == ControllerButtons.X,
            "hybrid preferences survive settings round trip");
        Check(restored.SelectRoute(VirtualControllerMode.DualSense).GyroSource == GyroInputSource.Automatic, "gyro choice remains independent per output route");
        Check(restored.SelectRoute(VirtualControllerMode.Ns1Pro).KeyboardMouseSupplementEnabled &&
            !restored.SelectRoute(VirtualControllerMode.DualSense).KeyboardMouseSupplementEnabled,
            "supplement opt-in persists independently for each output route");
        var disabledRoute = restored.SaveRoute(VirtualControllerMode.Ns1Pro,
            restored.Route(VirtualControllerMode.Ns1Pro) with { KeyboardMouseSupplementEnabled = false });
        Check(!disabledRoute.SelectRoute(VirtualControllerMode.Ns1Pro).KeyboardMouseSupplementEnabled &&
            disabledRoute.SelectRoute(VirtualControllerMode.Ns1Pro).KeyboardOverrides['P'] == ControllerButtons.X &&
            disabledRoute.SelectRoute(VirtualControllerMode.Ns1Pro).GyroSource == GyroInputSource.Mouse,
            "saving a disabled switch preserves existing button and gyro preferences");
        Check(new OutputRouteOptions { GyroSource = (GyroInputSource)100 }.Normalize().GyroSource == GyroInputSource.Automatic &&
            JsonSerializer.Deserialize<OutputRouteOptions>("{}")!.GyroSource == GyroInputSource.Automatic, "invalid and legacy gyro preferences default to automatic");
        return count;
    }
}
