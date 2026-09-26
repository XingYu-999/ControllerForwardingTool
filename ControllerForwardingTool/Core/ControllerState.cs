namespace ControllerForwardingTool.Core;

[Flags]
public enum ControllerButtons : uint
{
    None = 0,
    Y = 1u << 0, X = 1u << 1, B = 1u << 2, A = 1u << 3,
    R = 1u << 4, ZR = 1u << 5, Minus = 1u << 6, Plus = 1u << 7,
    RightStick = 1u << 8, LeftStick = 1u << 9, Home = 1u << 10,
    Capture = 1u << 11, C = 1u << 12, Down = 1u << 13,
    Up = 1u << 14, Right = 1u << 15, Left = 1u << 16,
    L = 1u << 17, ZL = 1u << 18, GR = 1u << 19, GL = 1u << 20,
    Touchpad = 1u << 21
}

public sealed record ControllerState(
    ControllerButtons Buttons,
    ushort LeftX, ushort LeftY, ushort RightX, ushort RightY,
    uint MotionTimestampMicroseconds,
    short AccelX, short AccelY, short AccelZ,
    short GyroX, short GyroY, short GyroZ,
    DateTimeOffset ReceivedAt)
{
    // Null preserves native NS2 digital trigger behavior; SDL sources retain 0..255 travel.
    public byte? AnalogLeftTrigger { get; init; }
    public byte? AnalogRightTrigger { get; init; }
    public byte LeftTriggerValue => AnalogLeftTrigger ?? (Buttons.HasFlag(ControllerButtons.ZL) ? (byte)255 : (byte)0);
    public byte RightTriggerValue => AnalogRightTrigger ?? (Buttons.HasFlag(ControllerButtons.ZR) ? (byte)255 : (byte)0);
    public static ControllerState Neutral(DateTimeOffset at) =>
        new(ControllerButtons.None, 2048, 2048, 2048, 2048,
            0, 0, 0, 0, 0, 0, 0, at);
}
