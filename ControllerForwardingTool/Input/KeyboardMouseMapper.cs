using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public enum MouseEmulationMode { RightStick, Gyroscope }

/// <summary>Maps Windows virtual keys and relative mouse velocity into canonical Nintendo coordinates.</summary>
public static class KeyboardMouseMapper
{
    public static ControllerState Map(Func<int, bool> down, double dx, double dy, double seconds,
        MouseEmulationMode mode, DateTimeOffset at)
    {
        var buttons = ControllerButtons.None;
        void Bind(int key, ControllerButtons button) { if (down(key)) buttons |= button; }
        Bind(0x20, ControllerButtons.B); Bind('J', ControllerButtons.B); Bind('K', ControllerButtons.A);
        Bind('U', ControllerButtons.Y); Bind('I', ControllerButtons.X);
        Bind('Q', ControllerButtons.L); Bind('E', ControllerButtons.R);
        Bind(0x02, ControllerButtons.ZL); Bind(0x01, ControllerButtons.ZR);
        Bind(0x10, ControllerButtons.LeftStick); Bind(0x11, ControllerButtons.RightStick);
        Bind(0x0D, ControllerButtons.Plus); Bind(0x09, ControllerButtons.Minus);
        Bind(0x25, ControllerButtons.Left); Bind(0x26, ControllerButtons.Up);
        Bind(0x27, ControllerButtons.Right); Bind(0x28, ControllerButtons.Down);
        Bind(0x70, ControllerButtons.Home); Bind('C', ControllerButtons.Capture);
        double x = (down('D') ? 1 : 0) - (down('A') ? 1 : 0);
        double y = (down('S') ? 1 : 0) - (down('W') ? 1 : 0);
        double length = Math.Max(1, Math.Sqrt(x * x + y * y));
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, .001, .1) : .008;
        double vx = double.IsFinite(dx) ? dx / seconds : 0;
        double vy = double.IsFinite(dy) ? dy / seconds : 0;
        bool gyro = mode == MouseEmulationMode.Gyroscope;
        return ControllerState.Neutral(at) with
        {
            Buttons = buttons, LeftX = StickCoordinates.FromSdl(x / length), LeftY = StickCoordinates.FromSdl(y / length, true),
            RightX = gyro ? (ushort)2048 : StickCoordinates.FromSdl(vx / 1200),
            RightY = gyro ? (ushort)2048 : StickCoordinates.FromSdl(vy / 1200, true),
            MotionTimestampMicroseconds = gyro ? unchecked((uint)(at.ToUnixTimeMilliseconds() * 1000)) : 0,
            // A stationary upright NS2 reports +Z gravity; pitch=X, yaw=Z, roll=-Y.
            AccelZ = gyro ? (short)4096 : (short)0,
            GyroX = gyro ? Raw(-vy * .15) : (short)0,
            GyroZ = gyro ? Raw(-vx * .15) : (short)0
        };
    }
    private static short Raw(double dps) => (short)Math.Clamp(Math.Round(dps * 16.384), short.MinValue, short.MaxValue);
}
