using System.Buffers.Binary;
using ControllerForwardingTool.Bluetooth;

namespace ControllerForwardingTool.Usb;

internal enum UsbRegistrationAction { Inspect, Register, Delete }
internal sealed record UsbRegistrationResult(ulong Host, ulong Controller, bool Registered, bool HasOtherHosts);

// Commands and storage layout: ndeadly/switch2_controller_research.
// Never read keys or directly write flash to edit registration records.
internal static class Ns2UsbProtocol
{
    internal static byte[] Command(byte command, byte sub, ReadOnlySpan<byte> payload)
    {
        byte[] result = new byte[8 + payload.Length];
        result[0] = command; result[1] = 0x91; result[3] = sub; result[5] = checked((byte)payload.Length);
        payload.CopyTo(result.AsSpan(8)); return result;
    }

    internal static void Validate(byte[] reply, byte command, byte sub, int length)
    {
        if (reply.Length < length || reply[0] != command || reply[1] != 1 || reply[2] != 0 || reply[3] != sub || reply[5] != 0xF8)
            throw new InvalidOperationException("USB 命令回复未通过校验；注册状态未知");
    }

    internal static byte[] ReadMemory(Func<byte[], int, byte[]> exchange, uint address, byte count)
    {
        if (count is 0 or > 0x50) throw new ArgumentOutOfRangeException(nameof(count));
        byte[] payload = [count, 0x7E, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), address);
        byte[] reply = exchange(Command(2, 4, payload), 16 + count);
        Validate(reply, 2, 4, 16 + count);
        if (BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(8)) != count ||
            BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(12)) != address)
            throw new InvalidOperationException("USB 存储读取地址或长度不匹配");
        return reply.AsSpan(16, count).ToArray();
    }

    internal static ulong[] ReadHosts(Func<byte[], int, byte[]> exchange)
    {
        byte count = ReadMemory(exchange, 0x1FA000, 1)[0];
        if (count == 0xFF)
        {
            if (ReadMemory(exchange, 0x1FA000, 8).All(b => b == 0xFF)) return [];
            throw new InvalidOperationException("手柄注册区格式未知，无法确认注册状态");
        }
        if (count > 2) throw new InvalidOperationException("未知的手柄注册格式，未修改注册信息");
        var hosts = new ulong[count];
        for (int i = 0; i < count; i++)
        {
            // Flash stores addresses in display order; command packets use reversed order.
            byte[] bytes = ReadMemory(exchange, (uint)(0x1FA008 + i * 0x28), 6);
            ulong host = 0; foreach (byte b in bytes) host = (host << 8) | b;
            if (!Ns2PairingProtocol.IsAddress(host)) throw new InvalidOperationException("手柄注册地址无效，未修改注册信息");
            hosts[i] = host;
        }
        return hosts;
    }

    internal static async Task<UsbRegistrationResult> RunAsync(UsbRegistrationAction action, ulong host,
        Func<byte[], int, byte[]> exchange, CancellationToken token)
    {
        if (!Ns2PairingProtocol.IsAddress(host)) throw new InvalidOperationException("请先开启一个可用的蓝牙适配器");
        token.ThrowIfCancellationRequested();
        ulong[] hosts = ReadHosts(exchange);
        ulong controller = 0;
        if (action == UsbRegistrationAction.Register && !hosts.Contains(host))
        {
            await Ns2PairingProtocol.RegisterAsync(host, 0, (packet, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                int length = packet[3] switch { 1 => 17, 2 or 4 => 25, _ => 9 };
                return Task.FromResult(exchange(packet, length));
            }, _ => { }, token, transport: 0, controllerDiscovered: address => controller = address);
            hosts = ReadHosts(exchange);
            if (!hosts.Contains(host)) throw new InvalidOperationException("提交已回复，但读取未确认本机注册；请刷新状态");
        }
        else if (action == UsbRegistrationAction.Delete && hosts.Contains(host))
        {
            // No documented single-entry delete. Refuse a global clear if it would remove another host.
            if (hosts.Any(address => address != host))
                throw new InvalidOperationException("手柄还保存了其他主机。协议仅支持全部清除，为保留其他主机，本次未删除");
            token.ThrowIfCancellationRequested();
            byte[] reply = exchange(Command(3, 8, []), 8);
            Validate(reply, 3, 8, 8);
            if (ReadHosts(exchange).Length != 0) throw new InvalidOperationException("删除未通过读回确认，请刷新状态");
            hosts = [];
        }
        return new(host, controller, hosts.Contains(host), hosts.Any(address => address != host));
    }
}
