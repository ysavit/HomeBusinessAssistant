using FounderScout.Application;

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
        FounderScoutCandidateSort sort = FounderScoutCandidateSort.PriorityDescending,
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
}
