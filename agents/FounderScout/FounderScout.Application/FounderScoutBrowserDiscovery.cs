using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Scheduling;

namespace FounderScout.Application;

/// <summary>Safe-stop classifications produced at the browser boundary.</summary>
public enum BrowserStopKind
{
    /// <summary>No stop condition.</summary>
    None = 0,
    /// <summary>The human must authenticate again.</summary>
    ReauthenticationRequired = 1,
    /// <summary>The source denied access.</summary>
    AccessDenied = 2,
    /// <summary>The source requested slower access.</summary>
    Throttled = 3,
    /// <summary>An interactive access challenge was detected.</summary>
    ChallengeDetected = 4,
    /// <summary>Navigation left the configured allow-list.</summary>
    UnexpectedHost = 5,
    /// <summary>The source parser failed its health threshold.</summary>
    ParserFailure = 6,
    /// <summary>Repeated navigation failed.</summary>
    NavigationFailure = 7,
    /// <summary>Another process owns the account profile.</summary>
    ProfileLocked = 8,
    /// <summary>The configured browser binary is unavailable.</summary>
    BrowserUnavailable = 9,
    /// <summary>The operation was cancelled.</summary>
    Cancelled = 10,
}

/// <summary>A bounded machine-readable browser stop observation.</summary>
public sealed record BrowserStopSignal(
    BrowserStopKind Kind,
    string ReasonCode,
    string Message,
    bool IsTransient = false)
{
    /// <summary>No stop condition was observed.</summary>
    public static BrowserStopSignal None { get; } = new(BrowserStopKind.None, "none", "No browser stop condition was observed.");
}

/// <summary>Validated browser runtime health without exposing profile data.</summary>
public sealed record BrowserRuntimeStatus(
    bool IsAvailable,
    string RuntimeName,
    string PlaywrightVersion,
    string? BrowserVersion,
    string ReasonCode,
    string Message);

/// <summary>One persistent browser-context launch request.</summary>
public sealed record BrowserSessionRequest(
    string AccountId,
    string ProfileDirectory,
    bool Headless,
    string? BrowserChannel,
    int NavigationTimeoutSeconds);

/// <summary>Opaque application-facing browser session handle.</summary>
public interface IBrowserSession : IAsyncDisposable
{
    /// <summary>The current safe page URL, with sensitive query values removed by the adapter.</summary>
    string CurrentUrl { get; }
    /// <summary>Whether the underlying context has closed.</summary>
    bool IsClosed { get; }
}

/// <summary>Validates and creates the pinned browser automation runtime.</summary>
public interface IBrowserRuntimeFactory
{
    /// <summary>Checks whether the configured runtime is installed without downloading it.</summary>
    ValueTask<BrowserRuntimeStatus> DiagnoseAsync(string? browserChannel, CancellationToken cancellationToken = default);
}

/// <summary>Owns one exclusive persistent browser context per account.</summary>
public interface IBrowserSessionManager
{
    /// <summary>Opens one context or returns a typed safe-stop result.</summary>
    ValueTask<BrowserSessionOpenResult> OpenAsync(BrowserSessionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Result of acquiring a browser profile and opening its context.</summary>
public sealed record BrowserSessionOpenResult(IBrowserSession? Session, BrowserStopSignal StopSignal)
{
    /// <summary>Whether a usable session was opened.</summary>
    public bool IsSuccess => Session is not null && StopSignal.Kind == BrowserStopKind.None;
}

/// <summary>One stable profile link found in source order.</summary>
public sealed record DiscoveredProfileLink(string SourceProfileKey, string Url);

/// <summary>One bounded batch from pagination, load-more, or scrolling.</summary>
public sealed record ProfileDiscoveryBatch(
    IReadOnlyList<DiscoveredProfileLink> Links,
    string? Continuation,
    int PageOrScrollMarker,
    bool Exhausted,
    BrowserStopSignal StopSignal);

/// <summary>Versioned Startup School navigation adapter.</summary>
public interface IProfileDiscoverySource
{
    /// <summary>Navigates to the entry/list page and classifies session health.</summary>
    ValueTask<BrowserStopSignal> OpenEntryAsync(
        IBrowserSession session,
        StartupSchoolSourceOptions options,
        CancellationToken cancellationToken = default);
    /// <summary>Waits for a human-completed headed authentication flow.</summary>
    ValueTask<BrowserStopSignal> WaitForAuthenticationAsync(
        IBrowserSession session,
        StartupSchoolSourceOptions options,
        CancellationToken cancellationToken = default);
    /// <summary>Gets one sequential bounded profile-link batch.</summary>
    ValueTask<ProfileDiscoveryBatch> GetNextBatchAsync(
        IBrowserSession session,
        StartupSchoolSourceOptions options,
        string? continuation,
        int pageOrScrollMarker,
        CancellationToken cancellationToken = default);
}

/// <summary>Extracts one grounded profile envelope from the current source adapter.</summary>
public interface IProfileCaptureExtractor
{
    /// <summary>Navigates to and extracts one profile, or returns a safe-stop result.</summary>
    ValueTask<ProfileCaptureExtractionResult> ExtractAsync(
        IBrowserSession session,
        StartupSchoolSourceOptions options,
        string accountId,
        string segmentId,
        DiscoveredProfileLink link,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of one profile navigation/extraction attempt.</summary>
public sealed record ProfileCaptureExtractionResult(
    FounderScoutCaptureEnvelope? Capture,
    BrowserStopSignal StopSignal,
    int NavigationAttempts);

/// <summary>Classifies authentication, enforcement, host, and parser signals.</summary>
public interface IBrowserChallengeDetector
{
    /// <summary>Detects a stop signal for the current page.</summary>
    ValueTask<BrowserStopSignal> DetectAsync(
        IBrowserSession session,
        StartupSchoolSourceOptions options,
        bool requireProfileRoot,
        CancellationToken cancellationToken = default);
}

/// <summary>One bounded diagnostic staged for Runner ingestion.</summary>
public sealed record BrowserDiagnosticArtifact(
    string RelativePath,
    string ContentType,
    long FileSize,
    string Sha256,
    DateTimeOffset DeleteAfterUtc);

/// <summary>Captures redacted, bounded browser failure evidence.</summary>
public interface IBrowserDiagnosticCapture
{
    /// <summary>Captures allowed diagnostics under the run artifact directory.</summary>
    ValueTask<IReadOnlyList<BrowserDiagnosticArtifact>> CaptureAsync(
        IBrowserSession session,
        string artifactDirectory,
        BrowserStopSignal signal,
        int retentionDays,
        CancellationToken cancellationToken = default);
}

/// <summary>Counts durable account captures for the daily discovery budget.</summary>
public interface IDiscoveryHistoryReader
{
    /// <summary>Counts distinct candidates captured by an account from the inclusive UTC boundary.</summary>
    ValueTask<int> CountDistinctCandidatesCapturedSinceAsync(
        string accountId,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>One browser discovery orchestration request.</summary>
public sealed record FounderScoutDiscoveryRequest(
    BrowserAccount Account,
    DiscoverySegment Segment,
    FounderScoutDiscoverySettings Limits,
    StartupSchoolSourceOptions Source,
    string ProfileDirectory,
    string ArtifactDirectory,
    Guid RunId,
    string CorrelationId,
    int RawProfileRetentionDays,
    int ErrorArtifactRetentionDays);

/// <summary>Deterministic completion classifications distinct from safe stops.</summary>
public enum FounderScoutDiscoveryCompletion
{
    /// <summary>The available source segment was exhausted.</summary>
    Completed = 0,
    /// <summary>No eligible or unseen work existed.</summary>
    NoWork = 1,
    /// <summary>An explicit safety or volume cap ended work successfully.</summary>
    Capped = 2,
    /// <summary>A browser safe-stop condition ended work.</summary>
    Stopped = 3,
}

/// <summary>Durable metrics and result of one discovery segment.</summary>
public sealed record FounderScoutDiscoveryResult(
    FounderScoutDiscoveryCompletion Completion,
    BrowserStopSignal StopSignal,
    int ViewedProfiles,
    int NewCandidates,
    int NewSnapshots,
    int KnownUnchangedProfiles,
    int ConsecutiveKnownProfiles,
    int NavigationAttempts,
    string CompletionReasonCode,
    IReadOnlyList<BrowserDiagnosticArtifact> Diagnostics);

/// <summary>Runs one sequential, resumable, bounded source segment.</summary>
public sealed class FounderScoutDiscoveryService(
    IBrowserSessionManager sessions,
    IProfileDiscoverySource source,
    IProfileCaptureExtractor extractor,
    IBrowserChallengeDetector challengeDetector,
    IBrowserDiagnosticCapture diagnostics,
    FounderScoutCaptureCommitService captures,
    IDiscoveryCheckpointRepository checkpoints,
    IDiscoveryHistoryReader history,
    IBrowserAccountRepository accounts,
    IDiscoverySegmentRepository segments,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IBrowserSessionManager sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly IProfileDiscoverySource source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IProfileCaptureExtractor extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    private readonly IBrowserChallengeDetector challengeDetector = challengeDetector ?? throw new ArgumentNullException(nameof(challengeDetector));
    private readonly IBrowserDiagnosticCapture diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly FounderScoutCaptureCommitService captures = captures ?? throw new ArgumentNullException(nameof(captures));
    private readonly IDiscoveryCheckpointRepository checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly IDiscoveryHistoryReader history = history ?? throw new ArgumentNullException(nameof(history));
    private readonly IBrowserAccountRepository accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly IDiscoverySegmentRepository segments = segments ?? throw new ArgumentNullException(nameof(segments));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Discovers and commits profiles until source exhaustion or one explicit bound.</summary>
    public async ValueTask<FounderScoutDiscoveryResult> DiscoverAsync(
        FounderScoutDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        if (!request.Limits.Enabled || !request.Account.Enabled || !request.Segment.Enabled)
        {
            return Empty(FounderScoutDiscoveryCompletion.NoWork, "discovery.disabled");
        }

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        if (request.Account.SuspendedUntilUtc is DateTimeOffset suspendedUntil && suspendedUntil > startedAtUtc)
        {
            return Empty(FounderScoutDiscoveryCompletion.NoWork, "discovery.accountSuspended");
        }

        if (request.Segment.PausedUntilUtc is DateTimeOffset pausedUntil && pausedUntil > startedAtUtc)
        {
            return Empty(FounderScoutDiscoveryCompletion.NoWork, "discovery.segmentPaused");
        }

        int capturedToday = await history.CountDistinctCandidatesCapturedSinceAsync(
            request.Account.Id,
            new DateTimeOffset(startedAtUtc.UtcDateTime.Date, TimeSpan.Zero),
            cancellationToken).ConfigureAwait(false);
        if (capturedToday >= request.Limits.MaxNewProfilesPerDay)
        {
            return Empty(FounderScoutDiscoveryCompletion.Capped, "discovery.dailyLimit");
        }

        BrowserSessionOpenResult opened = await sessions.OpenAsync(new(
            request.Account.Id,
            request.ProfileDirectory,
            request.Source.HeadlessDiscovery,
            request.Source.BrowserChannel,
            request.Source.NavigationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            await ApplyStopAsync(request.Account.Id, opened.StopSignal, cancellationToken).ConfigureAwait(false);
            return Empty(FounderScoutDiscoveryCompletion.Stopped, opened.StopSignal.ReasonCode, opened.StopSignal);
        }

        await using IBrowserSession browser = opened.Session!;
        BrowserStopSignal entry = await source.OpenEntryAsync(browser, request.Source, cancellationToken).ConfigureAwait(false);
        if (entry.Kind != BrowserStopKind.None)
        {
            return await StopAsync(request, browser, entry, 0, 0, 0, 0, 0, 0, cancellationToken).ConfigureAwait(false);
        }

        DiscoveryCheckpoint? latest = await checkpoints.GetLatestAsync(
            request.Account.Id,
            request.Segment.Id,
            cancellationToken).ConfigureAwait(false);
        DiscoveryResumeState resume = ParseResume(latest, request.Source.AdapterVersion);
        string? continuation = resume.Continuation;
        int marker = resume.PageOrScrollMarker;
        var seenLinks = new HashSet<string>(resume.SeenProfileKeys, StringComparer.Ordinal);
        var viewed = 0;
        var createdCandidates = 0;
        var createdSnapshots = 0;
        var known = 0;
        var consecutiveKnown = 0;
        var navigationAttempts = 0;
        var exhausted = false;
        var completion = FounderScoutDiscoveryCompletion.Completed;
        string completionReason = "discovery.sourceExhausted";

        while (!exhausted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? cap = GetCapReason(request, startedAtUtc, timeProvider.GetUtcNow(), capturedToday, viewed, createdSnapshots, consecutiveKnown);
            if (cap is not null)
            {
                completion = FounderScoutDiscoveryCompletion.Capped;
                completionReason = cap;
                break;
            }

            ProfileDiscoveryBatch batch = await source.GetNextBatchAsync(
                browser,
                request.Source,
                continuation,
                marker,
                cancellationToken).ConfigureAwait(false);
            if (batch.StopSignal.Kind != BrowserStopKind.None)
            {
                return await StopAsync(request, browser, batch.StopSignal, viewed, createdCandidates, createdSnapshots, known, consecutiveKnown, navigationAttempts, cancellationToken).ConfigureAwait(false);
            }

            continuation = batch.Continuation;
            marker = batch.PageOrScrollMarker;
            exhausted = batch.Exhausted;
            DiscoveredProfileLink[] unseen = batch.Links
                .Where(link => seenLinks.Add(link.SourceProfileKey))
                .ToArray();
            if (unseen.Length == 0 && exhausted)
            {
                break;
            }

            foreach (DiscoveredProfileLink link in unseen)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? innerCap = GetCapReason(request, startedAtUtc, timeProvider.GetUtcNow(), capturedToday, viewed, createdSnapshots, consecutiveKnown);
                if (innerCap is not null)
                {
                    completion = FounderScoutDiscoveryCompletion.Capped;
                    completionReason = innerCap;
                    exhausted = true;
                    break;
                }

                BrowserStopSignal signal = await challengeDetector.DetectAsync(browser, request.Source, requireProfileRoot: false, cancellationToken).ConfigureAwait(false);
                if (signal.Kind != BrowserStopKind.None)
                {
                    return await StopAsync(request, browser, signal, viewed, createdCandidates, createdSnapshots, known, consecutiveKnown, navigationAttempts, cancellationToken).ConfigureAwait(false);
                }

                ProfileCaptureExtractionResult extracted = await extractor.ExtractAsync(
                    browser,
                    request.Source,
                    request.Account.Id,
                    request.Segment.Id,
                    link,
                    cancellationToken).ConfigureAwait(false);
                viewed++;
                navigationAttempts += extracted.NavigationAttempts;
                if (extracted.StopSignal.Kind != BrowserStopKind.None || extracted.Capture is null)
                {
                    BrowserStopSignal stop = extracted.StopSignal.Kind == BrowserStopKind.None
                        ? new(BrowserStopKind.ParserFailure, "browser.parser.emptyCapture", "The source adapter returned no validated capture.")
                        : extracted.StopSignal;
                    return await StopAsync(request, browser, stop, viewed, createdCandidates, createdSnapshots, known, consecutiveKnown, navigationAttempts, cancellationToken).ConfigureAwait(false);
                }

                FounderScoutCaptureCommitResult committed = await captures.CommitAsync(new(
                    extracted.Capture,
                    request.RunId,
                    request.CorrelationId,
                    request.RawProfileRetentionDays), cancellationToken).ConfigureAwait(false);
                if (committed.CandidateCreated)
                {
                    createdCandidates++;
                }

                if (committed.SnapshotCreated)
                {
                    createdSnapshots++;
                    capturedToday++;
                    consecutiveKnown = 0;
                }
                else
                {
                    known++;
                    consecutiveKnown++;
                }

                await AppendCheckpointAsync(
                    request,
                    link.SourceProfileKey,
                    continuation,
                    marker,
                    seenLinks,
                    viewed,
                    createdSnapshots,
                    known,
                    "profile-committed",
                    cancellationToken).ConfigureAwait(false);
                if (request.Source.MinimumRequestSpacingMilliseconds > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(request.Source.MinimumRequestSpacingMilliseconds),
                        timeProvider,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (viewed == 0 && completion == FounderScoutDiscoveryCompletion.Completed)
        {
            completion = FounderScoutDiscoveryCompletion.NoWork;
            completionReason = "discovery.noUnseenProfiles";
        }

        await AppendCheckpointAsync(
            request,
            seenLinks.Count == 0 ? null : seenLinks.Last(),
            continuation,
            marker,
            seenLinks,
            viewed,
            createdSnapshots,
            known,
            completionReason,
            cancellationToken).ConfigureAwait(false);
        await MarkSuccessAsync(request, viewed, createdSnapshots, known, cancellationToken).ConfigureAwait(false);
        return new(completion, BrowserStopSignal.None, viewed, createdCandidates, createdSnapshots, known, consecutiveKnown, navigationAttempts, completionReason, []);
    }

    private static string? GetCapReason(
        FounderScoutDiscoveryRequest request,
        DateTimeOffset startedAtUtc,
        DateTimeOffset nowUtc,
        int capturedToday,
        int viewed,
        int createdSnapshots,
        int consecutiveKnown)
    {
        if (nowUtc.ToUniversalTime() - startedAtUtc >= TimeSpan.FromSeconds(request.Limits.MaxRuntimeSeconds))
        {
            return "discovery.runtimeLimit";
        }

        if (viewed >= request.Limits.MaxViewedProfilesPerRun)
        {
            return "discovery.viewedLimit";
        }

        if (createdSnapshots >= request.Limits.MaxNewProfilesPerRun)
        {
            return "discovery.newProfileLimit";
        }

        if (capturedToday >= request.Limits.MaxNewProfilesPerDay)
        {
            return "discovery.dailyLimit";
        }

        if (consecutiveKnown >= request.Limits.StopAfterConsecutiveKnownProfiles)
        {
            return "discovery.consecutiveKnownLimit";
        }

        return null;
    }

    private async ValueTask<FounderScoutDiscoveryResult> StopAsync(
        FounderScoutDiscoveryRequest request,
        IBrowserSession browser,
        BrowserStopSignal signal,
        int viewed,
        int candidates,
        int snapshots,
        int known,
        int consecutiveKnown,
        int navigationAttempts,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BrowserDiagnosticArtifact> artifacts = await diagnostics.CaptureAsync(
            browser,
            request.ArtifactDirectory,
            signal,
            request.ErrorArtifactRetentionDays,
            cancellationToken).ConfigureAwait(false);
        await ApplyStopAsync(request.Account.Id, signal, cancellationToken).ConfigureAwait(false);
        await AppendCheckpointAsync(request, null, null, 0, [], viewed, snapshots, known, signal.ReasonCode, cancellationToken).ConfigureAwait(false);
        return new(FounderScoutDiscoveryCompletion.Stopped, signal, viewed, candidates, snapshots, known, consecutiveKnown, navigationAttempts, signal.ReasonCode, artifacts);
    }

    private async ValueTask ApplyStopAsync(string accountId, BrowserStopSignal signal, CancellationToken cancellationToken)
    {
        BrowserSessionStatus status = signal.Kind switch
        {
            BrowserStopKind.ReauthenticationRequired => BrowserSessionStatus.ReauthenticationRequired,
            BrowserStopKind.AccessDenied => BrowserSessionStatus.AccessDenied,
            BrowserStopKind.Throttled => BrowserSessionStatus.Throttled,
            BrowserStopKind.ChallengeDetected => BrowserSessionStatus.ChallengeDetected,
            BrowserStopKind.ParserFailure or BrowserStopKind.UnexpectedHost => BrowserSessionStatus.ParserFailure,
            _ => BrowserSessionStatus.Unknown,
        };
        BrowserAccount? account = await accounts.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.SessionStatus == BrowserSessionStatus.Disabled)
        {
            return;
        }

        await accounts.TransitionHealthAsync(accountId, status, signal.ReasonCode, signal.Message, null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask MarkSuccessAsync(
        FounderScoutDiscoveryRequest request,
        int viewed,
        int created,
        int known,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        BrowserAccount currentAccount = await accounts.GetAsync(request.Account.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The browser account disappeared while discovery was running.");
        if (currentAccount.SessionStatus != BrowserSessionStatus.Disabled)
        {
            await accounts.UpsertAsync(currentAccount with
            {
                SessionStatus = BrowserSessionStatus.Healthy,
                LastSuccessfulRunAtUtc = nowUtc,
                LastErrorReasonCode = null,
                LastErrorMessage = null,
                SuspendedUntilUtc = null,
                UpdatedAtUtc = nowUtc,
            }, cancellationToken).ConfigureAwait(false);
        }

        DiscoverySegment currentSegment = await segments.GetAsync(request.Segment.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The discovery segment disappeared while discovery was running.");
        await segments.UpsertAsync(currentSegment with
        {
            LastRunAtUtc = nowUtc,
            ViewedCount = checked(currentSegment.ViewedCount + viewed),
            NewCount = checked(currentSegment.NewCount + created),
            DuplicateCount = checked(currentSegment.DuplicateCount + known),
            ConsecutiveLowYieldRuns = created == 0 ? currentSegment.ConsecutiveLowYieldRuns + 1 : 0,
            UpdatedAtUtc = nowUtc,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AppendCheckpointAsync(
        FounderScoutDiscoveryRequest request,
        string? lastKey,
        string? continuation,
        int marker,
        IEnumerable<string> seen,
        int viewed,
        int created,
        int known,
        string status,
        CancellationToken cancellationToken)
    {
        string[] keys = seen.TakeLast(2_000).Order(StringComparer.Ordinal).ToArray();
        string linkSetHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', keys))));
        string state = JsonSerializer.Serialize(new DiscoveryResumeState(
            request.Source.AdapterVersion,
            continuation,
            marker,
            keys,
            linkSetHash,
            viewed,
            created,
            known), JsonOptions);
        await checkpoints.AppendAsync(new(
            Guid.NewGuid(),
            request.Account.Id,
            request.Segment.Id,
            request.RunId,
            lastKey,
            state,
            timeProvider.GetUtcNow().ToUniversalTime(),
            status,
            "1.0"), cancellationToken).ConfigureAwait(false);
    }

    private static DiscoveryResumeState ParseResume(DiscoveryCheckpoint? checkpoint, string adapterVersion)
    {
        if (checkpoint is null || checkpoint.Version != "1.0")
        {
            return DiscoveryResumeState.Empty(adapterVersion);
        }

        try
        {
            DiscoveryResumeState? state = JsonSerializer.Deserialize<DiscoveryResumeState>(checkpoint.StateJson, JsonOptions);
            return state is not null && state.AdapterVersion == adapterVersion
                ? state
                : DiscoveryResumeState.Empty(adapterVersion);
        }
        catch (JsonException)
        {
            return DiscoveryResumeState.Empty(adapterVersion);
        }
    }

    private static void ValidateRequest(FounderScoutDiscoveryRequest request)
    {
        if (request.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > 128
            || !Path.IsPathFullyQualified(request.ProfileDirectory)
            || !Path.IsPathFullyQualified(request.ArtifactDirectory)
            || request.RawProfileRetentionDays is < 1 or > 365
            || request.ErrorArtifactRetentionDays is < 1 or > 365)
        {
            throw new ArgumentException("The discovery request is invalid.", nameof(request));
        }
    }

    private static FounderScoutDiscoveryResult Empty(
        FounderScoutDiscoveryCompletion completion,
        string reason,
        BrowserStopSignal? signal = null) =>
        new(completion, signal ?? BrowserStopSignal.None, 0, 0, 0, 0, 0, 0, reason, []);

    private sealed record DiscoveryResumeState(
        string AdapterVersion,
        string? Continuation,
        int PageOrScrollMarker,
        IReadOnlyList<string> SeenProfileKeys,
        string LinkSetHash,
        int Viewed,
        int Created,
        int Known)
    {
        public static DiscoveryResumeState Empty(string version) => new(version, null, 0, [], string.Empty, 0, 0, 0);
    }
}

/// <summary>Produces the recommended completion-delay discovery schedule values.</summary>
public static class FounderScoutDiscoverySchedulePolicy
{
    /// <summary>Builds the recommended fixed-delay schedule definition.</summary>
    public static FixedDelayScheduleDefinition CreateDefinition(
        FounderScoutDiscoverySettings settings,
        DateTimeOffset? initialDueAtUtc = null) =>
        new(TimeSpan.FromSeconds(settings.CooldownAfterCompletionSeconds), initialDueAtUtc, StartImmediately: true);

    /// <summary>Returns a Runner timeout with a bounded shutdown margin.</summary>
    public static int GetTimeoutSeconds(FounderScoutDiscoverySettings settings) =>
        checked(settings.MaxRuntimeSeconds + 120);
}
