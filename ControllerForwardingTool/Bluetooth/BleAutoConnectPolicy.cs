namespace ControllerForwardingTool.Bluetooth;

internal static class BleAutoConnectPolicy
{
    // Host fields and IsConnectable are deliberately not eligibility checks.
    // Windows scan responses/adapter metadata can be incomplete even in SYNC mode.
    internal static bool CanAttempt(BleCandidate candidate, BleDeviceHistory history,
        bool enabled, bool connected, bool connecting, BleConnectionFailure? failure, DateTimeOffset now) =>
        enabled && !connected && !connecting &&
        candidate.LastSeen != DateTimeOffset.MinValue && candidate.LastSeen <= now &&
        now - candidate.LastSeen < TimeSpan.FromSeconds(2) &&
        history.AllowsManualConnect(candidate, now) &&
        (failure is null || (candidate.LastSeen > failure.FinishedAt &&
            (now >= failure.FinishedAt.AddSeconds(2) ||
                (!failure.WasPairing && BleDeviceHistory.IsFreshPairing(candidate, now)))));
}

// A new SYNC session may immediately supersede a failed ordinary wake. Repeated
// failures in the same mode require a short cooldown AND a new advertisement.
internal sealed record BleConnectionFailure(DateTimeOffset FinishedAt, bool WasPairing);
