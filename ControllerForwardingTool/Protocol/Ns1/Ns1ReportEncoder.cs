using System.Buffers.Binary;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Protocol.Ns1;

/// <summary>NS1 0x30 input with synthetic factory calibration from UsbIpNs1Device.</summary>
public sealed class Ns1ReportEncoder
{
    private byte timer;

    public byte[] Encode(ControllerState state, bool imuEnabled = true)
    {
        byte[] report = new byte[64];
        report[0] = 0x30;
        report[1] = timer++;
        report[2] = 0x80; // Prototype battery placeholder; not sourced from NS2 yet.
        report[3] = BuildGroup(state.Buttons,
            (ControllerButtons.Y, 0x01), (ControllerButtons.X, 0x02),
            (ControllerButtons.B, 0x04), (ControllerButtons.A, 0x08),
            (ControllerButtons.R, 0x40), (ControllerButtons.ZR, 0x80));
        report[4] = BuildGroup(state.Buttons,
            (ControllerButtons.Minus, 0x01), (ControllerButtons.Plus, 0x02),
            (ControllerButtons.RightStick, 0x04), (ControllerButtons.LeftStick, 0x08),
            (ControllerButtons.Home, 0x10), (ControllerButtons.Capture, 0x20));
        report[5] = BuildGroup(state.Buttons,
            (ControllerButtons.Down, 0x01), (ControllerButtons.Up, 0x02),
            (ControllerButtons.Right, 0x04), (ControllerButtons.Left, 0x08),
            (ControllerButtons.L, 0x40), (ControllerButtons.ZL, 0x80));
        PackAxes(report, 6, state.LeftX, state.LeftY);
        PackAxes(report, 9, state.RightX, state.RightY);
        // NS1 -> SDL is (-Y, Z, -X); NS2 -> SDL is (X, Z, -Y).
        // Acceleration is 4096 counts/g in both. NS1 factory gyro scale is
        // 936/13371 degrees/sec per count; NS2 uses 16.384 counts/degree/sec.
        // Our source supplies one latest sample; repeat it in all three slots.
        if (imuEnabled)
            for (int offset = 13; offset < 49; offset += 12)
            {
                Write(report, offset, state.AccelY);
                Write(report, offset + 2, -(double)state.AccelX);
                Write(report, offset + 4, state.AccelZ);
                const double gyroScale = 13371.0 / (936.0 * 16.384);
                Write(report, offset + 6, state.GyroY * gyroScale);
                Write(report, offset + 8, -state.GyroX * gyroScale);
                Write(report, offset + 10, state.GyroZ * gyroScale);
            }
        // C, GL and GR have no NS1 counterparts.
        return report;
    }

    private static void Write(byte[] report, int offset, double value) =>
        BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(offset),
            (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue));

    private static byte BuildGroup(ControllerButtons buttons,
        params (ControllerButtons Button, byte Mask)[] entries)
    {
        byte result = 0;
        foreach (var (button, mask) in entries)
            if ((buttons & button) != 0) result |= mask;
        return result;
    }

    private static void PackAxes(Span<byte> destination, int offset, ushort x, ushort y)
    {
        x = (ushort)Math.Min(4095, (int)x);
        y = (ushort)Math.Min(4095, (int)y);
        destination[offset] = (byte)x;
        destination[offset + 1] = (byte)((x >> 8) | (y << 4));
        destination[offset + 2] = (byte)(y >> 4);
    }
}
