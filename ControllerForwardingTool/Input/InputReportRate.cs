namespace ControllerForwardingTool.Input;

/// <summary>One-second sliding window of distinct received reports, independent of UI polling.</summary>
public sealed class InputReportRate
{
    private readonly Queue<(ulong Id, double At)> reports = new();
    private readonly HashSet<ulong> ids = [];
    private double? startedAt;

    public void Observe(ulong reportId, double now)
    {
        Prune(now);
        if (!ids.Add(reportId)) return;
        startedAt ??= now;
        reports.Enqueue((reportId, now));
    }

    public double? Read(double now)
    {
        Prune(now);
        if (startedAt is not { } start || now - start < 1) return null;
        return reports.Count;
    }

    private void Prune(double now)
    {
        while (reports.TryPeek(out var report) && now - report.At >= 1)
        {
            reports.Dequeue();
            ids.Remove(report.Id);
        }
    }
}
