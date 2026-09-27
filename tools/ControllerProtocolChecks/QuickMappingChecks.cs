using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class QuickMappingChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var neutral = ControllerState.Neutral(DateTimeOffset.Now);
        var keyboard = new QuickMappingSession(true, VirtualControllerMode.Ns1Pro);
        Check(keyboard.Current?.Direction == StickDirection.LeftUp, "keyboard wizard starts with left stick up");
        keyboard.Observe([1], null);
        Check(keyboard.Index == 0, "opening click cannot become first direction");
        keyboard.Observe([], null); keyboard.Observe(['T'], null);
        Check(keyboard.Current?.Direction == StickDirection.LeftDown && keyboard.KeyboardSticks[StickDirection.LeftUp] == 'T', "keyboard records direction and advances");
        keyboard.Observe(['T'], null);
        Check(keyboard.Index == 1, "held direction cannot fill next step");
        keyboard.Observe([], null); keyboard.Observe(['T'], null);
        Check(keyboard.Index == 1, "same source key cannot fill two wizard directions");
        keyboard.Observe([], null); keyboard.Observe([119], null);
        Check(keyboard.Index == 1, "reserved F8 cannot map a stick direction");
        keyboard.Observe([], null); keyboard.Observe(['F', 'G'], null); keyboard.Observe(['G'], null);
        Check(keyboard.Index == 1, "chords must fully release before another capture");
        int[] directionKeys = ['T', 'G', 'F', 'H', 38, 40, 37, 39];
        for (int i = 1; i < directionKeys.Length; i++)
        { keyboard.Observe([], null); keyboard.Observe([directionKeys[i]], null); }
        Check(keyboard.KeyboardSticks.Count == 8 && keyboard.Current?.Button == ControllerButtons.B, "all eight directions precede button configuration");
        keyboard.Observe([], null); keyboard.Observe(['P'], null);
        Check(keyboard.KeyboardButtons['P'] == ControllerButtons.B && !keyboard.Completed, "keyboard button capture follows sticks");
        while (keyboard.Active) keyboard.Skip();
        Check(keyboard.Completed && keyboard.CapturedCount == 9, "skip preserves only recorded steps until completion");
        var cancelled = new QuickMappingSession(true, VirtualControllerMode.Ns1Pro);
        cancelled.Observe([], null); cancelled.Observe(['P'], null); cancelled.Observe([27], null);
        Check(!cancelled.Active && !cancelled.Completed && cancelled.CapturedCount == 0, "Escape discards all partial wizard input");
        cancelled.Observe(['Q'], null);
        Check(cancelled.CapturedCount == 0, "cancelled wizard ignores late input");

        var controller = new QuickMappingSession(false, VirtualControllerMode.Ns1Pro);
        controller.Observe([], neutral);
        controller.Observe([], neutral with { Buttons = ControllerButtons.LeftStick });
        Check(controller.Index == 0 && controller.ControllerSticks.Count == 0, "stick click cannot map a physical stick");
        controller.Observe([], neutral); controller.Observe(['W'], neutral);
        Check(controller.Index == 0, "keyboard key cannot map a physical stick");
        controller.Observe([], neutral with { LeftX = 2400 });
        Check(controller.Index == 0, "ordinary stick drift cannot trigger wizard");
        controller.Observe([], neutral with { RightX = 4095 });
        Check(controller.ControllerSticks[StickSource.Left] == StickSource.Right && controller.Current?.Stick == StickSource.Right,
            "right source stick can map to left output");
        controller.Observe([], neutral with { RightX = 4095 });
        Check(controller.Index == 1, "stick must recenter between steps");
        controller.Observe([], neutral); controller.Observe([], neutral with { LeftY = 0 });
        Check(controller.ControllerSticks[StickSource.Right] == StickSource.Left && controller.Current?.Button == ControllerButtons.B,
            "left source stick can map to right output");
        controller.Observe([], neutral); controller.Observe([], neutral with { LeftX = 4095 });
        Check(controller.Index == 2, "stick movement cannot fill a button step");
        controller.Observe([], neutral); controller.Observe([], neutral with { Buttons = ControllerButtons.A });
        Check(controller.ControllerButtons[ControllerButtons.A] == ControllerButtons.B, "physical button maps after both sticks");
        controller.Cancel();
        Check(controller.CapturedCount == 0, "controller wizard cancellation also discards stick mappings");
        foreach (var mode in Enum.GetValues<VirtualControllerMode>())
        {
            var session = new QuickMappingSession(false, mode);
            var buttons = session.Steps.Where(s => s.Button != ControllerButtons.None).Select(s => MappingPreview.OutputButtons(s.Button, mode)).ToArray();
            Check(buttons.All(b => b != ControllerButtons.None) && buttons.Distinct().Count() == buttons.Length, $"{mode} wizard contains only supported unique output buttons");
        }

        var route = new OutputRouteOptions
        {
            KeyboardStickBindings = new(keyboard.KeyboardSticks), KeyboardOverrides = new(keyboard.KeyboardButtons),
            ControllerSticks = new() { Left = StickSource.Right, Right = StickSource.Left }
        };
        var options = new BridgeOptions().SaveRoute(VirtualControllerMode.Ns1Pro, route);
        var restored = JsonSerializer.Deserialize<BridgeOptions>(JsonSerializer.Serialize(options))!.Normalize();
        var saved = restored.Route(VirtualControllerMode.Ns1Pro);
        Check(saved.KeyboardStickBindings.Count == 8 && saved.ControllerSticks.Left == StickSource.Right, "wizard axes survive route serialization");
        Check(restored.Route(VirtualControllerMode.Xbox360).KeyboardStickBindings.Count == 0 &&
            restored.Route(VirtualControllerMode.Xbox360).ControllerSticks.Left == StickSource.Left, "axis mappings stay isolated per output route");
        var legacy = JsonSerializer.Deserialize<BridgeOptions>("{}")!.Normalize();
        Check(legacy.ControllerSticks == new StickMapping() && legacy.KeyboardStickBindings.Count == 0, "old settings preserve WASD and physical axes");
        var invalid = new OutputRouteOptions { KeyboardStickBindings = new() { [(StickDirection)99] = 'P', [StickDirection.LeftUp] = 27 },
            ControllerSticks = new() { Left = (StickSource)99 } }.Normalize();
        Check(invalid.KeyboardStickBindings.Count == 0 && invalid.ControllerSticks.Left == StickSource.Left, "invalid keys and physical stick sources normalize safely");

        var runtime = restored.SelectRoute(VirtualControllerMode.Ns1Pro);
        ControllerState output = neutral;
        var bridge = new ControllerInputBridge(s => output = s, () => runtime, _ => null);
        bridge.Select(BridgeInputKind.KeyboardMouse, null);
        void Send(params int[] keys)
        {
            var raw = KeyboardMouseMapper.Map(keys.Contains, 0, 0, .016, MouseEmulationMode.RightStick, DateTimeOffset.Now);
            bridge.KeyboardMouse(raw, keys.ToHashSet());
        }
        Send('T'); Check(output.LeftY == 4095 && output.RightY == 2048, "saved keyboard up reaches output without physical stick swap");
        Send('W'); Check(output.LeftY == 2048, "replaced WASD direction no longer drives stick");
        Send('T', 'G'); Check(output.LeftY == 2048, "opposing mapped direction keys cancel");
        Send('T', 'H');
        Check(Math.Abs(Math.Sqrt(Math.Pow(StickCoordinates.Unit(output.LeftX), 2) + Math.Pow(StickCoordinates.Unit(output.LeftY), 2)) - 1) < .001,
            "custom keyboard diagonals stay normalized");
        Send(38); Check(output.RightY == 4095 && output.Buttons == ControllerButtons.None, "right stick direction suppresses its old dpad function");
        Send(); Check(output.LeftX == 2048 && output.LeftY == 2048 && output.RightY == 2048, "releasing mapped directions centers both sticks");
        bridge.KeyboardMouse(neutral with { RightX = 3700 }, new HashSet<int>());
        Check(output.RightX == 3700, "mouse right stick remains available when direction keys are released");
        Send('P'); Check(output.Buttons == ControllerButtons.B, "wizard button and axis bindings share real keyboard bridge");
        bridge.Select(BridgeInputKind.Ns2Ble, null);
        bridge.Ble(neutral with { LeftX = 3000, LeftY = 1000, RightX = 4000, RightY = 500 }, null);
        Check(output.LeftX == 4000 && output.LeftY == 500 && output.RightX == 3000 && output.RightY == 1000,
            "saved physical stick swap reaches BLE bridge without cascading");
        Check(bridge.Comparison.Source.LeftX == 3000, "physical stick remapping preserves raw source preview");
        var pad = new GamepadDevice(9, "Stick mapping test", 1, 2, true, ControllerLayout.Switch2Pro);
        var input = new GamepadSnapshot(ControllerButtons.None, [1, 0, 0, -1, 0, 0], [], [], -1, true, null, null, []);
        var frame = new GamepadFrame([pad], new Dictionary<uint, GamepadSnapshot> { [pad.Id] = input }, "", 1);
        bridge.Select(BridgeInputKind.WindowsGamepad, pad); bridge.Windows(frame, 1);
        Check(output.LeftX == 2048 && output.LeftY == 4095 && output.RightX == 4095 && output.RightY == 2048,
            "saved stick swap reaches Windows gamepad path with SDL Y conversion");
        bridge.Select(BridgeInputKind.Ns2Ble, null); bridge.SelectNs2Usb(pad); bridge.Windows(frame, 1);
        Check(output.LeftY == 4095 && output.RightX == 4095, "native NS2 USB uses same stick routing");
        bridge.SelectNs2Usb(null);
        runtime = runtime with { ControllerSticks = new() { Left = StickSource.Right, Right = StickSource.Right } };
        bridge.Ble(neutral with { RightX = 3500 }, null);
        Check(output.LeftX == 3500 && output.RightX == 3500, "one source stick can drive both output sticks");
        runtime = new BridgeOptions(); bridge.Select(BridgeInputKind.KeyboardMouse, null); Send('W');
        Check(output.LeftY == 4095, "reset restores default keyboard axes");
        return count;
    }
}
