namespace CodexLedWidget.Core;

public sealed record QuotaWindow(
    int UsedPercent,
    int RemainingPercent,
    TimeSpan? WindowDuration,
    DateTimeOffset? ResetsAt,
    string? LimitId = null,
    string? LimitName = null);

public sealed record QuotaSnapshot(
    string LimitId,
    string LimitName,
    string PlanType,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    DateTimeOffset FetchedAt,
    int? ResetCreditsAvailable = null)
{
    public int? RemainingPercent => Primary?.RemainingPercent ?? Secondary?.RemainingPercent;
    public DateTimeOffset? ResetsAt => Primary?.ResetsAt ?? Secondary?.ResetsAt;

    public QuotaSnapshot ApplyElapsedResets(DateTimeOffset now)
    {
        return this with
        {
            Primary = ResetElapsedWindow(Primary, now),
            Secondary = ResetElapsedWindow(Secondary, now)
        };
    }

    private static QuotaWindow? ResetElapsedWindow(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is not DateTimeOffset resetsAt || resetsAt > now)
        {
            return window;
        }

        return window with
        {
            UsedPercent = 0,
            RemainingPercent = 100,
            ResetsAt = null
        };
    }
}
