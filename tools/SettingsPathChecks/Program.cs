using System.Text.Json;
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
    Assert(BridgeOptions.Load(newPath, oldPath).RumbleGain == 1 && !File.Exists(newPath), "First launch defaults");

    var settings = new BridgeOptions { RumbleGain = 2.5, ApiPort = 4242,
        StickProfiles = new() { ["controller"] = new([2048, 2048, 2048, 2048], [0, 0, 0, 0], [4095, 4095, 4095, 4095]) } };
    string original = JsonSerializer.Serialize(settings);
    File.WriteAllText(oldPath, original);
    BridgeOptions migrated = BridgeOptions.Load(newPath, oldPath);
    Assert(migrated.RumbleGain == 2.5 && migrated.ApiPort == 4242 && migrated.StickProfiles["controller"].Valid,
        "Calibration and settings retained");
    Assert(File.ReadAllText(newPath) == original && File.ReadAllText(oldPath) == original, "Migration preserves old file");

    File.WriteAllText(oldPath, JsonSerializer.Serialize(settings with { RumbleGain = .5 }));
    Assert(BridgeOptions.Load(newPath, oldPath).RumbleGain == 2.5 && File.ReadAllText(newPath) == original,
        "Existing new settings take precedence");
    File.Delete(newPath);
    File.WriteAllText(oldPath, "{broken-json");
    Assert(BridgeOptions.Load(newPath, oldPath).RumbleGain == 1 && !File.Exists(newPath), "Invalid legacy settings not migrated");

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
