using System.Globalization;
using System.Text.RegularExpressions;

namespace ControllerForwardingTool.Bluetooth;

internal static class BleHostAddress
{
    internal static ulong Resolve(string deviceId, ulong peer, IEnumerable<ulong> adapterAddresses)
    {
        var addresses = adapterAddresses.Where(Ns2PairingProtocol.IsAddress).Distinct().ToArray();
        // WinRT connection IDs commonly include local and remote addresses. This
        // is a hint, cross-checked against enumerated radios, not a stable API contract.
        var match = Regex.Match(deviceId, @"^BluetoothLE#BluetoothLE(?<host>(?:[0-9a-f]{2}:){5}[0-9a-f]{2})-(?<peer>(?:[0-9a-f]{2}:){5}[0-9a-f]{2})$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            ulong host = ulong.Parse(match.Groups["host"].Value.Replace(":", ""), NumberStyles.HexNumber);
            ulong remote = ulong.Parse(match.Groups["peer"].Value.Replace(":", ""), NumberStyles.HexNumber);
            if (remote == peer && addresses.Contains(host)) return host;
            throw new InvalidOperationException("当前连接与蓝牙适配器地址不一致，未执行主机注册");
        }
        if (addresses.Length == 1) return addresses[0];
        throw new InvalidOperationException("无法唯一确认当前连接使用的蓝牙适配器；请仅启用一个适配器后长按 SYNC");
    }
}
