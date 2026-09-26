using System.Numerics;

namespace ControllerForwardingTool.Input;

/// <summary>Body-to-world orientation in SDL coordinates: X right, Y up, Z toward the player.
/// Integrates new gyro samples and corrects tilt using gravity; yaw remains relative.</summary>
public sealed class MotionOrientation
{
    public Quaternion Value { get; private set; } = Quaternion.Identity;
    private bool initialized;
    private double lastTime = double.NaN;

    public void Reset() { initialized = false; lastTime = double.NaN; Value = Quaternion.Identity; }

    public void Observe(double timestamp, Vector3 accel, Vector3 gyroDps)
    {
        if (!double.IsFinite(timestamp) || !Finite(accel) || !Finite(gyroDps)) return;
        if (double.IsFinite(lastTime) && timestamp <= lastTime) return;
        double dt = timestamp - lastTime;
        lastTime = timestamp;
        float norm = accel.Length();
        bool gravityValid = norm is > .75f and < 1.25f;
        if (!initialized || dt > .25)
        {
            if (!gravityValid) { initialized = false; return; }
            Value = Align(accel / norm, Vector3.UnitY);
            initialized = true;
            return;
        }
        if (!double.IsFinite(dt) || dt <= 0 || dt > .1) return;
        Vector3 rate = gyroDps * (MathF.PI / 180);
        if (gravityValid)
        {
            Vector3 expectedUp = Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(Value));
            rate += Vector3.Cross(accel / norm, expectedUp) * 2f;
        }
        float angle = rate.Length() * (float)dt;
        if (angle > 1e-9f)
            Value = Quaternion.Normalize(Value * Quaternion.CreateFromAxisAngle(Vector3.Normalize(rate), angle));
    }

    private static Quaternion Align(Vector3 from, Vector3 to)
    {
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (dot > .999999f) return Quaternion.Identity;
        if (dot < -.999999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public static class MotionCoordinates
{
    // NS2 native Z points up; convert both acceleration and angular velocity with the same rotation.
    public static Vector3 FromNs2(Vector3 native) => new(native.X, native.Z, -native.Y);
    public static Vector3 ToNs2(Vector3 canonical) => new(canonical.X, -canonical.Z, canonical.Y);
}
