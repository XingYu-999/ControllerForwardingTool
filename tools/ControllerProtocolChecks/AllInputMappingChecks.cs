using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.VirtualDevice;

internal static class AllInputMappingChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var mapping = new Ns2ButtonMapping { Bindings = new()
        {
            [ControllerButtons.B] = ControllerButtons.Y,
            [ControllerButtons.Y] = ControllerButtons.B,
            [ControllerButtons.Touchpad] = ControllerButtons.Plus
        } };
        var config = new BridgeOptions { Ns2Buttons = mapping };
        ControllerState latest = ControllerState.Neutral(DateTimeOffset.Now);
        var bridge = new ControllerInputBridge(s => latest = s, () => config, _ => null);
        var source = ControllerState.Neutral(DateTimeOffset.Now) with { Buttons = ControllerButtons.B, LeftX = 2700, GyroX = 123 };
        bridge.Select(BridgeInputKind.Ns2Ble, null);
        bridge.Ble(source, null);
        Check(latest.Buttons == ControllerButtons.Y && bridge.Comparison.Source == source, "BLE maps once and preserves raw comparison");
        bridge.Select(BridgeInputKind.KeyboardMouse, null);
        bridge.KeyboardMouse(source);
        Check(latest.Buttons == ControllerButtons.Y && latest.LeftX == 2700 && latest.GyroX == 123, "keyboard maps buttons without changing axes/motion");
        Check(bridge.Comparison.Source.Buttons == ControllerButtons.B, "keyboard source diagram stays unmapped");

        foreach (var layout in Enum.GetValues<ControllerLayout>())
        {
            var device = new GamepadDevice(7, "Mapping test", 1, 2, true, layout);
            var buttons = ControllerLayouts.RemapFaceButtons(ControllerButtons.B, true, layout.IsNintendo());
            var snapshot = new GamepadSnapshot(buttons, [0, 0, 0, 0, 0, 0], [], [], -1, true, null, null, []);
            var frame = new GamepadFrame([device], new Dictionary<uint, GamepadSnapshot> { [device.Id] = snapshot }, "", 1);
            bridge.Select(BridgeInputKind.WindowsGamepad, device);
            bridge.Windows(frame, 1);
            Check(latest.Buttons == ControllerButtons.Y && bridge.Comparison.Source.Buttons == ControllerButtons.B,
                $"{layout} applies mapping after canonical face conversion");
            foreach (var mode in Enum.GetValues<VirtualControllerMode>())
                Check(MappingPreview.OutputButtons(latest.Buttons, mode) ==
                    ControllerLayouts.RemapFaceButtons(ControllerButtons.Y, true, MappingPreview.Layout(mode).IsNintendo()),
                    $"{layout} mapped button position for {mode}");
            if (layout == ControllerLayout.Switch2Pro)
            {
                bridge.Select(BridgeInputKind.Ns2Ble, null); bridge.SelectNs2Usb(device); bridge.Windows(frame, 1);
                Check(latest.Buttons == ControllerButtons.Y, "native NS2 USB is mapped once");
            }
        }

        bridge.Select(BridgeInputKind.KeyboardMouse, null);
        config = config with { Ns2Buttons = new() { Bindings = new() { [ControllerButtons.B] = ControllerButtons.None } } };
        bridge.KeyboardMouse(source);
        Check(latest.Buttons == ControllerButtons.None, "new mapping applies to next keyboard frame");
        bridge.KeyboardMouse(source with { Buttons = ControllerButtons.None });
        Check(latest.Buttons == ControllerButtons.None, "keyboard release clears mapped buttons");
        var touchpad = mapping.Apply(source with { Buttons = ControllerButtons.Touchpad });
        Check(touchpad.Buttons == ControllerButtons.Plus, "PlayStation touchpad is remappable");
        var merged = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.A] = ControllerButtons.X, [ControllerButtons.B] = ControllerButtons.X } };
        Check(merged.Apply(source with { Buttons = ControllerButtons.A | ControllerButtons.B }).Buttons == ControllerButtons.X &&
            merged.Apply(source).Buttons == ControllerButtons.X &&
            merged.Apply(source with { Buttons = ControllerButtons.None }).Buttons == ControllerButtons.None, "many-to-one mapping releases only after all sources");

        var partial = source with { Buttons = ControllerButtons.ZR, AnalogLeftTrigger = 64, AnalogRightTrigger = 191 };
        var swapped = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.ZL] = ControllerButtons.ZR, [ControllerButtons.ZR] = ControllerButtons.ZL } }.Apply(partial);
        Check(swapped.LeftTriggerValue == 191 && swapped.RightTriggerValue == 64 && swapped.Buttons == ControllerButtons.ZL,
            "analog trigger swap preserves both travel values including below threshold");
        var disabled = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.ZL] = ControllerButtons.None, [ControllerButtons.ZR] = ControllerButtons.None } }.Apply(partial);
        Check(disabled.LeftTriggerValue == 0 && disabled.RightTriggerValue == 0 && disabled.Buttons == ControllerButtons.None, "disabled triggers zero analog and digital output");
        var combined = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.ZL] = ControllerButtons.ZR } }.Apply(partial);
        Check(combined.LeftTriggerValue == 0 && combined.RightTriggerValue == 191, "merged analog triggers use greatest travel");
        var buttonTrigger = new Ns2ButtonMapping { Bindings = new() { [ControllerButtons.B] = ControllerButtons.ZL } };
        Check(buttonTrigger.Apply(source).LeftTriggerValue == 255 &&
            buttonTrigger.Apply(source with { AnalogLeftTrigger = 50, AnalogRightTrigger = 0 }).LeftTriggerValue == 255,
            "digital button to trigger works for both digital and analog sources");
        Check(new Ns2ButtonMapping().Apply(partial).LeftTriggerValue == 64, "default mapping preserves partial analog travel");

        var row = new ButtonMappingRow(ControllerButtons.B, ControllerButtons.Y, VirtualControllerMode.Ns1Pro);
        row.SourceLayout = ControllerLayout.Xbox;
        Check(row.Label == "A", "Xbox source label reflects south button");
        row.KeyboardSource = true;
        Check(row.Label == "J / 空格 → A", "keyboard source shows actual key binding");
        row.KeyboardSource = false; row.SourceLayout = ControllerLayout.DualSense;
        Check(row.Label == "×", "PlayStation source label reflects cross");
        row.SourceLayout = ControllerLayout.SwitchPro;
        Check(row.Label == "B", "Nintendo source retains native label");
        var mouse = new ButtonMappingRow(ControllerButtons.ZR, ControllerButtons.B)
            { SourceLayout = ControllerLayout.Xbox, KeyboardSource = true };
        Check(mouse.Label == "鼠标左键 → RT", "mouse source label identifies left click");
        return count;
    }
}
