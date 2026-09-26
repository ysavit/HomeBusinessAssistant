namespace FounderScout.Application;

/// <summary>Stable recommendation states derived only from validated persisted dimensions.</summary>
public enum CandidateRecommendation
{
    /// <summary>High-priority founder-to-founder connection.</summary>
    StrongConnect = 0,
    /// <summary>A bounded exploratory call is appropriate.</summary>
    ExploratoryCall = 1,
    /// <summary>Retain for later observation.</summary>
    Monitor = 2,
    /// <summary>Do not prioritize an invitation.</summary>
    Pass = 3,
    /// <summary>Confidence or validation requires human judgment.</summary>
    ManualReview = 4,
}

/// <summary>Configurable activity recency buckets.</summary>
public sealed record FounderScoutActivitySettings(
    int RecentDays,
    decimal RecentScore,
    int ActiveDays,
    decimal ActiveScore,
    int StaleDays,
    decimal StaleScore,
    decimal OlderScore,
    decimal UnknownScore)
{
    /// <summary>Safe deterministic initial activity policy.</summary>
    public static FounderScoutActivitySettings Default { get; } = new(7, 100m, 30, 80m, 90, 55m, 20m, 25m);
}

/// <summary>Inputs required for deterministic C# score arithmetic.</summary>
public sealed record CandidateScoreRequest(
    IReadOnlyList<ModelScoreItem> QualityCategories,
    IReadOnlyList<ModelScoreItem> FitDimensions,
    IReadOnlyList<ModelRiskSignal> Risks,
    decimal ProfileCompleteness,
    decimal EvidenceCoverage,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset EvaluatedAtUtc,
    FounderScoutPrioritySettings Priority,
    FounderScoutRankingSettings Ranking,
    FounderScoutActivitySettings Activity,
    decimal MaximumRiskPenalty);

/// <summary>Separately stored deterministic evaluation/ranking dimensions.</summary>
public sealed record CandidateScoreResult(
    decimal BaseScore,
    decimal FounderQualityScore,
    decimal OurFitScore,
    decimal RiskPenalty,
    decimal AdjustedScore,
    decimal OverallConfidence,
    decimal ActivityScore,
    decimal InvitationPriority,
    CandidateRecommendation Recommendation);

/// <summary>Authoritative candidate score calculator.</summary>
public interface ICandidateScoreCalculator
{
    /// <summary>Calculates scores without trusting any model-provided total.</summary>
    CandidateScoreResult Calculate(CandidateScoreRequest request);
}

/// <summary>Exact C# score, confidence, activity, penalty, priority, and recommendation policy.</summary>
public sealed class CandidateScoreCalculator : ICandidateScoreCalculator
{
    /// <inheritdoc />
    public CandidateScoreResult Calculate(CandidateScoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, ModelScoreItem> quality = request.QualityCategories.ToDictionary(item => item.Key, StringComparer.Ordinal);
        Dictionary<string, ModelScoreItem> fit = request.FitDimensions.ToDictionary(item => item.Key, StringComparer.Ordinal);
        decimal baseScore = FounderEvaluationScorecard.Quality.Sum(definition => quality[definition.Key].Score);
        decimal ctoFit = quality["ctoFit"].Score;
        decimal founderQuality = Round((baseScore - ctoFit) / 90m * 100m, 2);
        decimal ourFit = FounderEvaluationScorecard.Fit.Sum(definition => fit[definition.Key].Score);
        decimal riskPenalty = Math.Min(
            request.MaximumRiskPenalty,
            request.Risks
                .Where(risk => risk.Evidence.Count > 0)
                .Where(risk => FounderEvaluationScorecard.RiskPenalties.ContainsKey(risk.Key))
                .Select(risk => (decimal)FounderEvaluationScorecard.RiskPenalties[risk.Key])
                .Sum());
        decimal adjusted = Round(Math.Clamp(baseScore - riskPenalty, 0m, 100m), 2);

        decimal weightedConfidence = 0m;
        foreach (FounderScoreDefinition definition in FounderEvaluationScorecard.Quality)
        {
            weightedConfidence += quality[definition.Key].Confidence * definition.Maximum;
        }
        foreach (FounderScoreDefinition definition in FounderEvaluationScorecard.Fit)
        {
            weightedConfidence += fit[definition.Key].Confidence * definition.Maximum;
        }
        weightedConfidence /= 200m;
        decimal overallConfidence = Round(Math.Clamp(
            weightedConfidence * 0.70m
            + request.ProfileCompleteness * 0.20m
            + request.EvidenceCoverage * 0.10m,
            0m,
            1m), 4);
        decimal activity = CalculateActivity(request.LastActivityAtUtc, request.EvaluatedAtUtc, request.Activity);
        decimal priority = Round(Math.Clamp(
            ourFit * request.Priority.OurFitWeight
            + founderQuality * request.Priority.FounderQualityWeight
            + overallConfidence * 100m * request.Priority.ConfidenceWeight
            + activity * request.Priority.ActivityWeight
            - riskPenalty,
            0m,
            100m), 2);
        CandidateRecommendation recommendation = overallConfidence < request.Ranking.MinimumConfidence
            ? CandidateRecommendation.ManualReview
            : priority >= request.Ranking.StrongConnectThreshold && adjusted >= request.Ranking.ExploratoryThreshold
                ? CandidateRecommendation.StrongConnect
                : priority >= request.Ranking.ExploratoryThreshold
                    ? CandidateRecommendation.ExploratoryCall
                    : priority >= request.Ranking.MonitorThreshold
                        ? CandidateRecommendation.Monitor
                        : CandidateRecommendation.Pass;
        return new(baseScore, founderQuality, ourFit, riskPenalty, adjusted, overallConfidence, activity, priority, recommendation);
    }

    /// <summary>Calculates activity from configured UTC recency buckets.</summary>
    public static decimal CalculateActivity(
        DateTimeOffset? lastActivityAtUtc,
        DateTimeOffset evaluatedAtUtc,
        FounderScoutActivitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!lastActivityAtUtc.HasValue) return settings.UnknownScore;
        double days = Math.Max(0d, (evaluatedAtUtc.ToUniversalTime() - lastActivityAtUtc.Value.ToUniversalTime()).TotalDays);
        return days <= settings.RecentDays
            ? settings.RecentScore
            : days <= settings.ActiveDays
                ? settings.ActiveScore
                : days <= settings.StaleDays
                    ? settings.StaleScore
                    : settings.OlderScore;
    }

    private static decimal Round(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
