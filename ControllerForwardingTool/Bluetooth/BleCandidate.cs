using Windows.Devices.Bluetooth;

namespace ControllerForwardingTool.Bluetooth;

public sealed record BleCandidate(ulong Address, BluetoothAddressType AddressType,
    string Name, short Rssi, DateTimeOffset LastSeen)
{
    public string? Alias { get; init; }
    public Ns2Advertisement? Advertisement { get; init; }
    public DateTimeOffset? AdvertisementSeenAt { get; init; }
    public bool? IsConnectable { get; init; }
    public string MaskedAddress => $"••:{Address & 0xFFFF:X4}";
    public string DisplayName => !string.IsNullOrWhiteSpace(Alias) ? Alias : string.IsNullOrWhiteSpace(Name) ? "未命名候选设备" : Name;
    public string Summary => $"{DisplayName}  ·  {MaskedAddress}  ·  {Rssi} dBm  ·  {LastSeen:HH:mm:ss}";
}
