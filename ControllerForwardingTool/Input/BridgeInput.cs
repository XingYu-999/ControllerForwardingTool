using System.Numerics;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public enum BridgeInputKind { Ns2Ble, WindowsGamepad }
public sealed record BridgeComparison(ControllerState Source, ControllerState Output);

/// <summary>Native Nintendo/XInput and tester coordinates: +Y is up. SDL +Y is down.</summary>
public static class StickCoordinates
{
    public static double Unit(ushort value) => Math.Clamp((value - 2048) / (value >= 2048 ? 2047.0 : 2048.0), -1, 1);
    public static ushort FromSdl(double value, bool y = false)
    {
        double unit = double.IsFinite(value) ? Math.Clamp(y ? -value : value, -1, 1) : 0;
        return (ushort)Math.Clamp(Math.Round(2048 + unit * (unit >= 0 ? 2047 : 2048)), 0, 4095);
    }
}

public static class WindowsInputMapper
{
    public static ControllerState Map(GamepadDevice device, GamepadSnapshot input, MotionReading? motion, DateTimeOffset at)
    {
        if (!input.Standard || !device.Standard) return ControllerState.Neutral(DateTimeOffset.MinValue);
        var buttons = ControllerLayouts.RemapFaceButtons(input.Buttons, device.Nintendo, true);
        var triggers = input.Triggers;
        // Nintendo has digital triggers. Keep analog travel separately for Xbox/PlayStation output.
        buttons &= ~(ControllerButtons.ZL | ControllerButtons.ZR);
        if (triggers.Left > .5) buttons |= ControllerButtons.ZL;
        if (triggers.Right > .5) buttons |= ControllerButtons.ZR;
        bool hasMotion = input.Accel is not null && input.Gyro is not null && motion is { Available: true };
        Vector3 accel = hasMotion ? MotionCoordinates.ToNs2(motion!.AccelG) : Vector3.Zero;
        Vector3 gyro = hasMotion ? MotionCoordinates.ToNs2(motion!.FilteredGyro) : Vector3.Zero;
        return new(buttons,
            StickCoordinates.FromSdl(input.Axes.ElementAtOrDefault(0)), StickCoordinates.FromSdl(input.Axes.ElementAtOrDefault(1), true),
            StickCoordinates.FromSdl(input.Axes.ElementAtOrDefault(2)), StickCoordinates.FromSdl(input.Axes.ElementAtOrDefault(3), true),
            hasMotion ? unchecked((uint)(long)(motion!.Timestamp * 1_000_000)) : 0,
            Raw(accel.X,4096), Raw(accel.Y,4096), Raw(accel.Z,4096), Raw(gyro.X,16.384), Raw(gyro.Y,16.384), Raw(gyro.Z,16.384), at)
        { AnalogLeftTrigger = (byte)Math.Round(triggers.Left * 255), AnalogRightTrigger = (byte)Math.Round(triggers.Right * 255) };
    }
    private static short Raw(float value, double scale) => (short)Math.Clamp(Math.Round(float.IsFinite(value) ? value * scale : 0), short.MinValue, short.MaxValue);
}

/// <summary>Serializes source changes with publication. Late callbacks from the previous source
/// cannot overwrite a neutral frame or another controller's state. Runs independently of UI polling.</summary>
public sealed class ControllerInputBridge(Action<ControllerState> publish, Func<BridgeOptions> options, Func<uint, MotionReading?> motion)
{
    private readonly object gate = new();
    private BridgeInputKind kind;
    private GamepadDevice? device;
    private GamepadDevice? ns2Usb;
    private bool usbLive;
    public void SelectNs2Usb(GamepadDevice? pad)
    {
        lock (gate)
        {
            if (ns2Usb?.Id == pad?.Id) return;
            ns2Usb = pad; usbLive = false;
            if (kind == BridgeInputKind.Ns2Ble) Emit(ControllerState.Neutral(DateTimeOffset.MinValue));
        }
    }
    private BridgeComparison comparison = new(ControllerState.Neutral(DateTimeOffset.MinValue), ControllerState.Neutral(DateTimeOffset.MinValue));
    public BridgeComparison Comparison => Volatile.Read(ref comparison);
    public ControllerState Latest => Comparison.Output;
    public void Select(BridgeInputKind source, GamepadDevice? pad)
    {
        lock (gate) { kind = source; device = pad; Emit(ControllerState.Neutral(DateTimeOffset.MinValue)); }
    }
    public void Ble(ControllerState state, StickProfile? profile)
    {
        lock (gate) if (kind == BridgeInputKind.Ns2Ble && !usbLive)
        {
            var config = options();
            Emit(StickMath.Apply(config.Ns2Buttons.Apply(state), profile, config), state);
        }
    }
    public void DisconnectBle()
    {
        lock (gate) if (kind == BridgeInputKind.Ns2Ble && !usbLive) Emit(ControllerState.Neutral(DateTimeOffset.MinValue));
    }
    public void Windows(GamepadFrame frame, double now)
    {
        lock (gate)
        {
            var source = kind == BridgeInputKind.Ns2Ble ? ns2Usb : device;
            if (source is null || now - frame.At > .25 || now < frame.At || !frame.Inputs.TryGetValue(source.Id, out var input) ||
                !frame.Devices.Any(d => d.Id == source.Id) || !input.Standard)
            {
                if (kind == BridgeInputKind.WindowsGamepad || usbLive)
                    if (Latest.ReceivedAt != DateTimeOffset.MinValue) Emit(ControllerState.Neutral(DateTimeOffset.MinValue));
                usbLive = false; return;
            }
            usbLive = kind == BridgeInputKind.Ns2Ble;
            var state = WindowsInputMapper.Map(source, input, motion(source.Id), DateTimeOffset.Now);
            var config = options();
            var raw = state;
            if (source.Layout == ControllerLayout.Switch2Pro) state = config.Ns2Buttons.Apply(state);
            Emit(StickMath.Apply(state, null, config), raw);
        }
    }
    private void Emit(ControllerState state, ControllerState? source = null)
    { Volatile.Write(ref comparison, new(source ?? state, state)); publish(state); }
}

/// <summary>Keep known physical devices even when VID/PID matches the target. Block our serials
/// and matching devices first enumerated during our virtual attachment. Device IDs are session-local.</summary>
public sealed class BridgeInputGuard
{
    private readonly HashSet<uint> beforeAttach = [], owned = [];
    private VirtualProfile? attaching;
    public void Begin(IEnumerable<GamepadDevice> devices, VirtualControllerMode mode)
    { beforeAttach.Clear(); beforeAttach.UnionWith(devices.Select(d=>d.Id)); attaching = VirtualProfile.Get(mode); }
    public void Observe(IEnumerable<GamepadDevice> devices)
    {
        foreach (var d in devices)
            if (d.Serial.StartsWith("NS2PROWIN11-",StringComparison.OrdinalIgnoreCase) ||
                attaching is { } p && !beforeAttach.Contains(d.Id) && d.Vendor == p.Vendor && d.Product == p.Product)
                owned.Add(d.Id);
    }
    public void End() => attaching = null;
    public bool IsOwned(GamepadDevice device) => owned.Contains(device.Id) || device.Serial.StartsWith("NS2PROWIN11-", StringComparison.OrdinalIgnoreCase);
    public bool Allows(GamepadDevice device) => device.Standard && !owned.Contains(device.Id) &&
        !device.Serial.StartsWith("NS2PROWIN11-",StringComparison.OrdinalIgnoreCase);
}
