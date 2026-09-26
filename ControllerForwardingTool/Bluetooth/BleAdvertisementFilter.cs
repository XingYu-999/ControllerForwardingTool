using Windows.Devices.Bluetooth;

namespace ControllerForwardingTool.Bluetooth;

internal static class BleAdvertisementFilter
{
    public static bool Accepts(ulong address, BluetoothAddressType addressType, string name,
        bool nintendoManufacturer, BleDeviceIdentity? remembered) =>
        remembered?.Matches(address, addressType) == true || nintendoManufacturer ||
        new[] { "Pro Controller", "Pro2", "Switch", "Nintendo" }
            .Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
}
