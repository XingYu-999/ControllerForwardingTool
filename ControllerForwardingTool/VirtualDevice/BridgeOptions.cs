using System.Text.Json;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Bluetooth;

namespace ControllerForwardingTool.VirtualDevice;

public sealed record BridgeOptions
{
    public VirtualControllerMode Mode { get; init; } = VirtualControllerMode.Ns2Pro;
    public BridgeInputKind InputKind { get; init; }
    // Read older single-device settings; normalization migrates and clears this field.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BleDeviceIdentity? LastBleDevice { get; init; }
    public BleDeviceHistory BleDevices { get; init; } = new();
    // Opt in to changing the host registration stored inside the controller.
    public bool RegisterHostOnSync { get; init; }
    // USB serial -> controller Bluetooth address, learned only from a verified registration exchange.
    public Dictionary<string, ulong> Ns2UsbAddresses { get; init; } = [];
    public Ns2ButtonMapping Ns2Buttons { get; init; } = new();
    // Zero follows source frames, -1 uses the target USB reference rate; positive values are fixed rates.
    public int PushHz { get; init; }
    public int ApiPort { get; init; }
    public int UsbPort { get; init; }
    public double RumbleGain { get; init; } = 1;
    public bool AudioGuard { get; init; } = true;
    public bool AutoStartOutput { get; init; }
    public bool LaunchAtLogin { get; init; }
    public bool StartInTray { get; init; }
    public bool MinimizeToTray { get; init; }
    public bool CloseToTray { get; init; } = true;
    public double GyroPitch { get; init; } = 1;
    public double GyroYaw { get; init; } = 1;
    public double GyroRoll { get; init; } = 1;
    public bool InvertPitch { get; init; }
    public bool InvertYaw { get; init; }
    public bool InvertRoll { get; init; }
    public double StickDeadzone { get; init; }
    public bool RadialDeadzone { get; init; } = true;
    public bool UseStickCalibration { get; init; } = true;
    public Dictionary<string, StickProfile> StickProfiles { get; init; } = [];
    public GyroOptions Motion { get; init; } = new();
    // Flat fields remain the runtime snapshot and support importing pre-profile settings.
    public Dictionary<VirtualControllerMode, OutputRouteOptions> OutputRoutes { get; init; } = [];
    public BridgeOptions Normalize()
    {
        var next = NormalizeValues();
        var routes = (OutputRoutes ?? []).Where(pair => Enum.IsDefined(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Normalize());
        // Before separate routes existed all outputs used the same preferences. Seed each
        // route with its own copy so upgrading preserves that behavior without future coupling.
        if (OutputRoutes is null || OutputRoutes.Count == 0)
            foreach (var mode in Enum.GetValues<VirtualControllerMode>())
                routes[mode] = OutputRouteOptions.From(next).Normalize();
        return next with { OutputRoutes = routes };
    }

    public OutputRouteOptions Route(VirtualControllerMode mode) =>
        Normalize().OutputRoutes.GetValueOrDefault(mode, new OutputRouteOptions());

    public BridgeOptions SelectRoute(VirtualControllerMode mode)
    {
        var normalized = Normalize();
        var validMode = Enum.IsDefined(mode) ? mode : VirtualControllerMode.Ns2Pro;
        return normalized.OutputRoutes.GetValueOrDefault(validMode, new OutputRouteOptions())
            .ApplyTo(normalized) with { Mode = validMode };
    }

    public BridgeOptions SaveRoute(VirtualControllerMode mode, OutputRouteOptions route)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var normalized = Normalize();
        var saved = route.Normalize();
        var routes = new Dictionary<VirtualControllerMode, OutputRouteOptions>(normalized.OutputRoutes) { [mode] = saved };
        return saved.ApplyTo(normalized) with { Mode = mode, OutputRoutes = routes };
    }

    internal BridgeOptions NormalizeValues() => this with {
        Mode = Enum.IsDefined(Mode) ? Mode : VirtualControllerMode.Ns2Pro,
        InputKind = Enum.IsDefined(InputKind) ? InputKind : BridgeInputKind.Ns2Ble,
        LastBleDevice = null,
        BleDevices = NormalizeBleDevices(),
        Ns2UsbAddresses = (Ns2UsbAddresses ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key) && Ns2PairingProtocol.IsAddress(p.Value)).ToDictionary(),
        Ns2Buttons = (Ns2Buttons ?? new()).Normalize(),
        PushHz = PushHz is -1 or 0 or 66 or 125 or 250 ? PushHz : 0,
        ApiPort = ApiPort is >= 1024 and <= 65535 ? ApiPort : 0,
        UsbPort = UsbPort is >= 1024 and <= 65535 ? UsbPort : 0,
        RumbleGain = Clamp(RumbleGain, 0, 3, 1),
        GyroPitch = Clamp(GyroPitch, .1, 4, 1), GyroYaw = Clamp(GyroYaw, .1, 4, 1), GyroRoll = Clamp(GyroRoll, .1, 4, 1),
        StickDeadzone = Clamp(StickDeadzone, 0, .3, 0), StickProfiles = StickProfiles ?? [],
        Motion = (Motion ?? new()).Normalize()
    };
    private BleDeviceHistory NormalizeBleDevices()
    {
        var history = (BleDevices ?? new()).Normalize();
        return LastBleDevice is { IsValid: true } legacy && history.AllowsAutoConnect(legacy.ToCandidate())
            ? history.Remember(legacy) : history;
    }
    private static double Clamp(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
    public static string SettingsPath => AppDataPaths.SettingsPath;
    public static BridgeOptions Load()
    {
        return Load(SettingsPath, Path.Combine(AppDataPaths.LocalRoot, "NS2ProWin11", "bridge-settings.json"));
    }

    internal static BridgeOptions Load(string settingsPath, string legacyPath)
    {
        try
        {
            bool migrate = !File.Exists(settingsPath) && File.Exists(legacyPath);
            string json = File.ReadAllText(migrate ? legacyPath : settingsPath);
            BridgeOptions? loaded = JsonSerializer.Deserialize<BridgeOptions>(json);
            if (loaded is null) return new();
            BridgeOptions normalized = loaded.Normalize();
            if (migrate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                    // Keep the legacy file as a backup. Never overwrite a newer user's settings.
                    // Stage the exact validated snapshot before moving it into place.
                    string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temporary, json);
                        File.Move(temporary, settingsPath, overwrite: false);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A read-only destination must not discard usable legacy settings.
                    System.Diagnostics.Trace.TraceWarning("Could not migrate settings: {0}", ex.Message);
                }
            }
            return normalized;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save() => Save(SettingsPath);

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Normalize(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record StickProfile(ushort[] Centers, ushort[] Minimum, ushort[] Maximum)
{
    public bool Valid => Centers is { Length: 4 } && Minimum is { Length: 4 } && Maximum is { Length: 4 } &&
        Enumerable.Range(0, 4).All(i => Centers[i] is >= 1024 and <= 3071 && Minimum[i] + 256 <= Centers[i] &&
            Maximum[i] >= Centers[i] + 256 && Maximum[i] <= 4095);
    public ushort Map(ushort raw, int axis)
    {
        if (!Valid) return raw;
        double delta = raw - Centers[axis];
        return (ushort)Math.Clamp(Math.Round(2048 + delta * (delta < 0 ? 2048.0 / (Centers[axis] - Minimum[axis]) :
            2047.0 / (Maximum[axis] - Centers[axis]))), 0, 4095);
    }
}
