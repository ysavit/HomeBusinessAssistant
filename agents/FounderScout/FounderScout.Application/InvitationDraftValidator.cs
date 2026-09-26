namespace FounderScout.Application;

/// <summary>One prior human-review-only draft used for local similarity checks.</summary>
public sealed record RecentInvitationDraft(Guid CandidateId, string ShortDraft, string DetailedDraft, string SimilarityFingerprint);

/// <summary>Deterministic draft validation result.</summary>
public sealed record InvitationDraftValidationResult(
    bool IsValid,
    IReadOnlyList<string> ErrorCodes,
    string SimilarityFingerprint,
    decimal MaximumObservedSimilarity);

/// <summary>Human-review-only invitation draft policy.</summary>
public interface IInvitationDraftValidator
{
    /// <summary>Validates grounding, safety, usefulness, length, and recent similarity.</summary>
    InvitationDraftValidationResult Validate(
        FounderEvaluationRequest request,
        ModelInvitationResponse invitation,
        IReadOnlyList<RecentInvitationDraft> recentDrafts,
        decimal similarityThreshold);
}

/// <summary>Conservative deterministic invitation draft validator; it has no send capability.</summary>
public sealed partial class InvitationDraftValidator : IInvitationDraftValidator
{
    private static readonly string[] ForbiddenAutomationTerms = ["automated analysis", "automation", "scoring", "scorecard", "scraping", "scraped", "algorithm ranked"];
    private static readonly string[] ForbiddenCommitments = ["i have decided to join", "i will join", "i will invest", "i'll invest", "build for free", "work for free", "unpaid developer"];
    private static readonly string[] ProtectedTerms = ["age", "gender", "race", "ethnicity", "religion", "marital", "disability", "health condition", "sexual orientation", "photo", "appearance"];
    private static readonly string[] ConnectionTerms = ["connect", "conversation", "chat", "compare notes", "founder-to-founder", "learn more"];

    /// <inheritdoc />
    public InvitationDraftValidationResult Validate(
        FounderEvaluationRequest request,
        ModelInvitationResponse invitation,
        IReadOnlyList<RecentInvitationDraft> recentDrafts,
        decimal similarityThreshold)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(recentDrafts);
        var errors = new HashSet<string>(StringComparer.Ordinal);
        string combined = $"{invitation.ShortDraft}\n{invitation.DetailedDraft}";
        string normalized = FounderEvidenceGroundingValidator.Normalize(combined);
        if (string.IsNullOrWhiteSpace(invitation.ShortDraft) || invitation.ShortDraft.Length > request.MaximumShortDraftCharacters)
            errors.Add("invitation.short.length");
        if (string.IsNullOrWhiteSpace(invitation.DetailedDraft) || invitation.DetailedDraft.Length > request.MaximumDetailedDraftCharacters)
            errors.Add("invitation.detailed.length");
        if (invitation.CandidateFactsUsed.Count is < 1 or > 4)
            errors.Add("invitation.fact.required");
        if (invitation.PersonaStrengthsUsed.Count is < 1 or > 4)
            errors.Add("invitation.complementarity.required");
        if (string.IsNullOrWhiteSpace(invitation.ConversationTopic))
            errors.Add("invitation.topic.required");
        if (!ConnectionTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
            errors.Add("invitation.reasonToConnect.required");

        FounderGroundingResult grounded = FounderEvidenceGroundingValidator.Validate(
            request,
            invitation.CandidateFactsUsed.Select(fact => new GroundingItem("$.invitation.candidateFactsUsed", fact, true)).ToArray());
        foreach (FounderEvaluationValidationError error in grounded.Errors)
            errors.Add(error.Code == "evaluation.evidence.fabricatedNumber" ? "invitation.claim.fabricatedNumber" : "invitation.fact.notGrounded");

        if (!invitation.CandidateFactsUsed.Any(fact => ContainsMeaningfulOverlap(combined, fact)))
            errors.Add("invitation.fact.notUsed");
        if (!invitation.PersonaStrengthsUsed.Any(strength => request.Persona.Strengths.Any(configured => MeaningfullyEquivalent(strength, configured)))
            || !invitation.PersonaStrengthsUsed.Any(strength => ContainsMeaningfulOverlap(combined, strength)))
            errors.Add("invitation.complementarity.notGrounded");
        if (ForbiddenAutomationTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
            errors.Add("invitation.automationMention");
        if (ForbiddenCommitments.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
            errors.Add("invitation.commitmentClaim");
        if (ProtectedTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal))
            || request.ProtectedMarkers.Any(marker => marker.Length > 0 && normalized.Contains(FounderEvidenceGroundingValidator.Normalize(marker), StringComparison.Ordinal)))
            errors.Add("invitation.protectedAttribute");
        if (GenericOnly(normalized, request))
            errors.Add("invitation.genericOnly");

        string fingerprint = FounderProfileCanonicalizer.HashUtf8(string.Join(' ', FounderEvidenceGroundingValidator.Tokenize(combined).Order(StringComparer.Ordinal)));
        RecentInvitationDraft[] comparableDrafts = recentDrafts
            .Where(previous => previous.CandidateId != request.CandidateId)
            .ToArray();
        decimal maximumSimilarity = comparableDrafts.Length == 0
            ? 0m
            : comparableDrafts.Max(previous => Jaccard(combined, $"{previous.ShortDraft}\n{previous.DetailedDraft}"));
        if (maximumSimilarity >= similarityThreshold)
            errors.Add("invitation.similarRecentDraft");
        return new(errors.Count == 0, errors.Order(StringComparer.Ordinal).ToArray(), fingerprint, Math.Round(maximumSimilarity, 4, MidpointRounding.AwayFromZero));
    }

    private static bool ContainsMeaningfulOverlap(string text, string fact)
    {
        HashSet<string> textTokens = FounderEvidenceGroundingValidator.Tokenize(text);
        HashSet<string> factTokens = FounderEvidenceGroundingValidator.Tokenize(fact);
        return factTokens.Count >= 2 && factTokens.Count(textTokens.Contains) >= Math.Min(3, factTokens.Count);
    }

    private static bool MeaningfullyEquivalent(string left, string right)
    {
        HashSet<string> leftTokens = FounderEvidenceGroundingValidator.Tokenize(left);
        HashSet<string> rightTokens = FounderEvidenceGroundingValidator.Tokenize(right);
        return leftTokens.Overlaps(rightTokens);
    }

    private static bool GenericOnly(string normalized, FounderEvaluationRequest request)
    {
        HashSet<string> draft = FounderEvidenceGroundingValidator.Tokenize(normalized);
        HashSet<string> profile = FounderEvidenceGroundingValidator.Tokenize(FounderProfileCanonicalizer.Canonicalize(request.Input));
        return draft.Count < 8 || draft.Count(profile.Contains) < 2;
    }

    private static decimal Jaccard(string left, string right)
    {
        HashSet<string> leftTokens = FounderEvidenceGroundingValidator.Tokenize(left);
        HashSet<string> rightTokens = FounderEvidenceGroundingValidator.Tokenize(right);
        if (leftTokens.Count == 0 && rightTokens.Count == 0) return 1m;
        int intersection = leftTokens.Count(rightTokens.Contains);
        int union = leftTokens.Union(rightTokens).Count();
        return union == 0 ? 0m : (decimal)intersection / union;
    }
}
