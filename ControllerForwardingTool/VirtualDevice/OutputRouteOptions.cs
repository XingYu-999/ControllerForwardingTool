using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.VirtualDevice;

/// <summary>Output-specific preferences. Device calibration and application settings remain global.</summary>
public sealed record OutputRouteOptions
{
    public const double DefaultRumbleGain = .7;

    public BridgeInputKind InputKind { get; init; } = BridgeInputKind.KeyboardMouse;
    public MouseEmulationMode MouseMode { get; init; } = MouseEmulationMode.RightStick;
    public GyroInputSource GyroSource { get; init; } = GyroInputSource.Automatic;
    public bool KeyboardMouseSupplementEnabled { get; init; }
    public Ns2ButtonMapping Ns2Buttons { get; init; } = new();
    public Dictionary<int, ControllerForwardingTool.Core.ControllerButtons> KeyboardOverrides { get; init; } = [];
    public Dictionary<StickDirection, int> KeyboardStickBindings { get; init; } = [];
    public StickMapping ControllerSticks { get; init; } = new();
    public int PushHz { get; init; }
    public int ApiPort { get; init; }
    public int UsbPort { get; init; }
    public double RumbleGain { get; init; } = DefaultRumbleGain;
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

    public static OutputRouteOptions CreateDefault(VirtualControllerMode mode)
    {
        var route = new OutputRouteOptions();
        // Outputs with native back buttons retain GL/GR; all others use L3/R3.
        if (mode is VirtualControllerMode.Ns2Pro or VirtualControllerMode.DualSenseEdge)
            return route with { Ns2Buttons = new() { Bindings = [] } };
        if (mode != VirtualControllerMode.Ns1Pro) return route;

        // NS2 -> NS1 preset captured from the release application's saved route on 2026-09-27.
        // Store portable preferences only, never device identities or pairing/calibration data.
        return route with
        {
            InputKind = BridgeInputKind.Ns2Ble,
            MouseMode = MouseEmulationMode.Gyroscope,
            KeyboardMouseSupplementEnabled = true,
            Ns2Buttons = new() { Bindings = new()
            {
                [ControllerButtons.A] = ControllerButtons.B,
                [ControllerButtons.B] = ControllerButtons.A,
                [ControllerButtons.Plus] = ControllerButtons.None,
                [ControllerButtons.GL] = ControllerButtons.LeftStick,
                [ControllerButtons.GR] = ControllerButtons.RightStick
            } },
            KeyboardOverrides = new()
            {
                ['K'] = ControllerButtons.B, ['L'] = ControllerButtons.A,
                ['J'] = ControllerButtons.Y, ['I'] = ControllerButtons.X,
                ['Q'] = ControllerButtons.L, ['E'] = ControllerButtons.R,
                ['1'] = ControllerButtons.ZL, ['4'] = ControllerButtons.ZR,
                ['C'] = ControllerButtons.RightStick, ['Z'] = ControllerButtons.LeftStick,
                [38] = ControllerButtons.Up, [40] = ControllerButtons.Down,
                [37] = ControllerButtons.Left, [39] = ControllerButtons.Right,
                [32] = ControllerButtons.Plus, ['N'] = ControllerButtons.Minus,
                ['M'] = ControllerButtons.Home, ['B'] = ControllerButtons.Capture,
                [1] = ControllerButtons.A, [2] = ControllerButtons.LeftStick
            },
            KeyboardStickBindings = new()
            {
                [StickDirection.LeftUp] = 'W', [StickDirection.LeftDown] = 'X',
                [StickDirection.LeftLeft] = 'A', [StickDirection.LeftRight] = 'D'
            }
        };
    }

    public static OutputRouteOptions From(BridgeOptions options) => new()
    {
        InputKind = options.InputKind, MouseMode = options.MouseMode, GyroSource = options.GyroSource, KeyboardMouseSupplementEnabled = options.KeyboardMouseSupplementEnabled,
        Ns2Buttons = options.Ns2Buttons, KeyboardOverrides = new(options.KeyboardOverrides),
        KeyboardStickBindings = new(options.KeyboardStickBindings), ControllerSticks = options.ControllerSticks with { },
        PushHz = options.PushHz, ApiPort = options.ApiPort, UsbPort = options.UsbPort,
        RumbleGain = options.RumbleGain, AudioGuard = options.AudioGuard,
        GyroPitch = options.GyroPitch, GyroYaw = options.GyroYaw, GyroRoll = options.GyroRoll,
        InvertPitch = options.InvertPitch, InvertYaw = options.InvertYaw, InvertRoll = options.InvertRoll,
        StickDeadzone = options.StickDeadzone, RadialDeadzone = options.RadialDeadzone, UseStickCalibration = options.UseStickCalibration
    };

    public BridgeOptions ApplyTo(BridgeOptions options) => options with
    {
        InputKind = InputKind, MouseMode = MouseMode, GyroSource = GyroSource, KeyboardMouseSupplementEnabled = KeyboardMouseSupplementEnabled,
        Ns2Buttons = Ns2Buttons, KeyboardOverrides = new(KeyboardOverrides ?? []), PushHz = PushHz, ApiPort = ApiPort, UsbPort = UsbPort,
        KeyboardStickBindings = new(KeyboardStickBindings ?? []), ControllerSticks = ControllerSticks ?? new(),
        RumbleGain = RumbleGain, AudioGuard = AudioGuard,
        GyroPitch = GyroPitch, GyroYaw = GyroYaw, GyroRoll = GyroRoll,
        InvertPitch = InvertPitch, InvertYaw = InvertYaw, InvertRoll = InvertRoll,
        StickDeadzone = StickDeadzone, RadialDeadzone = RadialDeadzone, UseStickCalibration = UseStickCalibration
    };

    public OutputRouteOptions Normalize() => From(ApplyTo(new BridgeOptions()).NormalizeValues());
}
