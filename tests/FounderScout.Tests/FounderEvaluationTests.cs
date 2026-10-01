using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Ai;

namespace FounderScout.Tests;

internal sealed class FounderEvaluationTests
{
    [Test]
    public void CacheKeyIsStableAndExcludesLocalCorrelationIds()
    {
        FounderEvaluationRequest first = CreateRequest();
        FounderEvaluationRequest second = first with
        {
            CandidateId = Guid.NewGuid(),
            SnapshotId = Guid.NewGuid(),
            ValidationFeedback = ["repair-only"],
        };

        Assert.That(second.CalculateInputHash(), Is.EqualTo(first.CalculateInputHash()));
    }

    [Test]
    public void CacheKeyChangesForEveryBehaviorVersionFamily()
    {
        FounderEvaluationRequest request = CreateRequest();
        string original = request.CalculateInputHash();
        FounderEvaluationRequest[] changed =
        [
            request with { Input = request.Input with { Problem = "Retailers lose time reconciling inventory." } },
            request with { ScorecardVersion = "yc-scorecard-v2" },
            request with { ScorecardContentHash = new('a', 64) },
            request with { PromptVersion = "founder-evaluation-prompt-2.0" },
            request with { PromptContentHash = new('b', 64) },
            request with { EvaluatorSchemaVersion = "founder-evaluation-response-2.0" },
            request with { Persona = request.Persona with { SchemaVersion = "2.0" } },
            request with { PersonaContentHash = new('c', 64) },
            request with { ConfigurationPolicyHash = new('d', 64) },
            request with { Provider = "AzureOpenAI" },
            request with { ModelOrDeployment = "different-deployment" },
            request with { ProviderPolicyVersion = "responses-provider-2.0" },
            request with { RequestedLanguage = "Spanish" },
            request with { RequestedTone = "Concise" },
            request with { MaximumShortDraftCharacters = 320 },
            request with { MaximumDetailedDraftCharacters = 950 },
            request with { MaximumOutputTokens = 5_000 },
            request with { ActivityScoreAtRequest = 80m },
        ];

        Assert.That(changed.Select(item => item.CalculateInputHash()),
            Has.All.Not.EqualTo(original));
    }

    [Test]
    public void StrictSchemaRequiresAllFieldsAndRejectsAdditionalProperties()
    {
        using JsonDocument schema = JsonDocument.Parse(FounderEvaluationJsonSchema.Json);
        JsonElement root = schema.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("required").GetArrayLength(), Is.EqualTo(11));
            Assert.That(root.GetProperty("properties").GetProperty("qualityCategories").GetProperty("minItems").GetInt32(), Is.EqualTo(9));
            Assert.That(root.GetProperty("properties").GetProperty("fitDimensions").GetProperty("minItems").GetInt32(), Is.EqualTo(5));
            Assert.That(root.GetRawText(), Does.Not.Contain("chainOfThought").IgnoreCase);
        });
    }

    [Test]
    public void ValidationRequiresExactKeysBoundsEvidenceAndExplicitUnknowns()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse valid = CreateResponse(request);
        ModelScoreItem first = valid.QualityCategories[0];
        ModelEvaluationResponse invalid = valid with
        {
            QualityCategories = valid.QualityCategories.Skip(1)
                .Append(first with { Key = "unknownCategory", Score = 99, Evidence = [] })
                .ToArray(),
            MissingEvidence = [],
        };

        FounderEvaluationValidationResult result = FounderEvaluationValidator.Validate(request, invalid);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("evaluation.score.unknownKey"));
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("evaluation.score.missingKey"));
        });
    }

    [Test]
    public void ValidationRejectsNullCollectionsWithoutThrowing()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse response = CreateResponse(request) with
        {
            QualityCategories = null!,
            FitDimensions = null!,
            Risks = null!,
        };

        FounderEvaluationValidationResult result = FounderEvaluationValidator.Validate(request, response);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Count(error => error.Code == "evaluation.array.required"), Is.EqualTo(3));
        });
    }

    [Test]
    public void DeterministicScoringSeparatesArithmeticRiskConfidenceActivityAndPriority()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse response = CreateResponse(request) with
        {
            QualityCategories = FounderEvaluationScorecard.Quality.Select(definition =>
                new ModelScoreItem(definition.Key, definition.Maximum, [KnownFact], "Grounded.", [], 0.8m)).ToArray(),
            FitDimensions = FounderEvaluationScorecard.Fit.Select(definition =>
                new ModelScoreItem(definition.Key, definition.Maximum, [KnownFact], "Grounded.", [], 0.8m)).ToArray(),
            Risks = [new("unpaidDeveloperRisk", [KnownFact], "Grounded risk.", 0.8m)],
        };

        CandidateScoreResult result = new CandidateScoreCalculator().Calculate(new(
            response.QualityCategories,
            response.FitDimensions,
            response.Risks,
            1m,
            1m,
            Now.AddDays(-2),
            Now,
            Priority,
            Ranking,
            FounderScoutActivitySettings.Default,
            100m));

        Assert.Multiple(() =>
        {
            Assert.That(result.BaseScore, Is.EqualTo(100m));
            Assert.That(result.FounderQualityScore, Is.EqualTo(100m));
            Assert.That(result.OurFitScore, Is.EqualTo(100m));
            Assert.That(result.RiskPenalty, Is.EqualTo(20m));
            Assert.That(result.AdjustedScore, Is.EqualTo(80m));
            Assert.That(result.OverallConfidence, Is.EqualTo(0.86m));
            Assert.That(result.ActivityScore, Is.EqualTo(100m));
            Assert.That(result.InvitationPriority, Is.EqualTo(77.90m));
            Assert.That(result.Recommendation, Is.EqualTo(CandidateRecommendation.ExploratoryCall));
        });
    }

    [Test]
    public void FounderQualityNormalizationExcludesCtoFit()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse response = CreateResponse(request);
        ModelScoreItem[] quality = response.QualityCategories
            .Select(item => item with { Score = item.Key == "ctoFit" ? 10 : 0 })
            .ToArray();

        CandidateScoreResult result = Calculate(quality, response.FitDimensions, [], 1m);

        Assert.Multiple(() =>
        {
            Assert.That(result.BaseScore, Is.EqualTo(10m));
            Assert.That(result.FounderQualityScore, Is.Zero);
        });
    }

    [Test]
    public void RiskWithoutEvidenceIsNotAppliedByCalculator()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse response = CreateResponse(request);
        CandidateScoreResult result = Calculate(
            response.QualityCategories,
            response.FitDimensions,
            [new("unpaidDeveloperRisk", [], "Unknown rather than negative.", 0.2m)],
            1m);

        Assert.That(result.RiskPenalty, Is.Zero);
    }

    [Test]
    public void LowConfidenceForcesManualReview()
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelEvaluationResponse response = CreateResponse(request);
        CandidateScoreResult result = Calculate(
            response.QualityCategories.Select(item => item with { Confidence = 0.1m }).ToArray(),
            response.FitDimensions.Select(item => item with { Confidence = 0.1m }).ToArray(),
            [],
            profileCompleteness: 0.1m);

        Assert.That(result.Recommendation, Is.EqualTo(CandidateRecommendation.ManualReview));
    }

    [TestCase(-2, 100)]
    [TestCase(-20, 80)]
    [TestCase(-60, 55)]
    [TestCase(-180, 20)]
    public void ActivityUsesConfiguredUtcBuckets(int dayOffset, int expected)
    {
        decimal actual = CandidateScoreCalculator.CalculateActivity(
            Now.AddDays(dayOffset),
            Now,
            FounderScoutActivitySettings.Default);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void EvidenceValidationRejectsFabricatedNumbersAndProtectedMarkers()
    {
        FounderEvaluationRequest request = CreateRequest() with { ProtectedMarkers = ["private-marker"] };
        FounderGroundingResult result = FounderEvidenceGroundingValidator.Validate(request,
        [
            new("$.fact", "Completed 999 customer interviews.", true),
            new("$.fact", "private-marker customer operations", true),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("evaluation.evidence.fabricatedNumber"));
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("evaluation.evidence.protectedMarker"));
        });
    }

    [TestCase("length", "invitation.short.length")]
    [TestCase("generic", "invitation.genericOnly")]
    [TestCase("number", "invitation.claim.fabricatedNumber")]
    [TestCase("automation", "invitation.automationMention")]
    [TestCase("protected", "invitation.protectedAttribute")]
    [TestCase("commitment", "invitation.commitmentClaim")]
    [TestCase("similarity", "invitation.similarRecentDraft")]
    public void InvitationValidationRejectsUnsafeOrUnhelpfulDrafts(string variant, string expectedCode)
    {
        FounderEvaluationRequest request = CreateRequest();
        ModelInvitationResponse invitation = CreateResponse(request).Invitation;
        IReadOnlyList<RecentInvitationDraft> recent = [];
        invitation = variant switch
        {
            "length" => invitation with { ShortDraft = new('x', request.MaximumShortDraftCharacters + 1) },
            "generic" => invitation with { ShortDraft = "Hello, let's connect.", DetailedDraft = "Hello, let's connect for a chat." },
            "number" => invitation with { CandidateFactsUsed = ["Completed 999 customer interviews."] },
            "automation" => invitation with { DetailedDraft = invitation.DetailedDraft + " Automated analysis helped." },
            "protected" => invitation with { DetailedDraft = invitation.DetailedDraft + " Your age stood out." },
            "commitment" => invitation with { DetailedDraft = invitation.DetailedDraft + " I will join and build for free." },
            "similarity" => invitation,
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        if (variant == "similarity")
        {
            recent = [new(Guid.NewGuid(), invitation.ShortDraft, invitation.DetailedDraft, new('0', 64))];
        }

        InvitationDraftValidationResult result = new InvitationDraftValidator().Validate(request, invitation, recent, 0.85m);

        Assert.That(result.ErrorCodes, Does.Contain(expectedCode));
    }

    [Test]
    public async Task DeterministicProviderRepairsInvalidSchemaOnce()
    {
        FounderEvaluationRequest request = CreateRequest() with
        {
            Input = CreateRequest().Input with { SourceProfileKey = "invalid-schema-strong" },
        };
        var provider = new DeterministicFounderEvaluationModelClient();

        FounderModelException first = Assert.ThrowsAsync<FounderModelException>(async () =>
            await provider.EvaluateAsync(request, CancellationToken.None))!;
        ModelEvaluationResponse repaired = await provider.EvaluateAsync(
            request with { ValidationFeedback = [first.Code] },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Kind, Is.EqualTo(FounderModelFailureKind.InvalidStructuredOutput));
            Assert.That(repaired.SchemaVersion, Is.EqualTo("1.0"));
        });
    }

    [Test]
    public void MissingProviderSecretFailsPermanentlyWithoutLeakingSecretMaterial()
    {
        FounderScoutAiSettings settings = new(
            "OpenAI", "https://api.openai.com/v1/", "configured-model", "secret://founder-scout/api-key", 30);

        FounderModelException exception = Assert.Throws<FounderModelException>(() =>
            FounderEvaluationModelClientHost.Create(settings, null))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Kind, Is.EqualTo(FounderModelFailureKind.Permanent));
            Assert.That(exception.Code, Is.EqualTo("analysis.provider.secretMissing"));
            Assert.That(exception.ToString(), Does.Not.Contain("secret://"));
        });
    }

    [Test]
    public async Task PromptAssetsContainRequiredEvidencePrivacyScoringAndDraftPolicies()
    {
        FounderEvaluationPrompt prompt = await FounderEvaluationPromptCatalog.LoadAsync(
            Path.Combine(FindRepositoryRoot(), "agents", "FounderScout", "prompts"));

        Assert.Multiple(() =>
        {
            Assert.That(prompt.SystemPrompt, Does.Contain("Unknown information remains unknown"));
            Assert.That(prompt.SystemPrompt, Does.Contain("Do not browse"));
            Assert.That(prompt.SystemPrompt, Does.Contain("protected/irrelevant personal attribute"));
            Assert.That(prompt.SystemPrompt, Does.Contain("founder execution quality from fit"));
            Assert.That(prompt.SystemPrompt, Does.Contain("unpaid-developer"));
            Assert.That(prompt.SystemPrompt, Does.Contain("Do not overreward idea novelty"));
            Assert.That(prompt.SystemPrompt, Does.Contain("Return only JSON"));
            Assert.That(prompt.SystemPrompt, Does.Contain("never say the local founder has decided to join").IgnoreCase);
            Assert.That(prompt.SystemPrompt, Does.Contain("Do not instruct any system to send"));
        });
    }

    [Test]
    public void ExplicitLocalFounderContextIsIncludedInTheProviderPayload()
    {
        FounderEvaluationRequest baseline = CreateRequest();
        FounderEvaluationRequest request = baseline with
        {
            Persona = baseline.Persona with
            {
                AdditionalContext = "Prefer complementary go-to-market leadership; keep uncertainty explicit.",
            },
        };

        using JsonDocument payload = JsonDocument.Parse(request.CreateUserPayload());

        Assert.That(
            payload.RootElement.GetProperty("persona").GetProperty("additionalContext").GetString(),
            Is.EqualTo("Prefer complementary go-to-market leadership; keep uncertainty explicit."));
    }

    [Test]
    [Explicit("Opt-in live provider test. Set HBA_FOUNDER_SCOUT_LIVE_API_KEY, HBA_FOUNDER_SCOUT_LIVE_MODEL, and optionally HBA_FOUNDER_SCOUT_LIVE_ENDPOINT.")]
    public async Task SyntheticLiveProviderEvaluationIsOptIn()
    {
        string key = Environment.GetEnvironmentVariable("HBA_FOUNDER_SCOUT_LIVE_API_KEY")
            ?? throw new AssertionException("HBA_FOUNDER_SCOUT_LIVE_API_KEY is required for the explicit live test.");
        string model = Environment.GetEnvironmentVariable("HBA_FOUNDER_SCOUT_LIVE_MODEL")
            ?? throw new AssertionException("HBA_FOUNDER_SCOUT_LIVE_MODEL is required for the explicit live test.");
        string endpoint = Environment.GetEnvironmentVariable("HBA_FOUNDER_SCOUT_LIVE_ENDPOINT")
            ?? "https://api.openai.com/v1/";
        FounderScoutAiSettings settings = new(
            "OpenAI", endpoint, model, "secret://founder-scout/live-test", 120, MaxOutputTokens: 6_000);
        await using FounderEvaluationModelClientHost host = FounderEvaluationModelClientHost.Create(settings, key);

        ModelEvaluationResponse response = await host.Client.EvaluateAsync(
            CreateRequest() with { ModelOrDeployment = model },
            CancellationToken.None);

        Assert.That(FounderEvaluationValidator.Validate(CreateRequest() with { ModelOrDeployment = model }, response).IsValid, Is.True);
    }

    private static CandidateScoreResult Calculate(
        IReadOnlyList<ModelScoreItem> quality,
        IReadOnlyList<ModelScoreItem> fit,
        IReadOnlyList<ModelRiskSignal> risks,
        decimal profileCompleteness) => new CandidateScoreCalculator().Calculate(new(
            quality,
            fit,
            risks,
            profileCompleteness,
            1m,
            Now.AddDays(-2),
            Now,
            Priority,
            Ranking,
            FounderScoutActivitySettings.Default,
            100m));

    private static FounderEvaluationRequest CreateRequest()
    {
        var input = new FounderEvaluationInput(
            FounderEvaluationInput.CurrentSchemaVersion,
            "strong-synthetic",
            "Synthetic Founder",
            "Remote US Central",
            TechnicalProfileStatus.NonTechnical,
            FounderCommitmentStatus.FullTime,
            IdeaCommitmentStatus.Committed,
            [],
            "Builds practical tools for local retailers.",
            KnownFact,
            "A supplier coordination workflow.",
            "Local retailers lose time coordinating suppliers.",
            "Independent local retailers.",
            "A shared supplier workflow.",
            ["Completed 12 customer interviews."],
            ["Software architecture"],
            "Technical co-founder and CTO",
            FounderCommitmentStatus.FullTime,
            "Equal founder-level partnership.",
            ["local commerce", "operations"],
            [new("background", "structured.background", KnownFact)]);
        var persona = new FounderScoutPersonaSettings(
            "1.0",
            "founder-persona/synthetic",
            "Synthetic Local Founder",
            "Technical Co-Founder / CTO",
            ["Software architecture", "Product engineering"],
            ["Complementary business leadership"],
            "Direct, thoughtful, founder-to-founder",
            ["Do not imply a commitment to join."]);
        return new(
            input,
            1m,
            Now.AddDays(-2),
            100m,
            FounderEvaluationScorecard.Version,
            "{\"version\":\"yc-scorecard-v1\"}",
            new('1', 64),
            FounderEvaluationPromptCatalog.CurrentPromptVersion,
            "Synthetic system prompt",
            new('2', 64),
            FounderEvaluationRequest.CurrentEvaluatorSchemaVersion,
            persona,
            new('3', 64),
            new('4', 64),
            "DiagnosticFake",
            "deterministic-v1",
            "responses-provider-1.0",
            "English",
            persona.MessageTone,
            350,
            1_000,
            6_000,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            []);
    }

    private static ModelEvaluationResponse CreateResponse(FounderEvaluationRequest request)
    {
        ModelScoreItem[] quality = FounderEvaluationScorecard.Quality.Select(definition =>
            new ModelScoreItem(definition.Key, Math.Max(1, definition.Maximum / 2), [KnownFact], "Grounded assessment.", [], 0.8m)).ToArray();
        ModelScoreItem[] fit = FounderEvaluationScorecard.Fit.Select(definition =>
            new ModelScoreItem(definition.Key, Math.Max(1, definition.Maximum / 2), [KnownFact], "Grounded fit.", [], 0.8m)).ToArray();
        string shortDraft = $"Hi {request.Input.DisplayName}, your work leading customer operations for local retailers stood out. I bring software architecture; I would enjoy a founder-to-founder conversation to compare notes.";
        string detailedDraft = $"Hi {request.Input.DisplayName}, your experience leading customer operations for local retailers stood out. I bring complementary software architecture experience, and I would enjoy a founder-to-founder conversation. Would you be open to comparing notes on supplier coordination?";
        return new(
            "1.0",
            "A synthetic founder profile grounded in supplied evidence.",
            quality,
            fit,
            [],
            [KnownFact],
            [],
            ["Revenue is not provided."],
            ["What customer evidence is most important next?"],
            "The bounded recommendation uses only supplied evidence.",
            new(shortDraft, detailedDraft, [KnownFact], ["Software architecture"], "Supplier coordination", 0.8m));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "HomeBusinessAssistant.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private const string KnownFact = "Led customer operations for local retailers.";
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 18, 0, 0, TimeSpan.Zero);
    private static readonly FounderScoutPrioritySettings Priority = new(0.55m, 0.25m, 0.15m, 0.05m, 100m);
    private static readonly FounderScoutRankingSettings Ranking = new(82, 72, 62, 0.60m, 30, 15, 15);
}
