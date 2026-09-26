using System.Buffers.Binary;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.VirtualDevice;

public static class Ns2VirtualReport
{
    // VIIPER haptic's TCP wire protocol includes a 32-bit motion timestamp after the 24-byte state.
    public static byte[] Encode(ControllerState state)
    {
        ControllerButtons[] bits = [ControllerButtons.B, ControllerButtons.A, ControllerButtons.Y,
            ControllerButtons.X, ControllerButtons.R, ControllerButtons.ZR, ControllerButtons.Plus,
            ControllerButtons.RightStick, ControllerButtons.Down, ControllerButtons.Right,
            ControllerButtons.Left, ControllerButtons.Up, ControllerButtons.L, ControllerButtons.ZL,
            ControllerButtons.Minus, ControllerButtons.LeftStick, ControllerButtons.Home,
            ControllerButtons.Capture, ControllerButtons.GR, ControllerButtons.GL, ControllerButtons.C];
        uint mask = 0;
        for (int i = 0; i < bits.Length; i++) if ((state.Buttons & bits[i]) != 0) mask |= 1u << i;
        byte[] data = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(data, mask);
        ushort[] axes = [state.LeftX, state.LeftY, state.RightX, state.RightY];
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4 + i * 2), axes[i]);
        short[] motion = [state.AccelX, state.AccelY, state.AccelZ, state.GyroX, state.GyroY, state.GyroZ];
        for (int i = 0; i < 6; i++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + i * 2), motion[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), state.MotionTimestampMicroseconds);
        return data;
    }
}
