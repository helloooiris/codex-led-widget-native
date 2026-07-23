namespace CodexLedWidget.Core;

public sealed record DualQuotaMeterSegment(string ShortLabel, int RemainingPercent, bool HasData)
{
    public string PercentText => HasData ? $"{RemainingPercent}%" : "--";
}

public sealed record DualQuotaMeter(DualQuotaMeterSegment Left, DualQuotaMeterSegment Right)
{
    public static DualQuotaMeter FromSnapshot(QuotaSnapshot snapshot, string cultureName)
    {
        return new DualQuotaMeter(
            CreateSegment(snapshot.Primary),
            CreateSegment(snapshot.Secondary));
    }

    private static DualQuotaMeterSegment CreateSegment(QuotaWindow? window)
    {
        if (window is null)
        {
            return new DualQuotaMeterSegment("--", 0, false);
        }

        return new DualQuotaMeterSegment(
            QuotaTextFormatter.FormatWindowShortLabel(window),
            Math.Clamp(window.RemainingPercent, 0, 100),
            true);
    }
}
