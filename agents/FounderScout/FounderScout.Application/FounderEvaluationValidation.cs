using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FounderScout.Application;

/// <summary>One stable deterministic validation finding.</summary>
public sealed record FounderEvaluationValidationError(string Code, string Path, string Message);

/// <summary>Typed validation result used before any completed evaluation is persisted.</summary>
public sealed record FounderEvaluationValidationResult(
    bool IsValid,
    IReadOnlyList<FounderEvaluationValidationError> Errors,
    decimal EvidenceCoverage);

/// <summary>Strict structure, key, range, and evidence validator.</summary>
public sealed class FounderEvaluationValidator
{
    private static readonly Regex Html = new("<[^>]+>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    /// <summary>Validates one typed response against exact definitions and supplied profile evidence.</summary>
    public static FounderEvaluationValidationResult Validate(
        FounderEvaluationRequest request,
        ModelEvaluationResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        var errors = new List<FounderEvaluationValidationError>();
        if (!string.Equals(response.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            Add(errors, "evaluation.schema.version", "$.schemaVersion", "The evaluator schema version is unsupported.");
        }

        IReadOnlyList<ModelScoreItem> quality = response.QualityCategories ?? [];
        IReadOnlyList<ModelScoreItem> fit = response.FitDimensions ?? [];
        IReadOnlyList<ModelRiskSignal> risks = response.Risks ?? [];
        ValidateExactScores(quality, FounderEvaluationScorecard.Quality, "$.qualityCategories", errors);
        ValidateExactScores(fit, FounderEvaluationScorecard.Fit, "$.fitDimensions", errors);
        ValidateBoundedText(response.ProfileSummary, 2_000, "$.profileSummary", errors);
        ValidateBoundedText(response.RecommendationRationale, 2_000, "$.recommendationRationale", errors);
        ValidateStrings(response.PositiveSignals, 20, 500, "$.positiveSignals", errors);
        ValidateStrings(response.RedFlags, 20, 500, "$.redFlags", errors);
        ValidateStrings(response.MissingEvidence, 30, 300, "$.missingEvidence", errors);
        ValidateStrings(response.PriorityQuestions, 10, 500, "$.priorityQuestions", errors);

        if (response.QualityCategories is null)
            Add(errors, "evaluation.array.required", "$.qualityCategories", "Quality categories are required.");
        if (response.FitDimensions is null)
            Add(errors, "evaluation.array.required", "$.fitDimensions", "Fit dimensions are required.");
        if (response.Risks is null)
            Add(errors, "evaluation.array.required", "$.risks", "Risk signals are required, even when empty.");

        if (risks.Count > FounderEvaluationScorecard.RiskPenalties.Count)
        {
            Add(errors, "evaluation.risk.count", "$.risks", "Too many risk signals were returned.");
        }

        foreach (IGrouping<string, ModelRiskSignal> group in risks.GroupBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!FounderEvaluationScorecard.RiskPenalties.ContainsKey(group.Key))
            {
                Add(errors, "evaluation.risk.unknown", "$.risks", "An unsupported risk key was returned.");
            }
            if (group.Count() != 1)
            {
                Add(errors, "evaluation.risk.duplicate", "$.risks", "A risk key was returned more than once.");
            }
        }

        foreach ((ModelRiskSignal risk, int index) in risks.Select((item, index) => (item, index)))
        {
            string path = $"$.risks[{index}]";
            ValidateConfidence(risk.Confidence, path, errors);
            ValidateStrings(risk.Evidence, 8, 500, $"{path}.evidence", errors);
            ValidateBoundedText(risk.Explanation, 1_000, $"{path}.explanation", errors);
            if (risk.Evidence.Count == 0)
            {
                Add(errors, "evaluation.risk.evidenceRequired", $"{path}.evidence", "A scoring risk requires profile evidence.");
            }
        }

        ValidateInvitationShape(response.Invitation, errors);
        List<GroundingItem> groundingItems = BuildGroundingItems(quality, fit, risks, response.Invitation);
        FounderGroundingResult grounded = FounderEvidenceGroundingValidator.Validate(request, groundingItems);
        errors.AddRange(grounded.Errors);
        return new(errors.Count == 0, errors, grounded.Coverage);
    }

    private static List<GroundingItem> BuildGroundingItems(
        IReadOnlyList<ModelScoreItem> quality,
        IReadOnlyList<ModelScoreItem> fit,
        IReadOnlyList<ModelRiskSignal> risks,
        ModelInvitationResponse? invitation)
    {
        var items = new List<GroundingItem>();
        foreach ((ModelScoreItem item, int index) in quality.Select((item, index) => (item, index)))
        {
            items.AddRange(item.Evidence.Select(evidence => new GroundingItem($"$.qualityCategories[{index}].evidence", evidence, item.Score > 0)));
        }
        foreach ((ModelScoreItem item, int index) in fit.Select((item, index) => (item, index)))
        {
            items.AddRange(item.Evidence.Select(evidence => new GroundingItem($"$.fitDimensions[{index}].evidence", evidence, item.Score > 0)));
        }
        foreach ((ModelRiskSignal item, int index) in risks.Select((item, index) => (item, index)))
        {
            items.AddRange(item.Evidence.Select(evidence => new GroundingItem($"$.risks[{index}].evidence", evidence, true)));
        }
        if (invitation?.CandidateFactsUsed is not null)
            items.AddRange(invitation.CandidateFactsUsed.Select(value => new GroundingItem("$.invitation.candidateFactsUsed", value, true)));
        return items;
    }

    private static void ValidateExactScores(
        IReadOnlyList<ModelScoreItem> actual,
        IReadOnlyList<FounderScoreDefinition> expected,
        string path,
        ICollection<FounderEvaluationValidationError> errors)
    {
        Dictionary<string, FounderScoreDefinition> definitions = expected.ToDictionary(item => item.Key, StringComparer.Ordinal);
        if (actual.Count != expected.Count)
        {
            Add(errors, "evaluation.score.count", path, "The response does not contain the exact required score items.");
        }

        foreach (IGrouping<string, ModelScoreItem> group in actual.GroupBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(group.Key, out FounderScoreDefinition? definition))
            {
                Add(errors, "evaluation.score.unknownKey", path, "The response contains an unsupported score key.");
                continue;
            }
            if (group.Count() != 1)
            {
                Add(errors, "evaluation.score.duplicateKey", path, "A required score key occurs more than once.");
            }

            ModelScoreItem item = group.First();
            if (item.Score < 0 || item.Score > definition.Maximum)
            {
                Add(errors, "evaluation.score.outOfRange", path, "A category score is outside its exact bound.");
            }
            ValidateConfidence(item.Confidence, path, errors);
            ValidateStrings(item.Evidence, 8, 500, $"{path}.evidence", errors);
            ValidateStrings(item.MissingEvidence, 12, 200, $"{path}.missingEvidence", errors);
            ValidateBoundedText(item.Explanation, 1_000, $"{path}.explanation", errors);
            if (item.Score > 0 && item.Evidence.Count == 0)
            {
                Add(errors, "evaluation.score.evidenceRequired", $"{path}.evidence", "A positive score requires profile evidence.");
            }
        }

        foreach (FounderScoreDefinition missing in expected.Where(item => !actual.Any(actualItem => string.Equals(actualItem.Key, item.Key, StringComparison.Ordinal))))
        {
            Add(errors, "evaluation.score.missingKey", path, $"The required key '{missing.Key}' is absent.");
        }
    }

    private static void ValidateInvitationShape(ModelInvitationResponse invitation, ICollection<FounderEvaluationValidationError> errors)
    {
        if (invitation is null)
        {
            Add(errors, "evaluation.invitation.required", "$.invitation", "Invitation output is required.");
            return;
        }
        ValidateBoundedText(invitation.ShortDraft, 5_000, "$.invitation.shortDraft", errors);
        ValidateBoundedText(invitation.DetailedDraft, 5_000, "$.invitation.detailedDraft", errors);
        ValidateStrings(invitation.CandidateFactsUsed, 4, 500, "$.invitation.candidateFactsUsed", errors);
        ValidateStrings(invitation.PersonaStrengthsUsed, 4, 300, "$.invitation.personaStrengthsUsed", errors);
        ValidateBoundedText(invitation.ConversationTopic, 500, "$.invitation.conversationTopic", errors);
        ValidateConfidence(invitation.Confidence, "$.invitation", errors);
    }

    private static void ValidateConfidence(decimal value, string path, ICollection<FounderEvaluationValidationError> errors)
    {
        if (value is < 0 or > 1)
        {
            Add(errors, "evaluation.confidence.outOfRange", path, "Confidence must be between zero and one.");
        }
    }

    private static void ValidateStrings(
        IReadOnlyList<string>? values,
        int maximumItems,
        int maximumLength,
        string path,
        ICollection<FounderEvaluationValidationError> errors)
    {
        if (values is null || values.Count > maximumItems)
        {
            Add(errors, "evaluation.array.bounds", path, "An evaluator array is outside its supported bound.");
            return;
        }
        foreach (string value in values)
        {
            ValidateBoundedText(value, maximumLength, path, errors);
        }
    }

    private static void ValidateBoundedText(
        string? value,
        int maximumLength,
        string path,
        ICollection<FounderEvaluationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl) || Html.IsMatch(value))
        {
            Add(errors, "evaluation.text.invalid", path, "Evaluator text is empty, oversized, contains control characters, or contains HTML.");
        }
    }

    private static void Add(
        ICollection<FounderEvaluationValidationError> errors,
        string code,
        string path,
        string message) => errors.Add(new(code, path, message));
}

/// <summary>One output text item that must trace to the evaluator input.</summary>
public sealed record GroundingItem(string Path, string Text, bool Required);

/// <summary>Grounding errors and deterministic coverage ratio.</summary>
public sealed record FounderGroundingResult(
    IReadOnlyList<FounderEvaluationValidationError> Errors,
    decimal Coverage);

/// <summary>Conservative substring/token/numeric grounding against only supplied evaluator input.</summary>
public sealed partial class FounderEvidenceGroundingValidator
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "for", "from", "has", "have", "i", "in", "is", "it", "of", "on", "or", "that", "the", "their", "they", "this", "to", "was", "with",
    };

    /// <summary>Validates evidence/facts and rejects fabricated numbers or protected markers.</summary>
    public static FounderGroundingResult Validate(FounderEvaluationRequest request, IReadOnlyList<GroundingItem> items)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        string corpus = Normalize(FounderProfileCanonicalizer.Canonicalize(request.Input));
        HashSet<string> corpusTokens = Tokenize(corpus);
        HashSet<string> corpusNumbers = NumberRegex().Matches(corpus).Cast<Match>().Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var errors = new List<FounderEvaluationValidationError>();
        var groundedCount = 0;
        foreach (GroundingItem item in items)
        {
            string normalized = Normalize(item.Text);
            bool protectedMarker = request.ProtectedMarkers.Any(marker => marker.Length > 0 && normalized.Contains(Normalize(marker), StringComparison.Ordinal));
            bool numbersGrounded = NumberRegex().Matches(normalized).Cast<Match>().All(match => corpusNumbers.Contains(match.Value));
            HashSet<string> tokens = Tokenize(normalized);
            int matching = tokens.Count(corpusTokens.Contains);
            decimal tokenCoverage = tokens.Count == 0 ? 0m : (decimal)matching / tokens.Count;
            bool traceable = normalized.Length >= 3
                && (corpus.Contains(normalized, StringComparison.Ordinal)
                    || tokens.Count >= 2 && tokenCoverage >= 0.60m);
            if (traceable && numbersGrounded && !protectedMarker)
            {
                groundedCount++;
                continue;
            }

            string code = protectedMarker
                ? "evaluation.evidence.protectedMarker"
                : !numbersGrounded
                    ? "evaluation.evidence.fabricatedNumber"
                    : "evaluation.evidence.notGrounded";
            errors.Add(new(code, item.Path, "An evidence item is not traceable to the supplied protected-attribute-free profile."));
        }

        int required = items.Count(item => item.Required);
        decimal coverage = required == 0 ? 0m : Math.Round((decimal)groundedCount / required, 4, MidpointRounding.AwayFromZero);
        return new(errors, Math.Clamp(coverage, 0m, 1m));
    }

    /// <summary>Normalizes text for conservative matching.</summary>
    public static string Normalize(string value)
    {
        string decomposed = FounderProfileCanonicalizer.NormalizeText(value).Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Creates meaningful normalized tokens.</summary>
    public static HashSet<string> Tokenize(string value) => Normalize(value)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length >= 2 && !StopWords.Contains(token))
        .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?%?\b", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NumberRegex();
}
