using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Configuration;

namespace FounderScout.Application;

/// <summary>The Stage 09 capture envelope shared with later source adapters.</summary>
public sealed record FounderScoutCaptureEnvelope(
    string CaptureSchemaVersion,
    string Source,
    string SourceAccountId,
    string SourceSegmentId,
    string SourceProfileKey,
    string? ProfileUrl,
    DateTimeOffset CapturedAtUtc,
    string DisplayName,
    string RawText,
    JsonElement StructuredFields,
    string SourceAdapterVersion,
    string? LastSeenText = null,
    decimal ExtractionCompleteness = 0.5m,
    IReadOnlyList<string>? ExtractionWarnings = null,
    string? SourcePageFingerprint = null,
    string? RawHtml = null)
{
    /// <summary>The supported capture/import envelope version.</summary>
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>Loads bounded local fixture envelopes from an allowed import root.</summary>
public interface IFounderScoutFixtureReader
{
    /// <summary>Loads and validates one JSON, JSONL, or text fixture file.</summary>
    ValueTask<IReadOnlyList<FounderScoutCaptureEnvelope>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>The durable result of atomically writing one raw capture artifact.</summary>
public sealed record FounderScoutRawArtifact(
    string RelativePath,
    string FileHash,
    long FileSize,
    bool Created);

/// <summary>Writes raw captures beneath the Founder Scout data root.</summary>
public interface IFounderScoutRawArtifactStore
{
    /// <summary>Atomically writes or reuses a content-addressed raw capture.</summary>
    ValueTask<FounderScoutRawArtifact> WriteAsync(
        Guid candidateId,
        string contentHash,
        FounderScoutCaptureEnvelope capture,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads one root-confined immutable capture for deterministic processing.</summary>
public interface IFounderScoutRawArtifactReader
{
    /// <summary>Reads and validates one capture artifact beneath the configured data root.</summary>
    ValueTask<FounderScoutCaptureEnvelope> ReadAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>Input for one fixture import correlated to a central Runner run.</summary>
public sealed record FounderScoutImportRequest(
    string InputPath,
    Guid RunId,
    string CorrelationId,
    int RawProfileRetentionDays);

/// <summary>Deterministic metrics from one fixture import.</summary>
public sealed record FounderScoutImportResult(
    int CapturesRead,
    int CandidatesCreated,
    int CandidatesMatched,
    int SnapshotsCreated,
    int DuplicateSnapshots,
    int RawArtifactsCreated,
    IReadOnlyList<Guid> CandidateIds);

/// <summary>Input for committing one already validated source capture.</summary>
public sealed record FounderScoutCaptureCommitRequest(
    FounderScoutCaptureEnvelope Capture,
    Guid RunId,
    string CorrelationId,
    int RawProfileRetentionDays);

/// <summary>Result of one durable artifact-before-snapshot capture commit.</summary>
public sealed record FounderScoutCaptureCommitResult(
    Guid CandidateId,
    bool CandidateCreated,
    bool SnapshotCreated,
    bool RawArtifactCreated,
    string ContentHash,
    Guid SnapshotId);

/// <summary>Commits one capture before downstream analysis using Stage 09 identities.</summary>
public sealed class FounderScoutCaptureCommitService(
    IFounderScoutRawArtifactStore artifactStore,
    ICandidateRepository candidates,
    IProfileSnapshotRepository snapshots)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
    private readonly IFounderScoutRawArtifactStore artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    private readonly ICandidateRepository candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
    private readonly IProfileSnapshotRepository snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));

    /// <summary>Commits one capture without holding a transaction around source I/O.</summary>
    public async ValueTask<FounderScoutCaptureCommitResult> CommitAsync(
        FounderScoutCaptureCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        FounderScoutCaptureEnvelope capture = request.Capture ?? throw new ArgumentException("A capture is required.", nameof(request));
        if (request.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > 128
            || request.RawProfileRetentionDays is < 1 or > 365)
        {
            throw new ArgumentException("The Founder Scout capture commit request is invalid.", nameof(request));
        }

        string sourceKey = FounderScoutIdentityNormalizer.NormalizeSourceKey(capture.SourceProfileKey);
        string? url = FounderScoutIdentityNormalizer.NormalizeUrl(capture.ProfileUrl);
        var identities = new List<CandidateIdentityInput>
        {
            new(
                CandidateIdentityAliasType.SourceKey,
                FounderScoutIdentityNormalizer.Hash("source-key", sourceKey),
                capture.SourceAccountId,
                1m,
                IsStrong: true),
        };
        if (url is not null)
        {
            identities.Add(new(
                CandidateIdentityAliasType.CanonicalUrl,
                FounderScoutIdentityNormalizer.Hash("canonical-url", url),
                capture.SourceAccountId,
                1m,
                IsStrong: true));
        }

        ResolveCandidateResult resolved = await candidates.ResolveAsync(new(
            Guid.NewGuid(),
            sourceKey,
            url,
            capture.DisplayName,
            capture.CapturedAtUtc,
            identities,
            request.RunId,
            request.CorrelationId), cancellationToken).ConfigureAwait(false);
        string contentHash = CalculateContentHash(capture);
        string artifactHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(capture, JsonOptions)));
        FounderScoutRawArtifact artifact = await artifactStore.WriteAsync(
            resolved.Candidate.Id,
            artifactHash,
            capture,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset deleteAfterUtc = capture.CapturedAtUtc.ToUniversalTime().AddDays(request.RawProfileRetentionDays);
        decimal completeness = capture.ExtractionCompleteness is >= 0 and <= 1
            ? capture.ExtractionCompleteness
            : throw new InvalidDataException("Capture completeness must be between zero and one.");
        AddProfileSnapshotResult snapshot = await snapshots.AddIfNewAsync(new(
            Guid.NewGuid(),
            resolved.Candidate.Id,
            capture.SourceAccountId,
            capture.SourceSegmentId,
            sourceKey,
            url,
            contentHash,
            capture.Source == "fixture" ? "fixture-envelope-1.0" : "browser-capture-1.0",
            capture.SourceAdapterVersion,
            capture.CapturedAtUtc,
            artifact.RelativePath,
            deleteAfterUtc,
            null,
            completeness,
            completeness,
            ProfileSnapshotStatus.Captured,
            request.RunId,
            request.CorrelationId), cancellationToken).ConfigureAwait(false);
        return new(resolved.Candidate.Id, resolved.Created, snapshot.Created, artifact.Created, contentHash, snapshot.Snapshot.Id);
    }

    private static string CalculateContentHash(FounderScoutCaptureEnvelope capture)
    {
        JsonElement content = JsonSerializer.SerializeToElement(new
        {
            rawText = capture.RawText.Replace("\r\n", "\n", StringComparison.Ordinal),
            structuredFields = capture.StructuredFields,
            capture.SourceAdapterVersion,
            capture.SourcePageFingerprint,
        }, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(content))));
    }
}

/// <summary>Validates and persists fixture captures in artifact-before-queue order.</summary>
public sealed class FounderScoutImportService(
    IFounderScoutFixtureReader fixtureReader,
    IFounderScoutRawArtifactStore artifactStore,
    ICandidateRepository candidates,
    IProfileSnapshotRepository snapshots,
    TimeProvider timeProvider)
{
    private readonly IFounderScoutFixtureReader fixtureReader = fixtureReader ?? throw new ArgumentNullException(nameof(fixtureReader));
    private readonly IFounderScoutRawArtifactStore artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
    private readonly ICandidateRepository candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
    private readonly IProfileSnapshotRepository snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Imports one bounded fixture file idempotently.</summary>
    public async ValueTask<FounderScoutImportResult> ImportAsync(
        FounderScoutImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > 128
            || request.RawProfileRetentionDays is < 1 or > 365)
        {
            throw new ArgumentException("The Founder Scout import request is invalid.", nameof(request));
        }

        IReadOnlyList<FounderScoutCaptureEnvelope> captures = await fixtureReader.ReadAsync(
            request.InputPath,
            cancellationToken).ConfigureAwait(false);
        var candidateIds = new HashSet<Guid>();
        var candidatesCreated = 0;
        var candidatesMatched = 0;
        var snapshotsCreated = 0;
        var duplicateSnapshots = 0;
        var rawArtifactsCreated = 0;
        var commitService = new FounderScoutCaptureCommitService(artifactStore, candidates, snapshots);
        foreach (FounderScoutCaptureEnvelope capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FounderScoutCaptureCommitResult committed = await commitService.CommitAsync(new(
                capture,
                request.RunId,
                request.CorrelationId,
                request.RawProfileRetentionDays), cancellationToken).ConfigureAwait(false);
            candidateIds.Add(committed.CandidateId);
            if (committed.CandidateCreated)
            {
                candidatesCreated++;
            }
            else
            {
                candidatesMatched++;
            }

            if (committed.RawArtifactCreated)
            {
                rawArtifactsCreated++;
            }
            if (committed.SnapshotCreated)
            {
                snapshotsCreated++;
            }
            else
            {
                duplicateSnapshots++;
            }
        }

        _ = timeProvider.GetUtcNow();
        return new(
            captures.Count,
            candidatesCreated,
            candidatesMatched,
            snapshotsCreated,
            duplicateSnapshots,
            rawArtifactsCreated,
            candidateIds.Order().ToArray());
    }

}

/// <summary>Stable source-key, URL, and identity hashing rules.</summary>
public static class FounderScoutIdentityNormalizer
{
    /// <summary>Normalizes a source key without inventing identity.</summary>
    public static string NormalizeSourceKey(string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Length > 512 || sourceKey.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded source profile key is required.", nameof(sourceKey));
        }

        return sourceKey.Trim().ToLowerInvariant();
    }

    /// <summary>Normalizes an optional HTTP(S) profile URL for strong identity matching.</summary>
    public static string? NormalizeUrl(string? profileUrl)
    {
        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return null;
        }

        if (profileUrl.Length > 2_048
            || !Uri.TryCreate(profileUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The fixture profile URL is invalid.", nameof(profileUrl));
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty,
            Host = uri.IdnHost.ToLowerInvariant(),
        };
        string normalized = builder.Uri.AbsoluteUri;
        return normalized[^1] == '/' && builder.Uri.AbsolutePath != "/"
            ? normalized.TrimEnd('/')
            : normalized;
    }

    /// <summary>Hashes one typed normalized identity with a domain separator.</summary>
    public static string Hash(string kind, string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}\n{normalizedValue}")));
    }
}
