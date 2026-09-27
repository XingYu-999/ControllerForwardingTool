namespace ControllerForwardingTool.Input;

/// <summary>Wait for the activation click/held keys to release, then accept exactly one source.</summary>
public sealed class MappingCapture
{
    private bool waitingForRelease;
    private DateTimeOffset deadline;
    public bool Active { get; private set; }
    public bool TimedOut { get; private set; }
    public void Begin(IEnumerable<int> held, DateTimeOffset now)
    { Active = true; TimedOut = false; waitingForRelease = held.Any(); deadline = now.AddSeconds(15); }
    public void Cancel() => Active = false;
    public int? Observe(IReadOnlyCollection<int> pressed, DateTimeOffset now)
    {
        if (!Active) return null;
        if (now >= deadline) { Active = false; TimedOut = true; return null; }
        if (waitingForRelease) { waitingForRelease = pressed.Count != 0; return null; }
        if (pressed.Count > 1) { waitingForRelease = true; return null; }
        if (pressed.Count == 0) return null;
        Active = false;
        return pressed.First();
    }
}
