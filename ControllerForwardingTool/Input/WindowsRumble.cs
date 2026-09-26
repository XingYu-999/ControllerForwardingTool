using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Input;

public static class WindowsRumble
{
    // Reduce Nintendo/PS5 haptic frames to ordinary low/high motor strength. Waveform/frequency,
    // audio and adaptive trigger effects cannot be reproduced by SDL's two-motor API.
    public static RumbleSettings FromPacket(Pro2OutputPacket packet, double gain)
    {
        if (!packet.Active || packet.Report.Length < 23) return new(0,0,200);
        double low = 0, high = 0;
        foreach (int offset in new[] {2,18})
        {
            var f = packet.Report.AsSpan(offset,5);
            high = Math.Max(high, ((f[1]&0xfc)<<4) | ((f[2]&0x0f)<<12));
            low = Math.Max(low, (f[3]&0xc0) | (f[4]<<8));
        }
        double multiplier = double.IsFinite(gain) ? Math.Clamp(gain,0,3) : 1;
        return RumbleSettings.Create(low / 29000 * multiplier * 100, high / 29000 * multiplier * 100, 200);
    }
}
