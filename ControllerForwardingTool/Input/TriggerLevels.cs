using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

/// <summary>SDL standard trigger axes are independent 0..1 values, not button bits.
/// Raw HID axis numbering is device-specific and must not be assumed to match SDL.</summary>
public readonly record struct TriggerLevels(double Left, double Right)
{
    public static TriggerLevels FromStandardAxes(ReadOnlySpan<double> axes) => new(
        axes.Length > 4 ? Normalize(axes[4]) : 0,
        axes.Length > 5 ? Normalize(axes[5]) : 0);

    public static TriggerLevels FromDigital(ControllerButtons buttons) => new(
        (buttons & ControllerButtons.ZL) != 0 ? 1 : 0,
        (buttons & ControllerButtons.ZR) != 0 ? 1 : 0);

    public static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
