using ControllerForwardingTool.VirtualDevice;

namespace ControllerForwardingTool.Bluetooth;

/// <summary>Called by the single BLE writer on each tick. Times use a monotonic clock.</summary>
public sealed class BleRumblePlayback
{
    private Pro2OutputPacket? held;
    private bool transientActive;
    private TimeSpan transientAt;

    public Pro2OutputPacket? Next(Pro2OutputPacket? incoming, TimeSpan now)
    {
        if (incoming is not null)
        {
            held = incoming.Active && incoming.SustainUntilStopped ? incoming : null;
            transientActive = incoming.Active && !incoming.SustainUntilStopped;
            transientAt = now;
            return incoming;
        }
        if (held is not null) return held;
        if (transientActive && now - transientAt >= TimeSpan.FromMilliseconds(500))
        {
            transientActive = false;
            return Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "watchdog-stop");
        }
        return null;
    }
}
