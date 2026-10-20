using System.IO;
using System.Globalization;
using System.Text.Json;
using CodexClaudeUsageWidget.Models;

namespace CodexClaudeUsageWidget.Services;

public static class QuotaParser
{
    public static QuotaSnapshot ParseCodex(JsonElement root, DateTimeOffset fetchedAt)
    {
        if (!root.TryGetProperty("rate_limit", out var rateLimit) || rateLimit.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Codex response did not contain rate_limit.");
        var windows = new List<QuotaWindow>();
        AddCodexWindow(rateLimit, "primary_window", "Session", windows);
        AddCodexWindow(rateLimit, "secondary_window", "Weekly", windows);
        if (rateLimit.TryGetProperty("additional_rate_limits", out var additional) && additional.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in additional.EnumerateArray())
            {
                var currentIndex = index++;
                var nested = item.TryGetProperty("rate_limit", out var inner) ? inner : item;
                var label = ReadString(item, "name") ?? ReadString(item, "limit_name") ?? $"Additional {currentIndex + 1}";
                AddCodexWindow(nested, $"additional_{currentIndex}", label, windows);
            }
        }
        if (windows.Count == 0) throw new InvalidDataException("Codex response had no recognized quota windows.");
        return new QuotaSnapshot("codex", fetchedAt, windows);
    }

    public static QuotaSnapshot ParseClaude(JsonElement root, DateTimeOffset fetchedAt)
    {
        (string Id, string Label)[] definitions =
        [
            ("five_hour", "5-hour"), ("seven_day", "Weekly"), ("seven_day_opus", "Opus weekly"),
            ("seven_day_sonnet", "Sonnet weekly"), ("seven_day_oauth_apps", "OAuth apps weekly"),
        ];
        var windows = new List<QuotaWindow>();
        foreach (var (id, label) in definitions)
        {
            if (!root.TryGetProperty(id, out var item) || item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("utilization", out var used) || !used.TryGetDouble(out var percent)) continue;
            windows.Add(new QuotaWindow(id, label, Math.Clamp(percent, 0, 100), ReadDate(item, "resets_at") ?? ReadDate(item, "reset_at")));
        }
        if (windows.Count == 0) throw new InvalidDataException("Claude response had no recognized quota windows.");
        return new QuotaSnapshot("claude", fetchedAt, windows);
    }



    private static void AddCodexWindow(JsonElement source, string id, string label, List<QuotaWindow> windows)
    {
        JsonElement item;
        if (source.TryGetProperty(id, out var nested) && nested.ValueKind == JsonValueKind.Object)
            item = nested;
        else if (source.TryGetProperty("used_percent", out _))
            item = source;
        else return;
        if (!item.TryGetProperty("used_percent", out var used) || !used.TryGetDouble(out var percent)) return;
        DateTimeOffset? reset = ReadDate(item, "reset_at");
        if (reset is null && item.TryGetProperty("limit_window_seconds", out var duration) && duration.TryGetDouble(out var seconds))
            reset = DateTimeOffset.UtcNow.AddSeconds(seconds);
        windows.Add(new QuotaWindow(id, label, Math.Clamp(percent, 0, 100), reset));
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var timestamp))
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(timestamp >= 1e12 ? (long)timestamp : (long)(timestamp * 1000)); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date;
        return null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
