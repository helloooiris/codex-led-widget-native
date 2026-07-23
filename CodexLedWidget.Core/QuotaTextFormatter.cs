using System.Globalization;

namespace CodexLedWidget.Core;

public static class QuotaTextFormatter
{
    public static string FormatWindowLabel(QuotaWindow? window, string cultureName)
    {
        if (window is null)
        {
            return "--";
        }

        string duration = FormatWindowDuration(window.WindowDuration, cultureName);
        return string.IsNullOrWhiteSpace(window.LimitName)
            ? duration
            : $"{window.LimitName} · {duration}";
    }

    public static string FormatWindowShortLabel(QuotaWindow window)
    {
        if (!string.IsNullOrWhiteSpace(window.LimitName) &&
            window.LimitName.Contains("Spark", StringComparison.OrdinalIgnoreCase))
        {
            return "Spark";
        }

        return FormatWindowDuration(window.WindowDuration, "en-US", shortForm: true);
    }

    public static string FormatWindow(QuotaWindow? window, string cultureName)
    {
        if (window is null)
        {
            return "--";
        }

        string remainingLabel = cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "剩余" : "left";
        string resetLabel = cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "重置" : "resets";
        List<string> parts = [$"{window.RemainingPercent}% {remainingLabel}"];

        if (window.ResetsAt is DateTimeOffset resetsAt)
        {
            parts.Add($"{resetLabel} {FormatResetDateTime(resetsAt, cultureName)}");
        }

        return string.Join(" · ", parts);
    }

    public static string FormatResetDateTime(DateTimeOffset dateTime, string cultureName)
    {
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return $"{dateTime.Month}月{dateTime.Day}日 {dateTime:HH\\:mm}";
        }

        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        return $"{dateTime.ToString("MMM d", culture)} {dateTime:HH\\:mm}";
    }

    public static string FormatPlan(string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType) || planType.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "--";
        }

        string lower = planType.Trim().ToLowerInvariant();
        return string.Concat(char.ToUpperInvariant(lower[0]), lower[1..]);
    }

    public static string FormatPlanSummary(string? planType, int? resetCreditsAvailable, string cultureName)
    {
        string plan = FormatPlan(planType);
        if (resetCreditsAvailable is null)
        {
            return plan;
        }

        string resetText = cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? $"{resetCreditsAvailable} 次可用重置"
            : $"{resetCreditsAvailable} resets available";
        return plan == "--" ? resetText : $"{plan} · {resetText}";
    }

    private static string FormatWindowDuration(TimeSpan? duration, string cultureName, bool shortForm = false)
    {
        if (duration is null)
        {
            return shortForm
                ? "--"
                : cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "额度窗口" : "quota window";
        }

        double minutes = duration.Value.TotalMinutes;
        if (minutes % 10080 == 0)
        {
            int weeks = Math.Max(1, (int)Math.Round(minutes / 10080));
            return shortForm
                ? $"{weeks}w"
                : cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? $"{weeks * 7}天窗口" : $"{weeks}w window";
        }

        if (minutes % 1440 == 0)
        {
            int days = (int)Math.Round(minutes / 1440);
            return shortForm
                ? $"{days}d"
                : cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? $"{days}天窗口" : $"{days}d window";
        }

        if (minutes % 60 == 0)
        {
            int hours = (int)Math.Round(minutes / 60);
            return shortForm
                ? $"{hours}h"
                : cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? $"{hours}小时窗口" : $"{hours}h window";
        }

        int roundedMinutes = (int)Math.Round(minutes);
        return shortForm
            ? $"{roundedMinutes}m"
            : cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? $"{roundedMinutes}分钟窗口" : $"{roundedMinutes}m window";
    }
}
