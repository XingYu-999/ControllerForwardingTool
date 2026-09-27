using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class KeyboardMouseChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var at = DateTimeOffset.Now;
        ControllerState Map(int[] keys, double dx = 0, double dy = 0, MouseEmulationMode mode = MouseEmulationMode.RightStick) =>
            KeyboardMouseMapper.Map(keys.Contains, dx, dy, .01, mode, at);
        var neutral = Map([]);
        Check(neutral == ControllerState.Neutral(at), "idle keyboard/mouse is neutral");
        var forward = Map(['W']);
        Check(forward.LeftY == 4095 && forward.LeftX == 2048, "W pushes left stick up");
        Check(Map(['W', 'S', 'A', 'D']) == neutral, "opposing keyboard directions cancel");
        var diagonal = Map(['W', 'D']);
        Check(Math.Abs(Math.Sqrt(Math.Pow(StickCoordinates.Unit(diagonal.LeftX), 2) +
            Math.Pow(StickCoordinates.Unit(diagonal.LeftY), 2)) - 1) < .001, "diagonal keyboard movement stays in stick circle");
        Check(Map(['J', 'K', 'U', 'I']).Buttons == (ControllerButtons.A | ControllerButtons.B | ControllerButtons.X | ControllerButtons.Y), "all four face buttons");
        Check(Map([0x20]).Buttons == ControllerButtons.B, "space maps to physical south button");
        var triggers = Map([1, 2]);
        Check(triggers.LeftTriggerValue == 255 && triggers.RightTriggerValue == 255, "mouse buttons drive both triggers");
        Check(Map([]).LeftTriggerValue == 0 && Map([]).Buttons == ControllerButtons.None, "release clears buttons and triggers");
        var stick = Map([], 12, -12);
        Check(stick.RightX == 4095 && stick.RightY == 4095 && stick.GyroX == 0 && stick.GyroZ == 0, "mouse right/up becomes right stick only");
        Check(Map([]).RightX == 2048 && Map([]).RightY == 2048, "stopped mouse centers right stick");
        var gyro = Map([], 12, -12, MouseEmulationMode.Gyroscope);
        Check(gyro.RightX == 2048 && gyro.RightY == 2048 && gyro.GyroX > 0 && gyro.GyroZ < 0 && gyro.AccelZ == 4096, "mouse gyro has pitch/yaw and gravity, no stick input");
        var stationary = Map([], 0, 0, MouseEmulationMode.Gyroscope);
        Check(stationary.GyroX == 0 && stationary.GyroZ == 0 && stationary.AccelZ == 4096, "stationary gyro has zero velocity and gravity");
        Check(Map([], double.NaN, double.PositiveInfinity).RightX == 2048, "invalid mouse samples are neutral");
        var slow = KeyboardMouseMapper.Map(_ => false, 6, -6, .005, MouseEmulationMode.Gyroscope, at);
        Check(slow.GyroX == gyro.GyroX && slow.GyroZ == gyro.GyroZ, "gyro uses velocity independent of sampling period");

        ControllerState latest = neutral;
        var bridge = new ControllerInputBridge(s => latest = s, () => new BridgeOptions(), _ => null);
        bridge.Select(BridgeInputKind.KeyboardMouse, null);
        bridge.KeyboardMouse(forward);
        Check(latest.LeftY == 4095, "keyboard reaches bridge output");
        bridge.Windows(new([], new Dictionary<uint, GamepadSnapshot>(), "", 0), 0);
        bridge.Ble(neutral, null); bridge.DisconnectBle(); bridge.SelectNs2Usb(null);
        Check(latest.LeftY == 4095, "other input streams cannot overwrite keyboard output");
        bridge.Select(BridgeInputKind.WindowsGamepad, null);
        bridge.KeyboardMouse(forward);
        Check(latest.Buttons == ControllerButtons.None && latest.LeftY == 2048, "late keyboard frames ignored after input switch");

        var options = new BridgeOptions().Normalize();
        foreach (var outputMode in Enum.GetValues<VirtualControllerMode>())
        {
            Check(options.Route(outputMode).InputKind == BridgeInputKind.KeyboardMouse &&
                options.Route(outputMode).MouseMode == MouseEmulationMode.RightStick, $"{outputMode} defaults to keyboard/right stick");
        }
        options = options.SaveRoute(VirtualControllerMode.Ns1Pro, new() { MouseMode = MouseEmulationMode.Gyroscope });
        var restored = JsonSerializer.Deserialize<BridgeOptions>(JsonSerializer.Serialize(options))!.Normalize();
        Check(restored.SelectRoute(VirtualControllerMode.Ns1Pro).MouseMode == MouseEmulationMode.Gyroscope &&
            restored.SelectRoute(VirtualControllerMode.Xbox360).MouseMode == MouseEmulationMode.RightStick, "mouse mode persists independently per route");
        Check(new OutputRouteOptions { MouseMode = (MouseEmulationMode)999 }.Normalize().MouseMode == MouseEmulationMode.RightStick, "invalid saved mouse mode falls back");
        Check(JsonSerializer.Deserialize<BridgeOptions>("{\"InputKind\":1}")!.InputKind == BridgeInputKind.WindowsGamepad,
            "legacy physical input enum values remain compatible");

        var platform = new FakePlatform();
        using var monitor = new KeyboardMouseMonitor(s => latest = s, platform, false);
        monitor.Configure(true, MouseEmulationMode.RightStick);
        platform.Keys.Add('W'); monitor.Tick();
        Check(!monitor.IsCaptured && latest.LeftY == 2048, "keyboard remains neutral until F8 capture");
        platform.Keys.Add(0x77); monitor.Tick();
        Check(monitor.IsCaptured && latest.LeftY == 4095, "F8 captures keyboard for running route");
        monitor.Tick(); Check(monitor.IsCaptured, "holding F8 does not toggle repeatedly");
        platform.Keys.Remove(0x77); platform.Cursor = (112, 90); monitor.Tick();
        Check(latest.RightX > 2048 && latest.RightY > 2048 && platform.Cursor == (100, 100), "captured mouse maps displacement and recenters cursor");
        Check(monitor.Latest == latest, "draft preview can read the captured mouse frame before remapping");
        platform.Keys.Add(0x1B); monitor.Tick();
        Check(!monitor.IsCaptured && latest == ControllerState.Neutral(latest.ReceivedAt), "escape releases and clears held input");
        Check(monitor.Latest == latest, "releasing capture also clears the preview snapshot");
        platform.Keys.Remove(0x1B); platform.Keys.Add(0x77); monitor.Tick();
        platform.Foreground = (IntPtr)2; platform.Keys.Remove(0x77); monitor.Tick();
        Check(!monitor.IsCaptured && latest.LeftY == 2048, "foreground change releases held input");
        platform.Keys.Add(0x77); monitor.Tick(); monitor.Configure(false, MouseEmulationMode.RightStick);
        Check(!monitor.IsCaptured && latest.LeftY == 2048, "stopping route immediately neutralizes capture");
        monitor.Configure(true, MouseEmulationMode.RightStick); monitor.Tick();
        Check(!monitor.IsCaptured, "held F8 cannot capture when route re-enables");
        platform.Keys.Remove(0x77); monitor.Tick(); platform.Keys.Add(0x77); monitor.Tick();
        monitor.Configure(true, MouseEmulationMode.Gyroscope);
        Check(!monitor.IsCaptured && latest.GyroX == 0 && latest.RightX == 2048, "changing mouse mode releases stale input");
        monitor.Dispose(); monitor.Tick();
        Check(!monitor.IsCaptured && latest.LeftY == 2048, "dispose leaves neutral input");

        // The tester start button must drive the actual NS1 device, not a local preview.
        var ns1 = new UsbIpNs1Device();
        var testOptions = new BridgeOptions { KeyboardOverrides = new() { ['P'] = ControllerButtons.X } };
        var testBridge = new ControllerInputBridge(ns1.Publish, () => testOptions, _ => null);
        testBridge.Select(BridgeInputKind.KeyboardMouse, null);
        var testPlatform = new FakePlatform();
        KeyboardMouseMonitor? testMonitor = null;
        using (testMonitor = new KeyboardMouseMonitor(s => testBridge.KeyboardMouse(s, testMonitor?.PressedKeys), testPlatform, false))
        {
            Check(!testMonitor.TryStartCapture(), "tester button cannot forward before output is enabled");
            testMonitor.Configure(true, MouseEmulationMode.RightStick);
            testPlatform.Keys.UnionWith([1, 0x20, 0x0D]);
            Check(testMonitor.TryStartCapture(), "tester button captures without F8");
            testMonitor.Tick();
            Check(ns1.ReadInput(64).AsSpan(3, 3).ToArray().All(x => x == 0) && testMonitor.PressedKeys.Count == 0,
                "activation click, space and enter stay out of actual NS1 output");
            testPlatform.Keys.Clear(); testMonitor.Tick();
            testPlatform.Keys.UnionWith(['W', 'J', 'P', 1]); testMonitor.Tick();
            var report = ns1.ReadInput(64);
            Check(report[3] == (0x04 | 0x02 | 0x80), "tester sends default, saved custom binding and mouse trigger to NS1");
            Check(((report[7] >> 4) | (report[8] << 4)) == 4095, "tester sends WASD stick movement to NS1");
            testPlatform.Cursor = (112, 100); testMonitor.Tick();
            report = ns1.ReadInput(64);
            Check((report[9] | ((report[10] & 15) << 8)) > 2048, "tester sends mouse motion to actual NS1 report");
            testPlatform.Keys.Clear(); testMonitor.Tick();
            Check(ns1.ReadInput(64).AsSpan(3, 3).ToArray().All(x => x == 0), "releasing tester keys clears NS1 buttons");
            testPlatform.Keys.Add('J'); testMonitor.Tick();
            testMonitor.StopCapture();
            Check(!testMonitor.IsCaptured && ns1.ReadInput(64)[3] == 0 && testMonitor.PressedKeys.Count == 0,
                "leaving tester immediately sends neutral NS1 output");
            Check(testMonitor.TryStartCapture(), "tester can restart after release");
            testPlatform.Keys.Clear(); testMonitor.Tick(); testPlatform.Keys.Add('J'); testMonitor.Tick();
            testPlatform.Foreground = (IntPtr)2; testMonitor.Tick();
            Check(!testMonitor.IsCaptured && ns1.ReadInput(64)[3] == 0, "tester focus loss clears real NS1 output");
            testPlatform.Keys.Clear(); testPlatform.Keys.Add(0x1B);
            Check(!testMonitor.TryStartCapture(), "tester cannot start while escape is held");
            testPlatform.Keys.Clear(); testPlatform.Foreground = IntPtr.Zero;
            Check(!testMonitor.TryStartCapture(), "tester cannot capture without foreground window");
            testPlatform.Foreground = (IntPtr)1; testPlatform.CursorWritable = false;
            Check(!testMonitor.TryStartCapture() && !testMonitor.IsCaptured, "cursor capture failure leaves tester paused");
            testPlatform.CursorWritable = true; testMonitor.TryStartCapture();
            testPlatform.Keys.Add('J'); testMonitor.Tick();
            testMonitor.Configure(false, MouseEmulationMode.RightStick);
            Check(!testMonitor.IsCaptured && ns1.ReadInput(64)[3] == 0, "stopping output neutralizes button-started test");
            testMonitor.Configure(true, MouseEmulationMode.RightStick); testPlatform.Keys.Clear();
            testMonitor.TryStartCapture(); testPlatform.Keys.Add('J'); testMonitor.Tick();
            testPlatform.Keys.Add(0x77); testMonitor.Tick();
            Check(!testMonitor.IsCaptured && ns1.ReadInput(64)[3] == 0, "F8 exits button-started test");
            testMonitor.Tick(); Check(!testMonitor.IsCaptured, "held F8 cannot restart stopped tester");
            testPlatform.Keys.Clear(); testMonitor.Tick(); testMonitor.TryStartCapture();
            testPlatform.Keys.Add('J'); testMonitor.Tick();
            testMonitor.Dispose();
            Check(!testMonitor.TryStartCapture() && ns1.ReadInput(64)[3] == 0, "disposed tester cannot restart or leave a held NS1 button");
        }
        return count;
    }
    private sealed class FakePlatform : IKeyboardMousePlatform
    {
        public HashSet<int> Keys { get; } = [];
        public (int X, int Y) Cursor { get; set; } = (100, 100);
        public IntPtr Foreground { get; set; } = (IntPtr)1;
        public bool CursorWritable { get; set; } = true;
        public bool Down(int key) => Keys.Contains(key);
        public bool TryGetCursor(out (int X, int Y) point) { point = Cursor; return true; }
        public bool TryGetCaptureAnchor(out (int X, int Y) point) { point = (100, 100); return true; }
        public bool SetCursor(int x, int y) { if (!CursorWritable) return false; Cursor = (x, y); return true; }
    }
}
