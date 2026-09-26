namespace ControllerForwardingTool.Bluetooth;

public sealed record BleDeviceHistory
{
    public BleDeviceIdentity[] Remembered { get; init; } = [];
    // Forgetting must survive subsequent advertisements and application restarts.
    // A fresh SYNC advertisement permits re-pairing, including in automatic mode.
    public BleDeviceIdentity[] Forgotten { get; init; } = [];
    // Naming a candidate does not mark it as successfully connected.
    public BleDeviceIdentity[] Aliases { get; init; } = [];

    public BleDeviceHistory Normalize()
    {
        var forgotten = Clean(Forgotten);
        return this with
        {
            Forgotten = forgotten,
            Aliases = Clean(Aliases).Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x with { Name = x.Name.Trim()[..Math.Min(x.Name.Trim().Length, 40)] }).ToArray(),
            Remembered = Clean(Remembered).Where(x => !forgotten.Any(y => y.Matches(x.Address, x.AddressType))).ToArray()
        };
    }

    public BleDeviceHistory Remember(BleDeviceIdentity device) => !device.IsValid ? Normalize() : (this with
    {
        Remembered = new[] { device }.Concat(Remembered ?? []).ToArray(),
        Forgotten = (Forgotten ?? []).Where(x => x is not null && !x.Matches(device.Address, device.AddressType)).ToArray()
    }).Normalize();

    public BleDeviceHistory Forget(BleDeviceIdentity device) => !device.IsValid ? Normalize() : (this with
    {
        Forgotten = new[] { device with { Name = "", Alias = null, Registration = null } }.Concat(Forgotten ?? []).ToArray(),
        Aliases = (Aliases ?? []).Where(x => x is not null && !x.Matches(device.Address, device.AddressType)).ToArray()
    }).Normalize();

    public bool AllowsAutoConnect(BleCandidate candidate) =>
        !Forgotten.Any(x => x.Matches(candidate.Address, candidate.AddressType));

    public bool AllowsManualConnect(BleCandidate candidate, DateTimeOffset now) =>
        AllowsAutoConnect(candidate) || IsFreshPairing(candidate, now);

    internal static bool IsFreshPairing(BleCandidate candidate, DateTimeOffset now) =>
        candidate.Advertisement is { IsPairing: true } &&
        candidate.AdvertisementSeenAt is { } seen && seen <= now && now - seen <= TimeSpan.FromSeconds(5);

    public string? GetAlias(ulong address, Windows.Devices.Bluetooth.BluetoothAddressType type) =>
        Aliases.FirstOrDefault(x => x.Matches(address, type))?.Name;

    public BleDeviceHistory Rename(BleCandidate candidate, string name)
    {
        name = name.Trim();
        if (name.Length > 40) throw new ArgumentException("名称最多 40 个字符");
        var identity = new BleDeviceIdentity(candidate.Address, candidate.AddressType, name);
        if (!identity.IsValid) throw new ArgumentException("候选设备地址无效");
        var remaining = Aliases.Where(x => !x.Matches(candidate.Address, candidate.AddressType));
        return (this with { Aliases = (name.Length == 0 ? remaining : new[] { identity }.Concat(remaining)).ToArray() }).Normalize();
    }

    private static BleDeviceIdentity[] Clean(BleDeviceIdentity[]? devices) => (devices ?? [])
        .Where(x => x is { IsValid: true }).DistinctBy(x => (x.Address, x.AddressType))
        .Select(x => x.Registration is not null && !x.Registration.IsValid ? x with { Registration = null } : x).ToArray();
}
