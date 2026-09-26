namespace ControllerForwardingTool.VirtualDevice;

public static class OutputRatePolicy
{
    // Reference cadence of our target USB profiles, not a claim about every retail firmware.
    public const int TargetUsbRate = -1;
    public static int TargetHz(VirtualControllerMode mode) => mode == VirtualControllerMode.Ns1Pro ? 125 : 250;
    public static int Resolve(int pushHz, VirtualControllerMode mode) => pushHz == TargetUsbRate ? TargetHz(mode) : pushHz;
}

// Coalesced wake-up only: state is held separately, so slow consumers never build a stale queue.
internal sealed class OutputWakeSignal
{
    private readonly SemaphoreSlim changed = new(0, 1);
    public void Signal() { try { changed.Release(); } catch (SemaphoreFullException) { } }
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token) => changed.WaitAsync(timeout, token);
}
