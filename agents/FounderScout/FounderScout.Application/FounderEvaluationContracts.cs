using System.Text.Json;
using System.Text.Json.Serialization;

namespace FounderScout.Application;

/// <summary>Stable failure categories exposed by a provider-neutral model client.</summary>
public enum FounderModelFailureKind
{
    /// <summary>A network, timeout, throttling, or provider availability failure may be retried.</summary>
    Transient = 0,
    /// <summary>Authentication, configuration, deployment, or unsupported-model failure requires attention.</summary>
    Permanent = 1,
    /// <summary>The provider returned content that did not satisfy the strict response contract.</summary>
    InvalidStructuredOutput = 2,
}

/// <summary>Bounded provider exception without secret-bearing request or response content.</summary>
public sealed class FounderModelException : Exception
{
    /// <summary>Creates a classified provider exception.</summary>
    public FounderModelException(
        FounderModelFailureKind kind,
        string code,
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128)
        {
            throw new ArgumentException("A bounded model failure code is required.", nameof(code));
        }

        Kind = kind;
        Code = code;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the retry/permanence classification.</summary>
    public FounderModelFailureKind Kind { get; }
    /// <summary>Gets the stable safe failure code.</summary>
    public string Code { get; }
    /// <summary>Gets a provider-supplied retry delay when present.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>One exact bounded score item returned by the model.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelScoreItem(
    string Key,
    int Score,
    IReadOnlyList<string> Evidence,
    string Explanation,
    IReadOnlyList<string> MissingEvidence,
    decimal Confidence);

/// <summary>One known evidence-backed risk proposed by the model.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelRiskSignal(
    string Key,
    IReadOnlyList<string> Evidence,
    string Explanation,
    decimal Confidence);

/// <summary>Two human-review-only introduction drafts and their grounding metadata.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelInvitationResponse(
    string ShortDraft,
    string DetailedDraft,
    IReadOnlyList<string> CandidateFactsUsed,
    IReadOnlyList<string> PersonaStrengthsUsed,
    string ConversationTopic,
    decimal Confidence);

/// <summary>Strict provider-neutral response; no model-provided total is accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelEvaluationResponse(
    string SchemaVersion,
    string ProfileSummary,
    IReadOnlyList<ModelScoreItem> QualityCategories,
    IReadOnlyList<ModelScoreItem> FitDimensions,
    IReadOnlyList<ModelRiskSignal> Risks,
    IReadOnlyList<string> PositiveSignals,
    IReadOnlyList<string> RedFlags,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> PriorityQuestions,
    string RecommendationRationale,
    ModelInvitationResponse Invitation);

/// <summary>All non-secret, behavior-affecting input for one model evaluation.</summary>
public sealed record FounderEvaluationRequest(
    FounderEvaluationInput Input,
    decimal ProfileCompleteness,
    DateTimeOffset? LastActivityAtUtc,
    decimal ActivityScoreAtRequest,
    string ScorecardVersion,
    string ScorecardContent,
    string ScorecardContentHash,
    string PromptVersion,
    string SystemPrompt,
    string PromptContentHash,
    string EvaluatorSchemaVersion,
    FounderScoutPersonaSettings Persona,
    string PersonaContentHash,
    string ConfigurationPolicyHash,
    string Provider,
    string ModelOrDeployment,
    string ProviderPolicyVersion,
    string RequestedLanguage,
    string RequestedTone,
    int MaximumShortDraftCharacters,
    int MaximumDetailedDraftCharacters,
    int MaximumOutputTokens,
    Guid CandidateId,
    Guid SnapshotId,
    IReadOnlyList<string> ProtectedMarkers,
    IReadOnlyList<string> ValidationFeedback)
{
    /// <summary>Current strict evaluator response schema.</summary>
    public const string CurrentEvaluatorSchemaVersion = "founder-evaluation-response-1.0";

    /// <summary>Calculates the cache identity from behavior-affecting, non-secret values.</summary>
    public string CalculateInputHash() => FounderProfileCanonicalizer.HashCanonical(new
    {
        Input,
        ProfileCompleteness,
        LastActivityAtUtc,
        ActivityScoreAtRequest,
        ScorecardVersion,
        ScorecardContentHash,
        PromptVersion,
        PromptContentHash,
        EvaluatorSchemaVersion,
        personaVersion = Persona.SchemaVersion,
        Persona.Reference,
        PersonaContentHash,
        ConfigurationPolicyHash,
        Provider,
        ModelOrDeployment,
        ProviderPolicyVersion,
        RequestedLanguage,
        RequestedTone,
        MaximumShortDraftCharacters,
        MaximumDetailedDraftCharacters,
        MaximumOutputTokens,
    });

    /// <summary>Renders the bounded user payload sent to the provider.</summary>
    public string CreateUserPayload()
    {
        using JsonDocument scorecard = JsonDocument.Parse(ScorecardContent);
        return FounderProfileCanonicalizer.Canonicalize(new
        {
            evaluatorSchemaVersion = EvaluatorSchemaVersion,
            profile = Input,
            scorecard = scorecard.RootElement,
            persona = Persona,
            requestedLanguage = RequestedLanguage,
            requestedTone = RequestedTone,
            maximumShortDraftCharacters = MaximumShortDraftCharacters,
            maximumDetailedDraftCharacters = MaximumDetailedDraftCharacters,
            validationFeedback = ValidationFeedback,
        });
    }
}

/// <summary>Provider-neutral structured Founder Scout model boundary.</summary>
public interface IFounderEvaluationModelClient
{
    /// <summary>Evaluates one protected-attribute-free request.</summary>
    Task<ModelEvaluationResponse> EvaluateAsync(
        FounderEvaluationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>One scorecard dimension with an exact key and maximum.</summary>
public sealed record FounderScoreDefinition(string Key, string DisplayName, int Maximum);

/// <summary>Versioned scoring definitions authoritative for validation and C# arithmetic.</summary>
public static class FounderEvaluationScorecard
{
    /// <summary>Current scorecard version.</summary>
    public const string Version = "yc-scorecard-v1";

    /// <summary>Exact quality categories totaling 100.</summary>
    public static IReadOnlyList<FounderScoreDefinition> Quality { get; } =
    [
        new("founderExecution", "Founder execution quality", 20),
        new("commitmentAndPosture", "Commitment and co-founder posture", 15),
        new("tractionAndValidation", "Traction and validation", 15),
        new("marketPotential", "Market potential", 15),
        new("gtmAndDomainAdvantage", "GTM and domain advantage", 10),
        new("ctoFit", "CTO fit", 10),
        new("ideaClarity", "Idea and problem clarity", 5),
        new("moatPotential", "Moat potential", 5),
        new("technicalFeasibility", "Technical feasibility", 5),
    ];

    /// <summary>Exact local-founder fit dimensions totaling 100.</summary>
    public static IReadOnlyList<FounderScoreDefinition> Fit { get; } =
    [
        new("complementarySkillsOwnership", "Complementary skills/ownership", 30),
        new("founderEquityPosture", "Founder-level/equity posture", 25),
        new("technicalDomainAlignment", "Technical/domain alignment", 20),
        new("timingLocationCompatibility", "Timing/location/working compatibility", 15),
        new("ctoWorkloadRealism", "CTO workload realism", 10),
    ];

    /// <summary>Known risk keys and maximum penalties.</summary>
    public static IReadOnlyDictionary<string, int> RiskPenalties { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["unpaidDeveloperRisk"] = 20,
            ["founderReservesMostEquity"] = 15,
            ["overscopedTechnicalBuild"] = 10,
            ["noCustomerAccessPath"] = 10,
            ["ctoExpectedToOwnEverything"] = 10,
            ["indefinitePartTimeCommitment"] = 10,
            ["unavailableExternalDependency"] = 10,
        };
}

/// <summary>Stable strict JSON Schema supplied to the Responses API.</summary>
public static class FounderEvaluationJsonSchema
{
    /// <summary>Gets the schema as canonical JSON.</summary>
    public static string Json { get; } = Build();

    private static string Build()
    {
        object StringSchema(int maximum) => new { type = "string", minLength = 1, maxLength = maximum };
        object StringArray(int maximumItems, int maximumLength) => new
        {
            type = "array",
            maxItems = maximumItems,
            items = StringSchema(maximumLength),
        };
        object ScoreItem(IReadOnlyList<FounderScoreDefinition> definitions) => new
        {
            type = "object",
            properties = new
            {
                key = new { type = "string", @enum = definitions.Select(item => item.Key).ToArray() },
                score = new { type = "integer", minimum = 0, maximum = definitions.Max(item => item.Maximum) },
                evidence = StringArray(8, 500),
                explanation = StringSchema(1_000),
                missingEvidence = StringArray(12, 200),
                confidence = new { type = "number", minimum = 0, maximum = 1 },
            },
            required = new[] { "key", "score", "evidence", "explanation", "missingEvidence", "confidence" },
            additionalProperties = false,
        };

        var schema = new
        {
            type = "object",
            properties = new
            {
                schemaVersion = new { type = "string", @enum = new[] { "1.0" } },
                profileSummary = StringSchema(2_000),
                qualityCategories = new { type = "array", minItems = 9, maxItems = 9, items = ScoreItem(FounderEvaluationScorecard.Quality) },
                fitDimensions = new { type = "array", minItems = 5, maxItems = 5, items = ScoreItem(FounderEvaluationScorecard.Fit) },
                risks = new
                {
                    type = "array",
                    maxItems = 7,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            key = new { type = "string", @enum = FounderEvaluationScorecard.RiskPenalties.Keys.ToArray() },
                            evidence = StringArray(8, 500),
                            explanation = StringSchema(1_000),
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "key", "evidence", "explanation", "confidence" },
                        additionalProperties = false,
                    },
                },
                positiveSignals = StringArray(20, 500),
                redFlags = StringArray(20, 500),
                missingEvidence = StringArray(30, 300),
                priorityQuestions = StringArray(10, 500),
                recommendationRationale = StringSchema(2_000),
                invitation = new
                {
                    type = "object",
                    properties = new
                    {
                        shortDraft = StringSchema(5_000),
                        detailedDraft = StringSchema(5_000),
                        candidateFactsUsed = StringArray(4, 500),
                        personaStrengthsUsed = StringArray(4, 300),
                        conversationTopic = StringSchema(500),
                        confidence = new { type = "number", minimum = 0, maximum = 1 },
                    },
                    required = new[] { "shortDraft", "detailedDraft", "candidateFactsUsed", "personaStrengthsUsed", "conversationTopic", "confidence" },
                    additionalProperties = false,
                },
            },
            required = new[] { "schemaVersion", "profileSummary", "qualityCategories", "fitDimensions", "risks", "positiveSignals", "redFlags", "missingEvidence", "priorityQuestions", "recommendationRationale", "invitation" },
            additionalProperties = false,
        };
        return FounderProfileCanonicalizer.Canonicalize(schema);
    }
}
