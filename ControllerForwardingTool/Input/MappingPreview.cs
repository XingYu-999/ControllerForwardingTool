using ControllerForwardingTool.Core;
using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public static class MappingPreview
{
    public static ControllerLayout Layout(VirtualControllerMode mode) => mode switch
    {
        VirtualControllerMode.Ns2Pro => ControllerLayout.Switch2Pro,
        VirtualControllerMode.Ns1Pro => ControllerLayout.SwitchPro,
        VirtualControllerMode.Xbox360 => ControllerLayout.Xbox,
        VirtualControllerMode.DualSenseEdge => ControllerLayout.DualSenseEdge,
        _ => ControllerLayout.DualSense
    };

    // Mirror the button capabilities/position conversion in VirtualReportEncoder.
    public static ControllerButtons OutputButtons(ControllerButtons buttons, VirtualControllerMode mode)
    {
        if (mode is VirtualControllerMode.DualSense or VirtualControllerMode.DualSenseEdge)
        {
            if ((buttons & ControllerButtons.Capture) != 0) buttons |= ControllerButtons.Touchpad;
            buttons &= ~(ControllerButtons.C | ControllerButtons.Capture);
            if (mode != VirtualControllerMode.DualSenseEdge) buttons &= ~(ControllerButtons.GL | ControllerButtons.GR);
        }
        else
        {
            buttons &= ~ControllerButtons.Touchpad;
            if (mode != VirtualControllerMode.Ns2Pro) buttons &= ~(ControllerButtons.C | ControllerButtons.GL | ControllerButtons.GR);
            if (mode == VirtualControllerMode.Xbox360) buttons &= ~ControllerButtons.Capture;
        }
        return ControllerLayouts.RemapFaceButtons(buttons, true, Layout(mode).IsNintendo());
    }
}
