using System.Text.Json;
using CodexClaudeUsageWidget.Services;
using Xunit;

namespace CodexClaudeUsageWidget.Tests;

public sealed class QuotaParserTests
{
    private static readonly DateTimeOffset FetchedAt = DateTimeOffset.Parse("2026-10-12T10:00:00Z");

    [Fact]
    public void CodexMapsSessionAndWeeklyWindowsAndUnixReset()
    {
        using var response = JsonDocument.Parse("""
        {"rate_limit":{"primary_window":{"used_percent":42,"reset_at":1792000000},"secondary_window":{"used_percent":12,"limit_window_seconds":604800}}}
        """);
        var result = QuotaParser.ParseCodex(response.RootElement, FetchedAt);

        Assert.Equal(new[] { "Session", "Weekly" }, result.Windows.Select(window => window.Label));
        Assert.Equal(new[] { 42d, 12d }, result.Windows.Select(window => window.UsedPercent));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792000000), result.Windows[0].ResetsAt);
        Assert.NotNull(result.Windows[1].ResetsAt);
        Assert.Equal(FetchedAt, result.FetchedAt);
    }

    [Fact]
    public void CodexIncludesAdditionalQuotaAndClampsDisplayPercentage()
    {
        using var response = JsonDocument.Parse("""
        {"rate_limit":{"additional_rate_limits":[{"name":"Model cap","rate_limit":{"used_percent":125}}]}}
        """);
        var result = QuotaParser.ParseCodex(response.RootElement, FetchedAt);

        var window = Assert.Single(result.Windows);
        Assert.Equal("Model cap", window.Label);
        Assert.Equal(100, window.UsedPercent);
    }

    [Fact]
    public void CodexRejectsUnrecognizedResponseInsteadOfReturningEmptyQuota()
    {
        using var response = JsonDocument.Parse("""{"rate_limit":{"new_shape":{}}}""");
        Assert.Throws<InvalidDataException>(() => QuotaParser.ParseCodex(response.RootElement, FetchedAt));
    }

    [Fact]
    public void ClaudeMapsPlanWindowsAndClampsPercent()
    {
        using var response = JsonDocument.Parse("""
        {"five_hour":{"utilization":67,"resets_at":"2026-10-12T12:30:00Z"},"seven_day":{"utilization":9},"seven_day_opus":{"utilization":101}}
        """);
        var result = QuotaParser.ParseClaude(response.RootElement, FetchedAt);

        Assert.Equal(new[] { "5-hour", "Weekly", "Opus weekly" }, result.Windows.Select(window => window.Label));
        Assert.Equal(new[] { 67d, 9d, 100d }, result.Windows.Select(window => window.UsedPercent));
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T12:30:00Z"), result.Windows[0].ResetsAt);
        Assert.Null(result.Windows[1].ResetsAt);
    }

    [Fact]
    public void ClaudeRejectsUnknownQuotaShape()
    {
        using var response = JsonDocument.Parse("""{"unknown_window":{"utilization":10}}""");
        Assert.Throws<InvalidDataException>(() => QuotaParser.ParseClaude(response.RootElement, FetchedAt));
    }


}
