namespace ControllerForwardingTool.Input;

// SDL retains HID devices whose first open failed, but does not retry their driver
// until a hint or device change. Retry only while no Switch 2 handle is in use.
internal sealed class Ns2UsbRecovery
{
    private double nextAttempt;

    internal bool ShouldRetry(double now, bool enabled, bool detected, bool opened)
    {
        if (!enabled || !detected || opened)
        {
            nextAttempt = now + 3;
            return false;
        }
        if (now < nextAttempt) return false;
        nextAttempt = now + 3;
        return true;
    }

    internal void RequestRetry() => nextAttempt = 0;

    internal static bool IsUsbCandidate(ushort vendor, ushort product, int bus, string serial) =>
        vendor == 0x057E && product == 0x2069 && bus == 1 &&
        !serial.StartsWith("NS2PROWIN11-", StringComparison.OrdinalIgnoreCase);
}
