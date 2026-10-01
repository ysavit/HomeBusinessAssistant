using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Domain;

namespace FounderScout.Application;

/// <summary>One bounded processing progress observation.</summary>
public sealed record FounderScoutProcessingProgress(int Current, int Maximum, Guid SnapshotId, string Phase, string Message);

/// <summary>Input for one no-AI processing batch.</summary>
public sealed record FounderScoutProcessingBatchRequest(
    int MaximumProfiles,
    string WorkerId,
    Guid RunId,
    string CorrelationId,
    FounderScoutProcessingSettings Settings,
    Guid? SnapshotId = null);

/// <summary>Deterministic processing metrics and reason distribution.</summary>
public sealed record FounderScoutProcessingBatchResult(
    int Claimed,
    int Completed,
    int Unchanged,
    int Changed,
    int DeepAnalysisQueued,
    int Monitored,
    int FilteredOut,
    int ManualReview,
    int ParserFailures,
    int ProcessingFailures,
    int IdentityConflicts,
    bool StoppedForParserHealth,
    IReadOnlyDictionary<string, int> ReasonCodeDistribution);

/// <summary>Claims, transforms, and atomically commits captured profiles without any AI dependency.</summary>
public sealed class FounderScoutProcessingService(
    IFounderScoutProcessingQueue queue,
    IFounderScoutRawArtifactReader artifacts,
    IFounderProfileParser parser,
    IProfileRedactor redactor,
    ICandidateIdentityResolver identityResolver,
    IFounderScreeningEngine screeningEngine)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly IFounderScoutProcessingQueue queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly IFounderScoutRawArtifactReader artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly IFounderProfileParser parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IProfileRedactor redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    private readonly ICandidateIdentityResolver identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    private readonly IFounderScreeningEngine screeningEngine = screeningEngine ?? throw new ArgumentNullException(nameof(screeningEngine));

    /// <summary>Processes at most the requested number of raw snapshots.</summary>
    public async ValueTask<FounderScoutProcessingBatchResult> ProcessAsync(
        FounderScoutProcessingBatchRequest request,
        Func<FounderScoutProcessingProgress, CancellationToken, ValueTask>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var claimed = 0;
        var completed = 0;
        var unchanged = 0;
        var changed = 0;
        var queued = 0;
        var monitored = 0;
        var filtered = 0;
        var manual = 0;
        var parserFailures = 0;
        var processingFailures = 0;
        var conflicts = 0;
        var consecutiveParserFailures = 0;
        var stopped = false;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        TimeSpan lease = TimeSpan.FromSeconds(request.Settings.ProcessingLeaseSeconds);
        FounderScreeningRules rules = new(
            request.Settings.RulesetVersion,
            request.Settings.DeepAnalysisThreshold,
            request.Settings.MonitorThreshold,
            request.Settings.PreferNonTechnical,
            request.Settings.RequireLocationCompatibility,
            request.Settings.HardFilterUnpaidImplementationLabor,
            request.Settings.FilterIdenticalTechnicalPreference);

        while (claimed < request.MaximumProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FounderScoutProcessingClaim? claim = request.SnapshotId.HasValue
                ? await queue.ClaimProcessingSnapshotAsync(request.SnapshotId.Value, request.WorkerId, lease, cancellationToken).ConfigureAwait(false)
                : await queue.ClaimPendingProcessingAsync(request.WorkerId, lease, cancellationToken).ConfigureAwait(false);
            if (claim is null) break;
            claimed++;
            if (progress is not null)
            {
                await progress(new(claimed - 1, request.MaximumProfiles, claim.Snapshot.Id, "profile-processing", "Parsing and screening one claimed profile."), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                FounderScoutCaptureEnvelope capture = await artifacts.ReadAsync(claim.Snapshot.RawArtifactRelativePath, cancellationToken).ConfigureAwait(false);
                FounderProfileParseResult parsed = parser.Parse(capture);
                bool parserHealthy = parsed.Profile is not null
                    && parsed.Health.IsHealthy
                    && parsed.Profile.Completeness >= request.Settings.MinimumParserCompleteness;
                if (!parserHealthy)
                {
                    parserFailures++;
                    consecutiveParserFailures++;
                    foreach (string reason in parsed.Health.ReasonCodes.DefaultIfEmpty("parser.completeness.belowThreshold")) Increment(reasons, reason);
                    await queue.FailProcessingAsync(new(
                        claim.Snapshot.Id,
                        request.WorkerId,
                        parsed.Health.ReasonCodes.Count > 0 ? parsed.Health.ReasonCodes[0] : "parser.completeness.belowThreshold",
                        Retry: false,
                        request.RunId,
                        request.CorrelationId), cancellationToken).ConfigureAwait(false);
                    if (consecutiveParserFailures >= request.Settings.MaximumConsecutiveParserFailures)
                    {
                        await queue.MarkParserFailureAsync(claim.Snapshot.SourceAccountId, claim.Snapshot.SourceSegmentId, "parser.health.repeatedFailure", cancellationToken).ConfigureAwait(false);
                        Increment(reasons, "parser.health.repeatedFailure");
                        stopped = true;
                        break;
                    }
                    continue;
                }

                consecutiveParserFailures = 0;
                NormalizedFounderProfile profile = parsed.Profile!;
                FounderRedactionResult redaction = redactor.Redact(profile, parsed.Evidence);
                FounderScreeningResult screening = screeningEngine.Screen(redaction.Input, redaction, rules);
                CandidateIdentityResolution identities = identityResolver.Resolve(profile, capture.StructuredFields, capture.SourceAccountId);
                string normalizedJson = FounderProfileCanonicalizer.Canonicalize(profile);
                string normalizedHash = FounderProfileCanonicalizer.HashUtf8(normalizedJson);
                string evaluatorJson = FounderProfileCanonicalizer.Canonicalize(redaction.Input);
                string evaluatorHash = FounderProfileCanonicalizer.HashUtf8(evaluatorJson);
                string evidenceJson = FounderProfileCanonicalizer.Canonicalize(parsed.Evidence);
                string redactionJson = FounderProfileCanonicalizer.Canonicalize(new
                {
                    redaction.RedactorVersion,
                    redaction.ReasonCounts,
                    redaction.Confidence,
                    redaction.RequiresManualReview,
                });
                NormalizedFounderProfile? previous = DeserializePrevious(claim.CurrentNormalizedProfileJson);
                IReadOnlyList<FounderProfileChange> differences = FounderProfileDiffer.Diff(previous, profile);
                CompleteFounderProfileProcessingResult persisted = await queue.CompleteProcessingAsync(new(
                    claim.Candidate.Id,
                    claim.Snapshot.Id,
                    request.WorkerId,
                    normalizedJson,
                    normalizedHash,
                    evaluatorJson,
                    evaluatorHash,
                    evidenceJson,
                    redactionJson,
                    profile,
                    screening,
                    identities.Signals,
                    differences,
                    request.RunId,
                    request.CorrelationId), cancellationToken).ConfigureAwait(false);
                completed++;
                conflicts += persisted.IdentityConflictsCreated;
                if (persisted.NormalizedChanged) changed++; else unchanged++;
                if (persisted.ScreeningCreated)
                {
                    foreach (string reason in screening.ReasonCodes) Increment(reasons, reason);
                    switch (screening.Outcome)
                    {
                        case ScreeningOutcome.DeepAnalyze: queued++; break;
                        case ScreeningOutcome.Monitor: monitored++; break;
                        case ScreeningOutcome.FilteredOut: filtered++; break;
                        case ScreeningOutcome.ManualReview: manual++; break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await queue.FailProcessingAsync(new(claim.Snapshot.Id, request.WorkerId, "processing.cancelled", true, request.RunId, request.CorrelationId), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
            {
                processingFailures++;
                Increment(reasons, "processing.profile.failed");
                await queue.FailProcessingAsync(new(claim.Snapshot.Id, request.WorkerId, "processing.profile.failed", true, request.RunId, request.CorrelationId), cancellationToken).ConfigureAwait(false);
            }
        }

        if (progress is not null)
        {
            await progress(new(completed, request.MaximumProfiles, Guid.Empty, "profile-processing", stopped ? "Processing stopped on repeated parser-health failure." : "Processing batch completed."), cancellationToken).ConfigureAwait(false);
        }
        return new(claimed, completed, unchanged, changed, queued, monitored, filtered, manual, parserFailures, processingFailures, conflicts, stopped, reasons);
    }

    private static void Validate(FounderScoutProcessingBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        if (request.MaximumProfiles is < 1 or > 500 || request.RunId == Guid.Empty || string.IsNullOrWhiteSpace(request.WorkerId) || request.WorkerId.Length > 128 || string.IsNullOrWhiteSpace(request.CorrelationId) || request.CorrelationId.Length > 128)
            throw new ArgumentException("The Founder Scout processing batch request is invalid.", nameof(request));
        if (request.SnapshotId == Guid.Empty || request.SnapshotId.HasValue && request.MaximumProfiles != 1)
            throw new ArgumentException("Targeted processing requires one valid snapshot and a batch size of one.", nameof(request));
    }

    private static NormalizedFounderProfile? DeserializePrevious(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<NormalizedFounderProfile>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static void Increment(Dictionary<string, int> values, string key) => values[key] = values.TryGetValue(key, out int count) ? count + 1 : 1;
}
