using System.Text.Json.Serialization;
using Windows.Devices.Bluetooth;

namespace ControllerForwardingTool.Bluetooth;

// Remember only a device that supplied valid FD2 input. This is a discovery hint,
// not a Bluetooth bond or proof of identity; GATT and input are verified again.
public sealed record BleDeviceIdentity(ulong Address, BluetoothAddressType AddressType, string Name)
{
    public BleHostRegistration? Registration { get; init; }
    [JsonIgnore]
    public bool IsValid => Address is > 0 and < 0xFFFFFFFFFFFF &&
        AddressType is BluetoothAddressType.Public or BluetoothAddressType.Random;

    [JsonIgnore]
    public string? Alias { get; init; }
    [JsonIgnore]
    public string Summary => $"{(Alias ?? (string.IsNullOrWhiteSpace(Name) ? "NS2 Pro" : Name))} · ••:{Address & 0xFFFF:X4} · {AddressType}" +
        (Registration is { IsValid: true } ? " · 曾确认主机注册" : " · 尚未确认主机注册");

    public bool Matches(ulong address, BluetoothAddressType type) =>
        IsValid && Address == address && AddressType == type;

    public BleCandidate ToCandidate() => new(Address, AddressType, Name ?? "NS2 Pro", 0, DateTimeOffset.MinValue);
    public static BleDeviceIdentity FromCandidate(BleCandidate candidate) =>
        new(candidate.Address, candidate.AddressType, string.IsNullOrWhiteSpace(candidate.Name) ? "NS2 Pro" : candidate.Name);
}
