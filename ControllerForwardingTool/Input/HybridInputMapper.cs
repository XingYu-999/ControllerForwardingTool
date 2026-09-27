using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public enum GyroInputSource { Automatic, Controller, Mouse }

/// <summary>Supplement physical buttons and motion without ever replacing physical stick axes.</summary>
public static class HybridInputMapper
{
    public static bool UsesMouse(GyroInputSource choice, bool controllerHasMotion, VirtualControllerMode output) =>
        output != VirtualControllerMode.Xbox360 && (choice == GyroInputSource.Mouse ||
            choice == GyroInputSource.Automatic && !controllerHasMotion);

    public static ControllerState Merge(ControllerState physical, ControllerState mouse, IReadOnlySet<int> keys,
        BridgeOptions options, bool controllerHasMotion)
    {
        if (!options.KeyboardMouseSupplementEnabled) return physical;
        var buttons = ControllerButtons.None;
        foreach (var key in keys)
            if (KeyboardMapping.CanBind(key) && options.KeyboardOverrides.TryGetValue(key, out var target)) buttons |= target;
        var result = physical with
        {
            Buttons = physical.Buttons | buttons,
            AnalogLeftTrigger = buttons.HasFlag(ControllerButtons.ZL) ? (byte)255 : physical.AnalogLeftTrigger,
            AnalogRightTrigger = buttons.HasFlag(ControllerButtons.ZR) ? (byte)255 : physical.AnalogRightTrigger
        };
        if (UsesMouse(options.GyroSource, controllerHasMotion, options.Mode))
            result = result with
            {
                MotionTimestampMicroseconds = mouse.MotionTimestampMicroseconds,
                AccelX = mouse.AccelX, AccelY = mouse.AccelY, AccelZ = mouse.AccelZ,
                GyroX = mouse.GyroX, GyroY = mouse.GyroY, GyroZ = mouse.GyroZ
            };
        return result;
    }
}
