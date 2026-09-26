using System.Buffers.Binary;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Protocol.Ns2;

// Offsets and button masks are observations from the local reference implementation,
// not a Nintendo specification. Keep raw values until hardware calibration is known.
public sealed class Fd2Decoder
{
    private const uint InvalidButtonMask = 0xFC300020;

    public bool TryDecode(ReadOnlySpan<byte> payload, DateTimeOffset receivedAt,
        out ControllerState state)
    {
        state = ControllerState.Neutral(receivedAt);
        if (payload.Length < 60)
            return false;

        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4, 4));
        if ((raw & InvalidButtonMask) != 0)
            return false;

        ControllerButtons buttons = ControllerButtons.None;
        Set(0x00000001, ControllerButtons.Y);
        Set(0x00000002, ControllerButtons.X);
        Set(0x00000004, ControllerButtons.B);
        Set(0x00000008, ControllerButtons.A);
        Set(0x00000040, ControllerButtons.R);
        Set(0x00000080, ControllerButtons.ZR);
        Set(0x00000100, ControllerButtons.Minus);
        Set(0x00000200, ControllerButtons.Plus);
        Set(0x00000400, ControllerButtons.RightStick);
        Set(0x00000800, ControllerButtons.LeftStick);
        Set(0x00001000, ControllerButtons.Home);
        Set(0x00002000, ControllerButtons.Capture);
        Set(0x00004000, ControllerButtons.C);
        Set(0x00010000, ControllerButtons.Down);
        Set(0x00020000, ControllerButtons.Up);
        Set(0x00040000, ControllerButtons.Right);
        Set(0x00080000, ControllerButtons.Left);
        Set(0x00400000, ControllerButtons.L);
        Set(0x00800000, ControllerButtons.ZL);
        Set(0x01000000, ControllerButtons.GR);
        Set(0x02000000, ControllerButtons.GL);

        state = new ControllerState(buttons,
            AxisX(payload, 10), AxisY(payload, 10),
            AxisX(payload, 13), AxisY(payload, 13),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(42, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(48, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(50, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(52, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(54, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(56, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(58, 2)),
            receivedAt);
        return true;

        void Set(uint mask, ControllerButtons button)
        {
            if ((raw & mask) != 0) buttons |= button;
        }
    }

    private static ushort AxisX(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | ((data[offset + 1] & 0x0F) << 8));

    private static ushort AxisY(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset + 1] >> 4) | (data[offset + 2] << 4));
}
