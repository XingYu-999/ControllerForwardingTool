using System.Buffers.Binary;

namespace ControllerForwardingTool.Bluetooth;

// Nintendo manufacturer payload, excluding the two-byte company ID (0x0553).
// Layout: ndeadly/switch2_controller_research, bluetooth_interface.md.
public sealed record Ns2Advertisement(ulong TargetHost)
{
    public bool IsPairing => TargetHost == 0;
    public static Ns2Advertisement? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != 1 || data[1] != 0 || data[2] != 3 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[3..]) != 0x057E ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]) != 0x2069) return null;
        ulong host = 0;
        for (int i = 0; i < 6; i++) host |= (ulong)data[10 + i] << (8 * i);
        return new(host);
    }
    public bool TargetsOtherHost(ulong localAddress) => TargetHost != 0 && localAddress != 0 && TargetHost != localAddress;
}
