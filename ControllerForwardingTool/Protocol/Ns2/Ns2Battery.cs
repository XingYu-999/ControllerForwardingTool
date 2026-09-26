using System.Buffers.Binary;

namespace ControllerForwardingTool.Protocol.Ns2;

// https://github.com/ndeadly/switch2_controller_research/blob/master/hid_reports.md
// BLE omits the report ID. FD2 (0x05) contains voltage; 0x09 contains a coarse level.
public sealed record Ns2PowerLevel(int Level, bool ExternalPower, bool Charging)
{
    public string Description => $"电量 {Level}/9 档" + (Charging ? " · 充电中" : ExternalPower ? " · 外接电源" : "");
}

public static class Ns2Battery
{
    public static int? ReadMillivolts(ReadOnlySpan<byte> fd2)
    {
        if (fd2.Length < 60) return null;
        int voltage = BinaryPrimitives.ReadUInt16LittleEndian(fd2.Slice(0x1F, 2));
        return voltage is >= 2500 and <= 5000 ? voltage : null;
    }

    public static Ns2PowerLevel? ReadPowerLevel(ReadOnlySpan<byte> report09)
    {
        if (report09.Length < 15) return null;
        byte power = report09[1];
        int level = (power >> 2) & 15;
        return (power & 0xC0) != 0 || level > 9 ? null : new(level, (power & 1) != 0, (power & 2) != 0);
    }
}
