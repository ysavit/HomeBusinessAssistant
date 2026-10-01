using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Domain;

namespace FounderScout.Application;

/// <summary>Input for one bounded deep-analysis batch.</summary>
public sealed record FounderDeepAnalysisBatchRequest(
    int MaximumCandidates,
    int MaximumConcurrency,
    string WorkerId,
    Guid RunId,
    string CorrelationId,
    FounderScoutConfiguration Configuration,
    FounderEvaluationPrompt Prompt,
    Guid? CandidateId = null,
    bool ForceReanalysis = false);

/// <summary>One bounded deep-analysis progress update.</summary>
public sealed record FounderDeepAnalysisProgress(int Current, int Maximum, Guid CandidateId, string Message);

/// <summary>One bounded top-candidate summary.</summary>
public sealed record FounderAnalysisTopCandidate(Guid CandidateId, string DisplayName, decimal InvitationPriority, string Recommendation);

/// <summary>Deterministic deep-analysis metrics and bounded top results.</summary>
public sealed record FounderDeepAnalysisBatchResult(
    int Claimed,
    int Completed,
    int CacheHits,
    int Filtered,
    int ManualReview,
    int Failed,
    int ProviderRequests,
    int ProviderRetries,
    int StrongConnect,
    int Exploratory,
    int Monitor,
    int Pass,
    int InvitationsGenerated,
    int InvitationsNeedsReview,
    int ReevaluationQueued,
    bool AttentionRequired,
    string? AttentionCode,
    IReadOnlyList<FounderAnalysisTopCandidate> TopCandidates);

/// <summary>Injectable retry delay for deterministic tests.</summary>
public interface IFounderModelRetryDelay
{
    /// <summary>Waits for a bounded provider retry delay.</summary>
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Production cancellation-aware retry delay.</summary>
public sealed class FounderModelRetryDelay : IFounderModelRetryDelay
{
    /// <inheritdoc />
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}

/// <summary>Claims candidates, validates model output, calculates scores, and persists human-review-only drafts.</summary>
public sealed class FounderDeepAnalysisService
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
    private readonly IFounderScoutWorkQueue queue;
    private readonly ICandidateRepository candidates;
    private readonly IProfileSnapshotRepository snapshots;
    private readonly IScreeningRepository screenings;
    private readonly IEvaluationRepository evaluations;
    private readonly IInvitationDraftRepository invitations;
    private readonly IFounderEvaluationModelClient model;
    private readonly ICandidateScoreCalculator scoreCalculator;
    private readonly IInvitationDraftValidator draftValidator;
    private readonly IFounderModelRetryDelay retryDelay;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the deep-analysis orchestration service.</summary>
    public FounderDeepAnalysisService(
        IFounderScoutWorkQueue queue,
        ICandidateRepository candidates,
        IProfileSnapshotRepository snapshots,
        IScreeningRepository screenings,
        IEvaluationRepository evaluations,
        IInvitationDraftRepository invitations,
        IFounderEvaluationModelClient model,
        TimeProvider timeProvider,
        ICandidateScoreCalculator? scoreCalculator = null,
        IInvitationDraftValidator? draftValidator = null,
        IFounderModelRetryDelay? retryDelay = null)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        this.snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        this.screenings = screenings ?? throw new ArgumentNullException(nameof(screenings));
        this.evaluations = evaluations ?? throw new ArgumentNullException(nameof(evaluations));
        this.invitations = invitations ?? throw new ArgumentNullException(nameof(invitations));
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.scoreCalculator = scoreCalculator ?? new CandidateScoreCalculator();
        this.draftValidator = draftValidator ?? new InvitationDraftValidator();
        this.retryDelay = retryDelay ?? new FounderModelRetryDelay();
    }

    /// <summary>Processes a bounded queue with no more than the configured concurrent provider calls.</summary>
    public async ValueTask<FounderDeepAnalysisBatchResult> AnalyzeAsync(
        FounderDeepAnalysisBatchRequest request,
        Func<FounderDeepAnalysisProgress, CancellationToken, ValueTask>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        int reevaluationQueued = request.CandidateId.HasValue ? 0 : await QueueChangedInputsAsync(request, cancellationToken).ConfigureAwait(false);
        var accumulator = new AnalysisAccumulator(reevaluationQueued);
        int nextSlot = 0;
        int concurrency = Math.Min(request.MaximumConcurrency, request.MaximumCandidates);
        Task[] workers = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
        {
            string workerId = $"{request.WorkerId}-{index + 1}";
            while (Volatile.Read(ref accumulator.StopRequested) == 0)
            {
                int slot = Interlocked.Increment(ref nextSlot);
                if (slot > request.MaximumCandidates) break;
                FounderScoutAnalysisClaim? claim = request.CandidateId.HasValue
                    ? await queue.ClaimCandidateAnalysisAsync(request.CandidateId.Value, workerId, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false)
                    : await queue.ClaimPendingAnalysisAsync(workerId, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
                if (claim is null) break;
                accumulator.IncrementClaimed();
                if (progress is not null)
                {
                    await progress(new(slot - 1, request.MaximumCandidates, claim.Candidate.Id, "Evaluating one claimed candidate."), cancellationToken).ConfigureAwait(false);
                }
                await ProcessClaimAsync(request, claim, accumulator, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken)).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        if (progress is not null)
        {
            await progress(new(accumulator.Completed, request.MaximumCandidates, Guid.Empty, "Deep-analysis batch completed."), cancellationToken).ConfigureAwait(false);
        }
        return accumulator.ToResult();
    }

    private async ValueTask<int> QueueChangedInputsAsync(
        FounderDeepAnalysisBatchRequest request,
        CancellationToken cancellationToken)
    {
        CandidateStatus[] statuses =
        [
            CandidateStatus.Analyzed,
            CandidateStatus.Shortlisted,
            CandidateStatus.Monitor,
            CandidateStatus.Passed,
            CandidateStatus.QueuedForInvite,
            CandidateStatus.MessageReviewed,
            CandidateStatus.ManualReview,
        ];
        CandidatePage page = await candidates.QueryRankedAsync(
            new(statuses, null, 0, Math.Min(500, Math.Max(request.MaximumCandidates * 4, 20))),
            cancellationToken).ConfigureAwait(false);
        var queued = 0;
        foreach (Candidate candidate in page.Items)
        {
            if (!candidate.CurrentSnapshotId.HasValue || !candidate.LatestEvaluationId.HasValue) continue;
            ProfileSnapshot? snapshot = await snapshots.GetAsync(candidate.CurrentSnapshotId.Value, cancellationToken).ConfigureAwait(false);
            ScreeningDecision? screening = await screenings.GetLatestAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            EvaluationAggregate? previous = await evaluations.GetAsync(candidate.LatestEvaluationId.Value, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || screening is null || previous is null) continue;
            FounderEvaluationRequest current = CreateRequest(candidate, snapshot, screening, request, []);
            string currentHash = current.CalculateInputHash();
            if (string.Equals(currentHash, previous.Evaluation.InputHash, StringComparison.Ordinal)
                || HasMatchingBaseInputHash(previous.Evaluation.StructuredEvaluationJson, currentHash)) continue;
            _ = await candidates.TransitionAsync(new(
                candidate.Id,
                CandidateStatus.PendingAnalysis,
                "analysis.behaviorInput.changed",
                request.WorkerId,
                "A behavior-affecting evaluation input changed.",
                JsonSerializer.Serialize(new { previous = previous.Evaluation.InputHash, current = currentHash }, StrictJson),
                null,
                previous.Evaluation.Id,
                request.RunId,
                request.CorrelationId), cancellationToken).ConfigureAwait(false);
            queued++;
            if (queued >= request.MaximumCandidates) break;
        }
        return queued;
    }

    private static bool HasMatchingBaseInputHash(string structuredEvaluationJson, string currentHash)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(structuredEvaluationJson);
            return document.RootElement.TryGetProperty("request", out JsonElement request)
                && request.TryGetProperty("baseInputHash", out JsonElement baseHash)
                && string.Equals(baseHash.GetString(), currentHash, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async ValueTask ProcessClaimAsync(
        FounderDeepAnalysisBatchRequest batch,
        FounderScoutAnalysisClaim claim,
        AnalysisAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!claim.Candidate.CurrentSnapshotId.HasValue)
            {
                await ReleaseAsync(claim, batch, "analysis.snapshot.missing", retry: false, cancellationToken).ConfigureAwait(false);
                accumulator.RecordFailure(manualReview: true);
                return;
            }
            ProfileSnapshot? snapshot = await snapshots.GetAsync(claim.Candidate.CurrentSnapshotId.Value, cancellationToken).ConfigureAwait(false);
            ScreeningDecision? screening = await screenings.GetLatestAsync(claim.Candidate.Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || screening is null || screening.SnapshotId != snapshot.Id)
            {
                await ReleaseAsync(claim, batch, "analysis.screening.missing", retry: false, cancellationToken).ConfigureAwait(false);
                accumulator.RecordFailure(manualReview: true);
                return;
            }

            FounderEvaluationRequest initialRequest = CreateRequest(claim.Candidate, snapshot, screening, batch, []) with
            {
                ExplicitReanalysisId = batch.ForceReanalysis ? batch.RunId : null,
            };
            string inputHash = initialRequest.CalculateInputHash();
            EvaluationAggregate? cached = batch.ForceReanalysis
                ? null
                : await evaluations.GetCompletedByInputHashAsync(claim.Candidate.Id, inputHash, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                InvitationDraft? cachedDraft = await invitations.GetForEvaluationAsync(cached.Evaluation.Id, cancellationToken).ConfigureAwait(false);
                if (cachedDraft is null)
                {
                    ModelEvaluationResponse? storedResponse = DeserializeStoredResponse(cached.Evaluation.StructuredEvaluationJson);
                    if (storedResponse is not null)
                    {
                        cachedDraft = await PersistDraftAsync(initialRequest, cached.Evaluation.Id, storedResponse.Invitation, cancellationToken).ConfigureAwait(false);
                    }
                }
                await FinalizeAsync(claim, batch, cached.Evaluation, cachedDraft, cancellationToken).ConfigureAwait(false);
                accumulator.RecordCacheHit(claim.Candidate, cached.Evaluation, cachedDraft);
                return;
            }

            EvaluationAttemptOutcome attempt = await EvaluateWithPolicyAsync(initialRequest, batch.Configuration, accumulator, cancellationToken).ConfigureAwait(false);
            if (attempt.PermanentFailure)
            {
                await ReleaseAsync(claim, batch, attempt.ErrorCode ?? "analysis.provider.attentionRequired", retry: true, cancellationToken).ConfigureAwait(false);
                accumulator.RecordAttention(attempt.ErrorCode ?? "analysis.provider.attentionRequired");
                return;
            }
            if (attempt.Response is null)
            {
                bool retry = attempt.TransientFailure;
                await ReleaseAsync(claim, batch, attempt.ErrorCode ?? "analysis.provider.failed", retry, cancellationToken).ConfigureAwait(false);
                if (retry)
                {
                    // A provider-wide outage or rate limit applies to the whole batch. Releasing this
                    // candidate makes it claimable again, so stop before charging for it repeatedly.
                    accumulator.RecordAttention(attempt.ErrorCode ?? "analysis.provider.failed");
                }
                else
                {
                    accumulator.RecordFailure(manualReview: true);
                }
                return;
            }

            ModelEvaluationResponse response = attempt.Response;
            FounderEvaluationValidationResult validation = attempt.Validation!;
            if (!HasScorableShape(response))
            {
                await ReleaseAsync(claim, batch, "analysis.validation.invalidStructure", retry: false, cancellationToken).ConfigureAwait(false);
                accumulator.RecordFailure(manualReview: true);
                return;
            }
            FounderScoutActivitySettings activity = batch.Configuration.Activity ?? FounderScoutActivitySettings.Default;
            CandidateScoreResult scores = scoreCalculator.Calculate(new(
                response.QualityCategories,
                response.FitDimensions,
                response.Risks,
                snapshot.ExtractionCompleteness,
                validation.EvidenceCoverage,
                claim.Candidate.LastActivityAtUtc,
                timeProvider.GetUtcNow(),
                batch.Configuration.Priority,
                batch.Configuration.Ranking,
                activity,
                batch.Configuration.Priority.MaximumRiskPenalty));
            IReadOnlyList<InvitationDraft> recent = await invitations.ListRecentActiveAsync(100, cancellationToken).ConfigureAwait(false);
            InvitationDraftValidationResult draftValidation = draftValidator.Validate(
                attempt.Request,
                response.Invitation,
                recent.Select(item => new RecentInvitationDraft(item.CandidateId, item.ShortDraft, item.DetailedDraft, item.SimilarityFingerprint)).ToArray(),
                batch.Configuration.Invitation.SimilarityThreshold);
            bool evaluationValid = validation.IsValid;
            CandidateRecommendation recommendation = evaluationValid && draftValidation.IsValid
                ? scores.Recommendation
                : CandidateRecommendation.ManualReview;
            EvaluationAggregate aggregate = CreateAggregate(
                claim,
                attempt.Request,
                response,
                validation,
                draftValidation,
                scores,
                recommendation,
                attempt.ProviderAttempts,
                evaluationValid);
            await evaluations.AddAsync(aggregate, cancellationToken).ConfigureAwait(false);
            InvitationDraft draft = await PersistDraftAsync(attempt.Request, aggregate.Evaluation.Id, response.Invitation, draftValidation, cancellationToken).ConfigureAwait(false);
            await FinalizeAsync(claim, batch, aggregate.Evaluation, draft, cancellationToken).ConfigureAwait(false);
            accumulator.RecordCompleted(claim.Candidate, aggregate.Evaluation, draft);
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(claim, batch, "analysis.cancelled", retry: true, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            await ReleaseAsync(claim, batch, "analysis.candidate.failed", retry: false, cancellationToken).ConfigureAwait(false);
            accumulator.RecordFailure(manualReview: true);
        }
    }

    private async ValueTask<EvaluationAttemptOutcome> EvaluateWithPolicyAsync(
        FounderEvaluationRequest initial,
        FounderScoutConfiguration configuration,
        AnalysisAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        int maximumAttempts = Math.Clamp(configuration.Analysis.MaximumRetries, 1, 3);
        bool repairUsed = false;
        FounderEvaluationRequest request = initial;
        ModelEvaluationResponse? lastResponse = null;
        FounderEvaluationValidationResult? lastValidation = null;
        InvitationDraftValidationResult? lastDraftValidation = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            accumulator.IncrementProviderRequests();
            try
            {
                ModelEvaluationResponse response = await model.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
                FounderEvaluationValidationResult validation = FounderEvaluationValidator.Validate(request, response);
                IReadOnlyList<InvitationDraft> recent = await invitations.ListRecentActiveAsync(100, cancellationToken).ConfigureAwait(false);
                InvitationDraftValidationResult draftValidation = draftValidator.Validate(
                    request,
                    response.Invitation,
                    recent.Select(item => new RecentInvitationDraft(item.CandidateId, item.ShortDraft, item.DetailedDraft, item.SimilarityFingerprint)).ToArray(),
                    configuration.Invitation.SimilarityThreshold);
                lastResponse = response;
                lastValidation = validation;
                lastDraftValidation = draftValidation;
                if (validation.IsValid && draftValidation.IsValid)
                    return new(request, response, validation, draftValidation, attempt, false, false, false, null);
                if (!repairUsed && attempt < maximumAttempts)
                {
                    repairUsed = true;
                    accumulator.IncrementProviderRetries();
                    string[] feedback = validation.Errors.Select(error => error.Code)
                        .Concat(draftValidation.ErrorCodes)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .Take(20)
                        .ToArray();
                    request = request with { ValidationFeedback = feedback };
                    continue;
                }
                return new(request, response, validation, draftValidation, attempt, false, false, false, "analysis.validation.needsReview");
            }
            catch (FounderModelException exception) when (exception.Kind == FounderModelFailureKind.Transient)
            {
                if (attempt >= maximumAttempts)
                    return new(request, null, null, null, attempt, true, false, false, exception.Code);
                accumulator.IncrementProviderRetries();
                await retryDelay.DelayAsync(exception.RetryAfter ?? CalculateRetryDelay(initial.CalculateInputHash(), attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (FounderModelException exception) when (exception.Kind == FounderModelFailureKind.InvalidStructuredOutput)
            {
                if (repairUsed || attempt >= maximumAttempts)
                    return new(request, lastResponse, lastValidation, lastDraftValidation, attempt, false, false, true, exception.Code);
                repairUsed = true;
                accumulator.IncrementProviderRetries();
                request = request with { ValidationFeedback = [exception.Code] };
            }
            catch (FounderModelException exception)
            {
                return new(request, null, null, null, attempt, false, true, false, exception.Code);
            }
        }
        return new(request, lastResponse, lastValidation, lastDraftValidation, maximumAttempts, false, false, true, "analysis.provider.exhausted");
    }

    private FounderEvaluationRequest CreateRequest(
        Candidate candidate,
        ProfileSnapshot snapshot,
        ScreeningDecision screening,
        FounderDeepAnalysisBatchRequest batch,
        IReadOnlyList<string> feedback)
    {
        FounderEvaluationInput input = JsonSerializer.Deserialize<FounderEvaluationInput>(screening.EvaluatorInputJson, StrictJson)
            ?? throw new InvalidDataException("The persisted evaluator input is invalid.");
        FounderScoutConfiguration configuration = batch.Configuration;
        FounderScoutActivitySettings activity = configuration.Activity ?? FounderScoutActivitySettings.Default;
        string personaHash = FounderProfileCanonicalizer.HashCanonical(configuration.Persona);
        string configurationHash = FounderProfileCanonicalizer.HashCanonical(new
        {
            configuration.Ranking,
            configuration.Priority,
            configuration.Invitation,
            activity,
            provider = configuration.Ai.Provider,
            endpoint = configuration.Ai.Endpoint,
            model = configuration.Ai.Deployment,
            configuration.Ai.ApiVersion,
            configuration.Ai.MaxOutputTokens,
            configuration.Ai.Temperature,
            configuration.Ai.ReasoningEffort,
            configuration.Ai.ProviderPolicyVersion,
            riskDefinitions = FounderEvaluationScorecard.RiskPenalties,
        });
        return new(
            input,
            snapshot.ExtractionCompleteness,
            candidate.LastActivityAtUtc,
            CandidateScoreCalculator.CalculateActivity(candidate.LastActivityAtUtc, timeProvider.GetUtcNow(), activity),
            batch.Prompt.ScorecardVersion,
            batch.Prompt.ScorecardContent,
            batch.Prompt.ScorecardContentHash,
            batch.Prompt.PromptVersion,
            batch.Prompt.SystemPrompt,
            batch.Prompt.PromptContentHash,
            FounderEvaluationRequest.CurrentEvaluatorSchemaVersion,
            configuration.Persona,
            personaHash,
            configurationHash,
            configuration.Ai.Provider,
            configuration.Ai.Deployment,
            configuration.Ai.ProviderPolicyVersion,
            "English",
            configuration.Persona.MessageTone,
            Math.Min(350, configuration.Invitation.MaximumCharacters),
            configuration.Invitation.MaximumCharacters,
            configuration.Ai.MaxOutputTokens,
            candidate.Id,
            snapshot.Id,
            [],
            feedback);
    }

    private EvaluationAggregate CreateAggregate(
        FounderScoutAnalysisClaim claim,
        FounderEvaluationRequest request,
        ModelEvaluationResponse response,
        FounderEvaluationValidationResult validation,
        InvitationDraftValidationResult draftValidation,
        CandidateScoreResult scores,
        CandidateRecommendation recommendation,
        int providerAttempts,
        bool evaluationValid)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        Guid id = Guid.NewGuid();
        string structured = FounderProfileCanonicalizer.Canonicalize(new
        {
            schemaVersion = "1.0",
            request = new
            {
                request.ScorecardVersion,
                request.ScorecardContentHash,
                request.PromptVersion,
                request.PromptContentHash,
                request.EvaluatorSchemaVersion,
                personaVersion = request.Persona.SchemaVersion,
                request.PersonaContentHash,
                request.ConfigurationPolicyHash,
                request.Provider,
                request.ModelOrDeployment,
                request.ProviderPolicyVersion,
                inputHash = request.CalculateInputHash(),
                baseInputHash = request.ExplicitReanalysisId.HasValue
                    ? (request with { ExplicitReanalysisId = null }).CalculateInputHash()
                    : null,
            },
            response,
            scores,
            validation = new { validation.IsValid, validation.EvidenceCoverage, errors = validation.Errors.Select(error => new { error.Code, error.Path }).ToArray() },
            draftValidation = new { draftValidation.IsValid, draftValidation.ErrorCodes, draftValidation.MaximumObservedSimilarity },
            providerAttempts,
        });
        var evaluation = new Evaluation(
            id,
            claim.Candidate.Id,
            request.SnapshotId,
            request.ScorecardVersion,
            request.Provider,
            request.ModelOrDeployment,
            request.ProviderPolicyVersion,
            request.PromptVersion,
            request.CalculateInputHash(),
            scores.FounderQualityScore,
            scores.OurFitScore,
            scores.OverallConfidence,
            scores.ActivityScore,
            scores.BaseScore,
            scores.RiskPenalty,
            scores.AdjustedScore,
            scores.InvitationPriority,
            recommendation.ToString(),
            structured,
            null,
            evaluationValid ? EvaluationStatus.Completed : EvaluationStatus.NeedsReview,
            evaluationValid ? null : "analysis.validation.needsReview",
            Math.Max(0, providerAttempts - 1),
            null,
            nowUtc,
            nowUtc,
            1);
        EvaluationCategory[] categories = response.QualityCategories.Select(item => new EvaluationCategory(
            Guid.NewGuid(), id, item.Key, item.Score,
            FounderEvaluationScorecard.Quality.Single(definition => definition.Key == item.Key).Maximum,
            FounderProfileCanonicalizer.Canonicalize(item.Evidence),
            item.Explanation,
            nowUtc)).ToArray();
        EvaluationRisk[] risks = response.Risks
            .Where(item => FounderEvaluationScorecard.RiskPenalties.ContainsKey(item.Key))
            .Select(item => new EvaluationRisk(
                Guid.NewGuid(), id, item.Key, FounderEvaluationScorecard.RiskPenalties[item.Key],
                FounderProfileCanonicalizer.Canonicalize(item.Evidence), item.Explanation, nowUtc))
            .ToArray();
        return new(evaluation, categories, risks);
    }

    private async ValueTask<InvitationDraft> PersistDraftAsync(
        FounderEvaluationRequest request,
        Guid evaluationId,
        ModelInvitationResponse invitation,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InvitationDraft> recent = await invitations.ListRecentActiveAsync(100, cancellationToken).ConfigureAwait(false);
        InvitationDraftValidationResult validation = draftValidator.Validate(
            request,
            invitation,
            recent.Select(item => new RecentInvitationDraft(item.CandidateId, item.ShortDraft, item.DetailedDraft, item.SimilarityFingerprint)).ToArray(),
            0.85m);
        return await PersistDraftAsync(request, evaluationId, invitation, validation, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InvitationDraft> PersistDraftAsync(
        FounderEvaluationRequest request,
        Guid evaluationId,
        ModelInvitationResponse invitation,
        InvitationDraftValidationResult validation,
        CancellationToken cancellationToken)
    {
        var draft = new InvitationDraft(
            Guid.NewGuid(),
            evaluationId,
            request.CandidateId,
            invitation.ShortDraft,
            invitation.DetailedDraft,
            FounderProfileCanonicalizer.Canonicalize(new
            {
                invitation.CandidateFactsUsed,
                invitation.PersonaStrengthsUsed,
                invitation.ConversationTopic,
                validation.MaximumObservedSimilarity,
            }),
            invitation.Confidence,
            validation.IsValid ? InvitationDraftStatus.Valid : InvitationDraftStatus.NeedsReview,
            FounderProfileCanonicalizer.Canonicalize(validation.ErrorCodes),
            validation.SimilarityFingerprint,
            timeProvider.GetUtcNow(),
            null,
            null,
            false,
            1);
        return await invitations.CreateAndSupersedePreviousAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FinalizeAsync(
        FounderScoutAnalysisClaim claim,
        FounderDeepAnalysisBatchRequest batch,
        Evaluation evaluation,
        InvitationDraft? draft,
        CancellationToken cancellationToken)
    {
        CandidateRecommendation recommendation = Enum.TryParse(evaluation.Recommendation, out CandidateRecommendation parsed)
            ? parsed
            : CandidateRecommendation.ManualReview;
        if (draft is null || draft.Status != InvitationDraftStatus.Valid || evaluation.Status != EvaluationStatus.Completed)
            recommendation = CandidateRecommendation.ManualReview;
        CandidateStatus status = recommendation switch
        {
            CandidateRecommendation.StrongConnect or CandidateRecommendation.ExploratoryCall => CandidateStatus.Shortlisted,
            CandidateRecommendation.Monitor => CandidateStatus.Monitor,
            CandidateRecommendation.Pass => CandidateStatus.Passed,
            _ => CandidateStatus.ManualReview,
        };
        bool validScores = evaluation.Status == EvaluationStatus.Completed;
        bool finalized = await queue.FinalizeAnalysisClaimAsync(new(
            claim.Candidate.Id,
            claim.WorkerId,
            status,
            evaluation.Id,
            validScores ? evaluation.FounderQualityScore : null,
            validScores ? evaluation.OurFitScore : null,
            validScores ? evaluation.Confidence : null,
            validScores ? evaluation.ActivityScore : null,
            validScores ? evaluation.RiskPenalty : null,
            validScores ? evaluation.InvitationPriority : null,
            "analysis.completed",
            batch.RunId,
            batch.CorrelationId), cancellationToken).ConfigureAwait(false);
        if (!finalized) throw new InvalidOperationException("The deep-analysis claim was lost before finalization.");
    }

    private async ValueTask ReleaseAsync(
        FounderScoutAnalysisClaim claim,
        FounderDeepAnalysisBatchRequest batch,
        string reasonCode,
        bool retry,
        CancellationToken cancellationToken)
    {
        _ = await queue.ReleaseAnalysisClaimAsync(new(
            claim.Candidate.Id,
            claim.WorkerId,
            reasonCode,
            retry,
            batch.RunId,
            batch.CorrelationId,
            CandidateStatus.ManualReview), cancellationToken).ConfigureAwait(false);
    }

    private static ModelEvaluationResponse? DeserializeStoredResponse(string structuredJson)
    {
        using JsonDocument document = JsonDocument.Parse(structuredJson);
        return document.RootElement.TryGetProperty("response", out JsonElement response)
            ? response.Deserialize<ModelEvaluationResponse>(StrictJson)
            : null;
    }

    private static TimeSpan CalculateRetryDelay(string inputHash, int attempt)
    {
        int jitter = int.Parse(inputHash.AsSpan((attempt - 1) * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) % 251;
        return TimeSpan.FromMilliseconds(Math.Min(5_000, 250 * (1 << Math.Min(attempt - 1, 4))) + jitter);
    }

    private static bool HasScorableShape(ModelEvaluationResponse response)
    {
        if (response.QualityCategories is null || response.FitDimensions is null || response.Risks is null)
            return false;
        bool Exact(IReadOnlyList<ModelScoreItem> actual, IReadOnlyList<FounderScoreDefinition> expected) =>
            actual.Count == expected.Count
            && actual.All(item => item is not null)
            && actual.GroupBy(item => item.Key, StringComparer.Ordinal).All(group => group.Count() == 1)
            && expected.All(definition => actual.Any(item => item.Key == definition.Key && item.Score >= 0 && item.Score <= definition.Maximum));
        return Exact(response.QualityCategories, FounderEvaluationScorecard.Quality)
            && Exact(response.FitDimensions, FounderEvaluationScorecard.Fit)
            && response.Risks.All(item => item is not null && FounderEvaluationScorecard.RiskPenalties.ContainsKey(item.Key));
    }

    private static void ValidateRequest(FounderDeepAnalysisBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.Prompt);
        if (request.MaximumCandidates is < 1 or > 500
            || request.MaximumConcurrency is < 1 or > 16
            || request.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.WorkerId)
            || request.WorkerId.Length > 100
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > 128
            || request.CandidateId == Guid.Empty
            || request.CandidateId.HasValue && (request.MaximumCandidates != 1 || request.MaximumConcurrency != 1))
            throw new ArgumentException("The deep-analysis batch request is invalid.", nameof(request));
    }

    private sealed record EvaluationAttemptOutcome(
        FounderEvaluationRequest Request,
        ModelEvaluationResponse? Response,
        FounderEvaluationValidationResult? Validation,
        InvitationDraftValidationResult? DraftValidation,
        int ProviderAttempts,
        bool TransientFailure,
        bool PermanentFailure,
        bool InvalidStructuredOutput,
        string? ErrorCode);

    private sealed class AnalysisAccumulator(int reevaluationQueued)
    {
        private readonly object gate = new();
        private readonly List<FounderAnalysisTopCandidate> top = [];
        private int claimed;
        private int completed;
        private int cacheHits;
        private int manualReview;
        private int failed;
        private int providerRequests;
        private int providerRetries;
        private int strong;
        private int exploratory;
        private int monitor;
        private int pass;
        private int invitationsGenerated;
        private int invitationsNeedsReview;
        private bool attentionRequired;
        private string? attentionCode;
        private int stopRequested;

        public int Completed => Volatile.Read(ref completed);
        public ref int StopRequested => ref stopRequested;
        public void IncrementClaimed() => Interlocked.Increment(ref claimed);
        public void IncrementProviderRequests() => Interlocked.Increment(ref providerRequests);
        public void IncrementProviderRetries() => Interlocked.Increment(ref providerRetries);

        public void RecordCacheHit(Candidate candidate, Evaluation evaluation, InvitationDraft? draft)
        {
            Interlocked.Increment(ref cacheHits);
            RecordCompleted(candidate, evaluation, draft);
        }

        public void RecordCompleted(Candidate candidate, Evaluation evaluation, InvitationDraft? draft)
        {
            Interlocked.Increment(ref completed);
            if (draft is not null)
            {
                Interlocked.Increment(ref invitationsGenerated);
                if (draft.Status != InvitationDraftStatus.Valid) Interlocked.Increment(ref invitationsNeedsReview);
            }
            CandidateRecommendation recommendation = Enum.TryParse(evaluation.Recommendation, out CandidateRecommendation parsed)
                ? parsed
                : CandidateRecommendation.ManualReview;
            if (draft is null || draft.Status != InvitationDraftStatus.Valid || evaluation.Status != EvaluationStatus.Completed)
                recommendation = CandidateRecommendation.ManualReview;
            switch (recommendation)
            {
                case CandidateRecommendation.StrongConnect: Interlocked.Increment(ref strong); break;
                case CandidateRecommendation.ExploratoryCall: Interlocked.Increment(ref exploratory); break;
                case CandidateRecommendation.Monitor: Interlocked.Increment(ref monitor); break;
                case CandidateRecommendation.Pass: Interlocked.Increment(ref pass); break;
                default: Interlocked.Increment(ref manualReview); break;
            }
            lock (gate)
            {
                top.Add(new(candidate.Id, candidate.DisplayName, evaluation.InvitationPriority, recommendation.ToString()));
            }
        }

        public void RecordFailure(bool manualReview)
        {
            Interlocked.Increment(ref failed);
            if (manualReview) Interlocked.Increment(ref this.manualReview);
        }

        public void RecordAttention(string code)
        {
            Interlocked.Increment(ref failed);
            lock (gate)
            {
                attentionRequired = true;
                attentionCode ??= code;
            }
            Interlocked.Exchange(ref stopRequested, 1);
        }

        public FounderDeepAnalysisBatchResult ToResult()
        {
            FounderAnalysisTopCandidate[] bounded;
            lock (gate)
            {
                bounded = top.OrderByDescending(item => item.InvitationPriority).ThenBy(item => item.CandidateId).Take(5).ToArray();
            }
            return new(
                claimed,
                completed,
                cacheHits,
                0,
                manualReview,
                failed,
                providerRequests,
                providerRetries,
                strong,
                exploratory,
                monitor,
                pass,
                invitationsGenerated,
                invitationsNeedsReview,
                reevaluationQueued,
                attentionRequired,
                attentionCode,
                bounded);
        }
    }
}
