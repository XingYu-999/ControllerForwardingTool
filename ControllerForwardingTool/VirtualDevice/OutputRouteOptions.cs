using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.VirtualDevice;

/// <summary>Output-specific preferences. Device calibration and application settings remain global.</summary>
public sealed record OutputRouteOptions
{
    public BridgeInputKind InputKind { get; init; }
    public Ns2ButtonMapping Ns2Buttons { get; init; } = new();
    public int PushHz { get; init; }
    public int ApiPort { get; init; }
    public int UsbPort { get; init; }
    public double RumbleGain { get; init; } = 1;
    public bool AudioGuard { get; init; } = true;
    public double GyroPitch { get; init; } = 1;
    public double GyroYaw { get; init; } = 1;
    public double GyroRoll { get; init; } = 1;
    public bool InvertPitch { get; init; }
    public bool InvertYaw { get; init; }
    public bool InvertRoll { get; init; }
    public double StickDeadzone { get; init; }
    public bool RadialDeadzone { get; init; } = true;
    public bool UseStickCalibration { get; init; } = true;

    public static OutputRouteOptions From(BridgeOptions options) => new()
    {
        InputKind = options.InputKind, Ns2Buttons = options.Ns2Buttons,
        PushHz = options.PushHz, ApiPort = options.ApiPort, UsbPort = options.UsbPort,
        RumbleGain = options.RumbleGain, AudioGuard = options.AudioGuard,
        GyroPitch = options.GyroPitch, GyroYaw = options.GyroYaw, GyroRoll = options.GyroRoll,
        InvertPitch = options.InvertPitch, InvertYaw = options.InvertYaw, InvertRoll = options.InvertRoll,
        StickDeadzone = options.StickDeadzone, RadialDeadzone = options.RadialDeadzone, UseStickCalibration = options.UseStickCalibration
    };

    public BridgeOptions ApplyTo(BridgeOptions options) => options with
    {
        InputKind = InputKind, Ns2Buttons = Ns2Buttons, PushHz = PushHz, ApiPort = ApiPort, UsbPort = UsbPort,
        RumbleGain = RumbleGain, AudioGuard = AudioGuard,
        GyroPitch = GyroPitch, GyroYaw = GyroYaw, GyroRoll = GyroRoll,
        InvertPitch = InvertPitch, InvertYaw = InvertYaw, InvertRoll = InvertRoll,
        StickDeadzone = StickDeadzone, RadialDeadzone = RadialDeadzone, UseStickCalibration = UseStickCalibration
    };

    public OutputRouteOptions Normalize() => From(ApplyTo(new BridgeOptions()).NormalizeValues());
}
