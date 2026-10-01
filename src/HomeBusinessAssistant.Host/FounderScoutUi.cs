using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;

namespace HomeBusinessAssistant.Host;

internal static class FounderScoutUi
{
    public static FounderScoutCandidateQuery CandidateQuery(
        string? search = null,
        IReadOnlyList<string>? recommendations = null,
        IReadOnlyList<FounderScout.Domain.CandidateStatus>? statuses = null,
        decimal? minimumScore = null,
        decimal? minimumConfidence = null,
        bool? needsManualReview = null,
        FounderScoutCandidateSort sort = FounderScoutCandidateSort.LastCapturedDescending,
        int offset = 0,
        int pageSize = 50) => new(
            search,
            recommendations,
            statuses,
            minimumScore,
            MaximumScore: null,
            minimumConfidence,
            TechnicalStatus: null,
            CommitmentStatus: null,
            IdeaStatus: null,
            HasTractionEvidence: null,
            RiskKey: null,
            ChangedFromUtc: null,
            ChangedToUtc: null,
            ActiveSinceUtc: null,
            QueueKind: null,
            AccountId: null,
            SegmentId: null,
            needsManualReview,
            sort,
            offset,
            pageSize);

    public static FounderScoutRunProgress? GetRunProgress(ManagementRunDetail? detail)
    {
        if (detail is null || detail.Events.Count == 0) return null;

        AgentRunEventRecord? latest = detail.Events
            .Where(item => item.EventType is "progress" or "heartbeat" or "checkpoint" or "warning" or "error" or "started")
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefault();
        AgentRunEventRecord? progress = detail.Events
            .Where(item => string.Equals(item.EventType, "progress", StringComparison.Ordinal))
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefault();
        if (latest is null) return null;

        ProgressData latestData = ReadProgressData(latest.DataJson);
        ProgressData boundedData = progress is null ? default : ReadProgressData(progress.DataJson);
        double? percentage = boundedData.Percentage;
        if (percentage is null && boundedData.Current is long current && boundedData.Total is > 0)
        {
            percentage = Math.Clamp((double)current / boundedData.Total.Value * 100d, 0d, 100d);
        }

        return new(
            latestData.Phase ?? boundedData.Phase,
            latest.Message,
            boundedData.Current,
            boundedData.Total,
            percentage,
            latest.TimestampUtc);
    }

    public static string? GetDiscoveryOutcome(ManagementRunItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Run.SummaryJson)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(item.Run.SummaryJson);
            if (!document.RootElement.TryGetProperty("agentData", out JsonElement data)
                || !data.TryGetProperty("discovery", out JsonElement discovery)
                || discovery.ValueKind != JsonValueKind.Object) return null;
            int viewed = discovery.TryGetProperty("viewedProfiles", out JsonElement viewedValue) && viewedValue.TryGetInt32(out int viewedCount)
                ? viewedCount : 0;
            int saved = discovery.TryGetProperty("newSnapshots", out JsonElement savedValue) && savedValue.TryGetInt32(out int savedCount)
                ? savedCount : 0;
            string? reason = discovery.TryGetProperty("completionReasonCode", out JsonElement reasonValue)
                ? reasonValue.GetString() : null;
            string explanation = reason switch
            {
                "discovery.noProgress" => "Stopped after two batches found no new profile links.",
                "discovery.newProfileLimit" => "Stopped at the configured new-profile limit.",
                "discovery.viewedLimit" => "Stopped at the configured viewed-profile limit.",
                "discovery.runtimeLimit" => "Stopped at the configured runtime limit.",
                "discovery.dailyLimit" => "Stopped at the daily capture limit.",
                "discovery.consecutiveKnownLimit" => "Stopped after too many already-known profiles.",
                "discovery.sourceExhausted" => "The source had no more profiles in this segment.",
                _ => string.IsNullOrWhiteSpace(reason) ? "" : $"Stop reason: {reason}.",
            };
            return $"Viewed {viewed} profiles and saved {saved} new snapshots. {explanation}".Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProgressData ReadProgressData(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return new(
                ReadString(root, "Phase"),
                ReadInt64(root, "Current"),
                ReadInt64(root, "Total"),
                ReadDouble(root, "Percentage"));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JsonElement? FindProperty(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        JsonElement? value = FindProperty(root, name);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        JsonElement? value = FindProperty(root, name);
        return value.HasValue && value.Value.TryGetInt64(out long result) ? result : null;
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        JsonElement? value = FindProperty(root, name);
        return value.HasValue && value.Value.TryGetDouble(out double result) ? result : null;
    }

    private readonly record struct ProgressData(string? Phase, long? Current, long? Total, double? Percentage);
}

internal sealed record FounderScoutRunProgress(
    string? Phase,
    string Message,
    long? Current,
    long? Total,
    double? Percentage,
    DateTimeOffset ObservedAtUtc);
