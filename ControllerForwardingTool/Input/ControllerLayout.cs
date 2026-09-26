namespace ControllerForwardingTool.Input;

public enum ControllerLayout { Generic, SwitchPro, Switch2Pro, Xbox, DualShock3, DualShock, DualSense, DualSenseEdge }

public static class ControllerLayouts
{
    public static ControllerForwardingTool.Core.ControllerButtons RemapFaceButtons(ControllerForwardingTool.Core.ControllerButtons buttons, bool sourceNintendo, bool targetNintendo)
    {
        if (sourceNintendo == targetNintendo) return buttons;
        const ControllerForwardingTool.Core.ControllerButtons face = ControllerForwardingTool.Core.ControllerButtons.A | ControllerForwardingTool.Core.ControllerButtons.B | ControllerForwardingTool.Core.ControllerButtons.X | ControllerForwardingTool.Core.ControllerButtons.Y;
        var result = buttons & ~face;
        void Swap(ControllerForwardingTool.Core.ControllerButtons from, ControllerForwardingTool.Core.ControllerButtons to) { if ((buttons & from) != 0) result |= to; }
        Swap(ControllerForwardingTool.Core.ControllerButtons.A, ControllerForwardingTool.Core.ControllerButtons.B);
        Swap(ControllerForwardingTool.Core.ControllerButtons.B, ControllerForwardingTool.Core.ControllerButtons.A);
        Swap(ControllerForwardingTool.Core.ControllerButtons.X, ControllerForwardingTool.Core.ControllerButtons.Y);
        Swap(ControllerForwardingTool.Core.ControllerButtons.Y, ControllerForwardingTool.Core.ControllerButtons.X);
        return result;
    }
    public static ControllerLayout Identify(ushort vendor, ushort product, int sdlType) => (vendor, product) switch
    {
        (0x057e, 0x2069) => ControllerLayout.Switch2Pro,
        (0x057e, 0x2009) => ControllerLayout.SwitchPro,
        (0x054c, 0x0df2) => ControllerLayout.DualSenseEdge,
        (0x054c, 0x0ce6) => ControllerLayout.DualSense,
        _ => sdlType switch
        {
            2 or 3 => ControllerLayout.Xbox,
            4 => ControllerLayout.DualShock3,
            5 => ControllerLayout.DualShock,
            6 => ControllerLayout.DualSense,
            7 => ControllerLayout.SwitchPro,
            _ => ControllerLayout.Generic
        }
    };

    public static string Name(this ControllerLayout layout) => layout switch
    {
        ControllerLayout.SwitchPro => "Switch Pro",
        ControllerLayout.Switch2Pro => "Switch 2 Pro",
        ControllerLayout.Xbox => "Xbox",
        ControllerLayout.DualShock3 => "DualShock 3",
        ControllerLayout.DualShock => "DualShock 4",
        ControllerLayout.DualSense => "DualSense",
        ControllerLayout.DualSenseEdge => "DualSense Edge",
        _ => "通用布局"
    };
    public static bool IsNintendo(this ControllerLayout layout) => layout is ControllerLayout.SwitchPro or ControllerLayout.Switch2Pro;
    public static bool IsPlayStation(this ControllerLayout layout) => layout is ControllerLayout.DualShock3 or ControllerLayout.DualShock or ControllerLayout.DualSense or ControllerLayout.DualSenseEdge;
    public static string[] ButtonLabels(this ControllerLayout layout)
    {
        bool n = layout.IsNintendo(), ps = layout.IsPlayStation();
        return [n ? "B" : ps ? "×" : "A", n ? "A" : ps ? "○" : "B", n ? "Y" : ps ? "□" : "X", n ? "X" : ps ? "△" : "Y",
            n ? "−" : layout == ControllerLayout.DualShock3 ? "Select" : layout is ControllerLayout.DualSense or ControllerLayout.DualSenseEdge ? "Create" : ps ? "Share" : "View",
            ps ? "PS" : "Home", n ? "+" : layout == ControllerLayout.DualShock3 ? "Start" : ps ? "Options" : "Menu",
            "L3", "R3", n ? "L" : ps ? "L1" : "LB", n ? "R" : ps ? "R1" : "RB", "↑", "↓", "←", "→",
            n ? "Capture" : ps ? "Mic" : "Share", n ? "GR" : "R paddle", n ? "GL" : "L paddle", "R2 paddle", "L2 paddle", "Touchpad", n ? "C" : "Misc2", "Misc3", "Misc4", "Misc5", "Misc6"];
    }
}

public readonly record struct RumbleSettings(ushort Low, ushort High, uint DurationMs)
{
    public static RumbleSettings Create(double lowPercent, double highPercent, double durationMs) => new(
        Level(lowPercent), Level(highPercent), (uint)Math.Clamp(double.IsFinite(durationMs) ? durationMs : 1000, 50, 10000));
    private static ushort Level(double percent) => (ushort)Math.Round(Math.Clamp(double.IsFinite(percent) ? percent : 0, 0, 100) * 65535 / 100);
}
