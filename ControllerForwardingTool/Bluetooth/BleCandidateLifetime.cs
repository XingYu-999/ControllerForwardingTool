namespace ControllerForwardingTool.Bluetooth;

internal static class BleCandidateLifetime
{
    // BLE has no reliable "powered off" advertisement. Expire scan sightings;
    // an established device is retained until its link ends instead. Pending
    // attempts do not keep an expired scan row selectable.
    internal static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(6);

    internal static bool IsVisible(BleCandidate candidate, DateTimeOffset now, BleCandidate? active, bool linkActive) =>
        (linkActive && active is not null && candidate.Address == active.Address && candidate.AddressType == active.AddressType) ||
        (candidate.LastSeen != DateTimeOffset.MinValue && candidate.LastSeen <= now && now - candidate.LastSeen < VisibilityTimeout);
}
