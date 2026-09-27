using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class MappingCaptureChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var now = DateTimeOffset.Now;
        var capture = new MappingCapture();
        capture.Begin([1], now);
        Check(capture.Observe([1], now.AddMilliseconds(10)) is null, "activation click cannot bind itself");
        Check(capture.Observe([], now.AddMilliseconds(20)) is null, "release arms binding capture");
        Check(capture.Observe(['P'], now.AddMilliseconds(30)) == 'P' && !capture.Active, "arbitrary key captures once");
        Check(capture.Observe(['Q'], now.AddMilliseconds(40)) is null, "completed capture cannot overwrite binding");
        capture.Begin([], now);
        Check(capture.Observe(['J', 'K'], now) is null && capture.Active, "simultaneous keys require release");
        Check(capture.Observe(['J'], now) is null, "partial release cannot select chord member");
        capture.Observe([], now);
        Check(capture.Observe([5], now) == 5, "mouse side button can be captured");
        capture.Begin([], now); capture.Cancel();
        Check(capture.Observe(['P'], now) is null, "cancel ignores later input");
        capture.Begin([], now);
        Check(capture.Observe(['P'], now.AddSeconds(15)) is null && capture.TimedOut, "capture expires without binding");
        capture.Begin([], now);
        Check(capture.Observe([(int)ControllerButtons.Touchpad], now) == (int)ControllerButtons.Touchpad, "physical controller capture");
        var mapping = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.B] = ControllerButtons.X } };
        ControllerState Raw(params int[] keys) => KeyboardMouseMapper.Map(keys.Contains, 0, 0, .033, MouseEmulationMode.RightStick, now);
        var overrides = new Dictionary<int, ControllerButtons> { ['J'] = ControllerButtons.Plus, ['P'] = ControllerButtons.A, ['W'] = ControllerButtons.ZR, [5] = ControllerButtons.Home };
        var j = KeyboardMapping.Apply(Raw('J'), new HashSet<int> { 'J' }, mapping, overrides);
        Check(j.Buttons == ControllerButtons.Plus, "direct binding replaces default without double mapping");
        Check(KeyboardMapping.Apply(Raw(32), new HashSet<int> { 32 }, mapping, overrides).Buttons == ControllerButtons.X, "unmodified alias retains logical mapping");
        Check(KeyboardMapping.Apply(Raw('P'), new HashSet<int> { 'P' }, mapping, overrides).Buttons == ControllerButtons.A, "new key drives output");
        var w = KeyboardMapping.Apply(Raw('W'), new HashSet<int> { 'W' }, mapping, overrides);
        Check(w.RightTriggerValue == 255 && w.LeftY == 2048, "bound WASD stops axis and drives assigned trigger");
        Check(KeyboardMapping.Apply(Raw(5), new HashSet<int> { 5 }, mapping, overrides).Buttons == ControllerButtons.Home, "mouse side button drives output");
        Check(KeyboardMapping.Apply(Raw(), new HashSet<int>(), mapping, overrides).Buttons == ControllerButtons.None, "release clears direct mapping");
        Check(KeyboardMapping.Apply(Raw('J'), new HashSet<int> { 'J' }, mapping, new Dictionary<int, ControllerButtons>()).Buttons == ControllerButtons.X, "removing binding restores default");
        var options = new BridgeOptions().SaveRoute(VirtualControllerMode.Ns1Pro, new() { KeyboardOverrides = overrides });
        var restored = JsonSerializer.Deserialize<BridgeOptions>(JsonSerializer.Serialize(options))!.Normalize();
        Check(restored.Route(VirtualControllerMode.Ns1Pro).KeyboardOverrides['P'] == ControllerButtons.A && restored.Route(VirtualControllerMode.Xbox360).KeyboardOverrides.Count == 0, "direct mappings persist per output route");
        var invalid = new OutputRouteOptions { KeyboardOverrides = new() { [27] = ControllerButtons.B, [119] = ControllerButtons.B, ['P'] = ControllerButtons.A } }.Normalize();
        Check(invalid.KeyboardOverrides.Count == 1 && invalid.KeyboardOverrides.ContainsKey('P'), "reserved hotkeys cannot be rebound");
        ControllerState output = Raw();
        var runtime = restored.SelectRoute(VirtualControllerMode.Ns1Pro);
        var bridge = new ControllerInputBridge(s => output = s, () => runtime, _ => null);
        bridge.Select(BridgeInputKind.KeyboardMouse, null);
        bridge.KeyboardMouse(Raw('P'), new HashSet<int> { 'P' });
        Check(output.Buttons == ControllerButtons.A && bridge.Comparison.Source.Buttons == ControllerButtons.None,
            "saved new key reaches bridge while original input remains unchanged");
        bridge.KeyboardMouse(Raw(), new HashSet<int>());
        Check(output.Buttons == ControllerButtons.None, "new binding releases through real bridge path");
        runtime = runtime with { KeyboardOverrides = [] };
        bridge.KeyboardMouse(Raw('P'), new HashSet<int> { 'P' });
        Check(output.Buttons == ControllerButtons.None, "removed binding is inactive on next bridge frame");
        return count;
    }
}
