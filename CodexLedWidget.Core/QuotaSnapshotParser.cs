using System.Text.Json.Nodes;

namespace CodexLedWidget.Core;

public static class QuotaSnapshotParser
{
    public static QuotaSnapshot ParseRateLimitsResponse(string json)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Codex 返回了空响应。");
        IReadOnlyList<JsonNode> buckets = ResolveBuckets(root);
        JsonNode snapshotNode = buckets.FirstOrDefault()
            ?? throw new InvalidOperationException("Codex 响应中没有额度窗口。");
        List<QuotaWindow> windows = buckets
            .SelectMany(ParseWindows)
            .Take(2)
            .ToList();

        return new QuotaSnapshot(
            LimitId: ReadString(snapshotNode, "limitId") ?? "codex",
            LimitName: ReadString(snapshotNode, "limitName") ?? "Codex",
            PlanType: ReadString(snapshotNode, "planType") ?? "unknown",
            Primary: windows.ElementAtOrDefault(0),
            Secondary: windows.ElementAtOrDefault(1),
            FetchedAt: DateTimeOffset.Now,
            ResetCreditsAvailable: ReadInt(root["rateLimitResetCredits"], "availableCount"));
    }

    private static IReadOnlyList<JsonNode> ResolveBuckets(JsonNode root)
    {
        JsonNode? byId = root["rateLimitsByLimitId"];
        if (byId is JsonObject map)
        {
            List<JsonNode> buckets = [];
            if (map.TryGetPropertyValue("codex", out JsonNode? codex) && codex is not null)
            {
                buckets.Add(codex);
            }

            foreach (KeyValuePair<string, JsonNode?> item in map)
            {
                if (item.Key != "codex" && item.Value is not null)
                {
                    buckets.Add(item.Value);
                }
            }

            if (buckets.Count > 0)
            {
                return buckets;
            }
        }

        return root["rateLimits"] is JsonNode fallback ? [fallback] : [];
    }

    private static IEnumerable<QuotaWindow> ParseWindows(JsonNode bucket)
    {
        string limitId = ReadString(bucket, "limitId") ?? "codex";
        string limitName = ReadString(bucket, "limitName") ?? (limitId == "codex" ? "Codex" : limitId);
        QuotaWindow? primary = ParseWindow(bucket["primary"], limitId, limitName);
        QuotaWindow? secondary = ParseWindow(bucket["secondary"], limitId, limitName);

        if (primary is not null)
        {
            yield return primary;
        }

        if (secondary is not null)
        {
            yield return secondary;
        }
    }

    private static QuotaWindow? ParseWindow(JsonNode? node, string limitId, string limitName)
    {
        if (node is null)
        {
            return null;
        }

        int usedPercent = ClampPercent(ReadInt(node, "usedPercent") ?? 0);
        int remainingPercent = ClampPercent(100 - usedPercent);
        int? windowDurationMins = ReadInt(node, "windowDurationMins");
        long? resetsAtSeconds = ReadLong(node, "resetsAt");

        return new QuotaWindow(
            UsedPercent: usedPercent,
            RemainingPercent: remainingPercent,
            WindowDuration: windowDurationMins is null ? null : TimeSpan.FromMinutes(windowDurationMins.Value),
            ResetsAt: resetsAtSeconds is null ? null : DateTimeOffset.FromUnixTimeSeconds(resetsAtSeconds.Value).ToLocalTime(),
            LimitId: limitId,
            LimitName: limitName);
    }

    private static string? ReadString(JsonNode node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>();
    }

    private static int? ReadInt(JsonNode? node, string propertyName)
    {
        if (node is null)
        {
            return null;
        }

        JsonNode? value = node[propertyName];
        return value is null ? null : Convert.ToInt32(value.GetValue<double>());
    }

    private static long? ReadLong(JsonNode node, string propertyName)
    {
        JsonNode? value = node[propertyName];
        return value is null ? null : Convert.ToInt64(value.GetValue<double>());
    }

    private static int ClampPercent(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }
}
