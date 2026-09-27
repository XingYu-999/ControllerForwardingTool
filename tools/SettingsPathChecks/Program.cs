using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

// Exercise migration with isolated paths; never read or write real user settings.
string root = Path.Combine(Path.GetTempPath(), "forwarding-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string oldPath = Path.Combine(root, "legacy.json");
    string newPath = Path.Combine(root, "new", "bridge-settings.json");
    Assert(BridgeOptions.SettingsPath == Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ControllerForwardingTool", "bridge-settings.json"), "Known folder path");
    var defaults = BridgeOptions.Load(newPath, oldPath);
    Assert(defaults.RumbleGain == .7 && !File.Exists(newPath), "First launch defaults without writing settings");
    Assert(defaults.Mode == VirtualControllerMode.Ns1Pro && defaults.InputKind == BridgeInputKind.Ns2Ble &&
        defaults.KeyboardMouseSupplementEnabled && defaults.MouseMode == MouseEmulationMode.Gyroscope,
        "First launch uses the captured NS2 to NS1 route");
    Assert(defaults.Motion.AngularThreshold == 1.6 && defaults.Motion.AccelerationThreshold == .05 &&
        defaults.Motion.AutoCalibrate && !defaults.Motion.SmoothSmallMotion && defaults.Motion.StationarySeconds == 3,
        "Captured motion preferences retained");
    Assert(defaults.BleDevices.Remembered.Length == 0 && defaults.Ns2UsbAddresses.Count == 0 && defaults.StickProfiles.Count == 0,
        "Portable defaults contain no device identities or calibration");
    var neutral = ControllerState.Neutral(DateTimeOffset.Now);
    Assert(defaults.Ns2Buttons.Apply(neutral with { Buttons = ControllerButtons.A }).Buttons == ControllerButtons.B &&
        defaults.Ns2Buttons.Apply(neutral with { Buttons = ControllerButtons.B }).Buttons == ControllerButtons.A &&
        defaults.Ns2Buttons.Apply(neutral with { Buttons = ControllerButtons.Plus }).Buttons == ControllerButtons.None,
        "Captured NS1 face-button swap and disabled Plus apply to source frames");
    ControllerState Keyboard(params int[] keys) => KeyboardMapping.Apply(neutral, keys.ToHashSet(),
        defaults.Ns2Buttons, defaults.KeyboardOverrides, defaults.KeyboardStickBindings);
    Assert(Keyboard('K').Buttons == ControllerButtons.B && Keyboard('L').Buttons == ControllerButtons.A &&
        Keyboard(32).Buttons == ControllerButtons.Plus && Keyboard(1).Buttons == ControllerButtons.A &&
        Keyboard(2).Buttons == ControllerButtons.LeftStick,
        "Captured keyboard and mouse buttons target NS1 directly");
    Assert(Keyboard('X').LeftY == 0 && Keyboard('W').LeftY == 4095 && Keyboard('S').LeftY == 2048,
        "Captured W/X/A/D stick layout replaces W/S/A/D");
    foreach (var mode in Enum.GetValues<VirtualControllerMode>())
    {
        var route = defaults.SelectRoute(mode);
        Assert(route.RumbleGain == .7, $"{mode} starts at 0.7x rumble");
        bool nativeBackButtons = mode is VirtualControllerMode.Ns2Pro or VirtualControllerMode.DualSenseEdge;
        foreach (var (source, target) in new[]
        {
            (ControllerButtons.GL, nativeBackButtons ? ControllerButtons.GL : ControllerButtons.LeftStick),
            (ControllerButtons.GR, nativeBackButtons ? ControllerButtons.GR : ControllerButtons.RightStick)
        })
        {
            var mapped = route.Ns2Buttons.Apply(neutral with { Buttons = source });
            Assert(MappingPreview.OutputButtons(mapped.Buttons, mode) == target &&
                route.Ns2Buttons.Apply(neutral).Buttons == ControllerButtons.None,
                $"{mode} default {source} press and release reach {target}");
        }
    }
    var incomplete = defaults with { OutputRoutes = new() { [VirtualControllerMode.Xbox360] = new() { RumbleGain = 2 } } };
    Assert(incomplete.SelectRoute(VirtualControllerMode.Ns1Pro).KeyboardMouseSupplementEnabled &&
        incomplete.SelectRoute(VirtualControllerMode.Ns2Pro).Ns2Buttons.Target(ControllerButtons.GL) == ControllerButtons.GL,
        "Missing routes use their own presets");
    Assert((new BridgeOptions { RumbleGain = double.NaN }).Normalize().RumbleGain == .7 &&
        (new OutputRouteOptions { RumbleGain = double.PositiveInfinity }).Normalize().RumbleGain == .7,
        "Non-finite rumble gains recover to 0.7x");
    defaults.OutputRoutes[VirtualControllerMode.Xbox360].Ns2Buttons.Bindings[ControllerButtons.GL] = ControllerButtons.A;
    Assert(defaults.Route(VirtualControllerMode.DualSense).Ns2Buttons.Target(ControllerButtons.GL) == ControllerButtons.LeftStick &&
        BridgeOptions.CreateDefault().Route(VirtualControllerMode.Xbox360).Ns2Buttons.Target(ControllerButtons.GL) == ControllerButtons.LeftStick,
        "Default mappings are independent between routes and settings instances");
    defaults.Save(newPath);
    var roundTrip = BridgeOptions.Load(newPath, oldPath);
    Assert(roundTrip.Ns2Buttons.Apply(neutral with { Buttons = ControllerButtons.B }).Buttons == ControllerButtons.A &&
        roundTrip.Route(VirtualControllerMode.Xbox360).Ns2Buttons.Target(ControllerButtons.GL) == ControllerButtons.A &&
        roundTrip.KeyboardOverrides[32] == ControllerButtons.Plus && roundTrip.KeyboardStickBindings[StickDirection.LeftDown] == 'X',
        "Saved mappings and captured preset survive a settings round trip");
    File.Delete(newPath);

    var settings = new BridgeOptions { RumbleGain = 2.5, ApiPort = 4242,
        Ns2UsbAddresses = new() { ["physical-serial"] = 0x98E255C21688 },
        StickProfiles = new() { ["controller"] = new([2048, 2048, 2048, 2048], [0, 0, 0, 0], [4095, 4095, 4095, 4095]) } };
    string original = JsonSerializer.Serialize(settings);
    File.WriteAllText(oldPath, original);
    BridgeOptions migrated = BridgeOptions.Load(newPath, oldPath);
    Assert(migrated.Ns2UsbAddresses.GetValueOrDefault("physical-serial") == 0x98E255C21688,
        "USB serial and Bluetooth identity survive settings round trip");
    Assert((settings with { Ns2UsbAddresses = null! }).Normalize().Ns2UsbAddresses.Count == 0,
        "Missing USB identities normalize safely");
    Assert(migrated.RumbleGain == 2.5 && migrated.ApiPort == 4242 && migrated.StickProfiles["controller"].Valid,
        "Calibration and settings retained");
    Assert(Enum.GetValues<VirtualControllerMode>().All(mode => migrated.Route(mode).RumbleGain == 2.5) &&
        migrated.Route(VirtualControllerMode.Ns1Pro).Ns2Buttons.Target(ControllerButtons.Plus) == ControllerButtons.Plus &&
        !migrated.Route(VirtualControllerMode.Ns1Pro).KeyboardMouseSupplementEnabled,
        "Legacy settings seed all routes without overwriting saved preferences with the new preset");
    Assert(File.ReadAllText(newPath) == original && File.ReadAllText(oldPath) == original, "Migration preserves old file");

    File.WriteAllText(oldPath, JsonSerializer.Serialize(settings with { RumbleGain = .5 }));
    Assert(BridgeOptions.Load(newPath, oldPath).RumbleGain == 2.5 && File.ReadAllText(newPath) == original,
        "Existing new settings take precedence");
    File.Delete(newPath);
    File.WriteAllText(oldPath, "{broken-json");
    Assert(BridgeOptions.Load(newPath, oldPath).RumbleGain == .7 && !File.Exists(newPath), "Invalid legacy settings not migrated");
    File.WriteAllText(newPath, "null");
    Assert(BridgeOptions.Load(newPath, oldPath).Mode == VirtualControllerMode.Ns1Pro,
        "Null settings recover to the captured NS1 preset");
    File.Delete(newPath);

    File.WriteAllText(oldPath, original);
    string blocker = Path.Combine(root, "not-a-directory");
    File.WriteAllText(blocker, "block directory creation");
    Assert(BridgeOptions.Load(Path.Combine(blocker, "settings.json"), oldPath).RumbleGain == 2.5,
        "Migration failure keeps usable legacy settings");
    Assert(File.ReadAllText(oldPath) == original, "Failure preserves legacy backup");
    Console.WriteLine("All settings path and migration checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}
