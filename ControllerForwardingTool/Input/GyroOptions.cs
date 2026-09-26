namespace ControllerForwardingTool.Input;

public sealed record GyroOptions
{
    public bool AutoCalibrate { get; init; } = true;
    public bool SmoothSmallMotion { get; init; }
    public double AngularThreshold { get; init; } = 1.5; // degrees / second
    public double AccelerationThreshold { get; init; } = .035; // change of the gravity vector in g
    public double StationarySeconds { get; init; } = 3;
    public GyroOptions Normalize() => this with
    {
        AngularThreshold = FiniteClamp(AngularThreshold, .2, 5, 1.5),
        AccelerationThreshold = FiniteClamp(AccelerationThreshold, .01, .15, .035),
        StationarySeconds = FiniteClamp(StationarySeconds, 2, 10, 3)
    };
    private static double FiniteClamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
