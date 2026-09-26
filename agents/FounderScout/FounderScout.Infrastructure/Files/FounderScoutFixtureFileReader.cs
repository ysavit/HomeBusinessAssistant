using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Infrastructure.Files;

/// <summary>Reads bounded fixture files only from the configured Founder Scout import root.</summary>
public sealed class FounderScoutFixtureFileReader : IFounderScoutFixtureReader
{
    private const long MaximumFileBytes = 2 * 1024 * 1024;
    private const int MaximumCaptures = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly HashSet<string> ForbiddenStructuredProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "cookie", "cookies", "accessToken", "refreshToken", "authorization",
        "age", "birthDate", "gender", "race", "ethnicity", "religion", "photo", "image",
        "maritalStatus", "health", "disability",
    };

    private readonly string importsRoot;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates a reader confined to one initialized imports root.</summary>
    public FounderScoutFixtureFileReader(string importsRoot, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importsRoot);
        this.importsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(importsRoot));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FounderScoutCaptureEnvelope>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = ValidateInputPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is < 1 or > MaximumFileBytes)
        {
            throw new InvalidDataException("The Founder Scout fixture file size is outside supported bounds.");
        }

        string extension = info.Extension.ToLowerInvariant();
        IReadOnlyList<FounderScoutCaptureEnvelope> captures = extension switch
        {
            ".json" => await ReadJsonAsync(fullPath, cancellationToken).ConfigureAwait(false),
            ".jsonl" => await ReadJsonLinesAsync(fullPath, cancellationToken).ConfigureAwait(false),
            ".txt" => [await ReadTextAsync(fullPath, cancellationToken).ConfigureAwait(false)],
            _ => throw new InvalidDataException("Founder Scout imports support only .json, .jsonl, and .txt fixtures."),
        };
        if (captures.Count is < 1 or > MaximumCaptures)
        {
            throw new InvalidDataException("The Founder Scout fixture capture count is outside supported bounds.");
        }

        foreach (FounderScoutCaptureEnvelope capture in captures)
        {
            ValidateCapture(capture);
        }

        return captures;
    }

    private static async ValueTask<IReadOnlyList<FounderScoutCaptureEnvelope>> ReadJsonAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return [Deserialize(document.RootElement)];
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("A Founder Scout JSON fixture must contain an envelope object or envelope array.");
        }

        if (document.RootElement.GetArrayLength() > MaximumCaptures)
        {
            throw new InvalidDataException("The Founder Scout fixture contains too many captures.");
        }

        return document.RootElement.EnumerateArray().Select(Deserialize).ToArray();
    }

    private static async ValueTask<IReadOnlyList<FounderScoutCaptureEnvelope>> ReadJsonLinesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var captures = new List<FounderScoutCaptureEnvelope>();
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (captures.Count >= MaximumCaptures || line.Length > 262_144)
            {
                throw new InvalidDataException("The Founder Scout JSONL fixture is outside supported bounds.");
            }

            using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
            captures.Add(Deserialize(document.RootElement));
        }

        return captures;
    }

    private async ValueTask<FounderScoutCaptureEnvelope> ReadTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string rawText = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        string sourceKey = Path.GetFileNameWithoutExtension(path).Trim().ToLowerInvariant();
        string displayName = rawText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? sourceKey;
        using JsonDocument empty = JsonDocument.Parse("{}");
        return new(
            FounderScoutCaptureEnvelope.CurrentSchemaVersion,
            "fixture",
            "fixture-account",
            "fixture-segment",
            sourceKey,
            null,
            timeProvider.GetUtcNow().ToUniversalTime(),
            displayName[..Math.Min(displayName.Length, 200)],
            rawText,
            empty.RootElement.Clone(),
            "text-fixture-1.0");
    }

    private string ValidateInputPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException("The Founder Scout fixture path must be absolute.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(importsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Founder Scout fixture must be beneath the configured imports directory.");
        }

        FounderScoutPathPolicy.RejectReparsePoint(importsRoot);
        string? current = Path.GetDirectoryName(fullPath);
        while (current is not null && current.StartsWith(importsRoot, StringComparison.OrdinalIgnoreCase))
        {
            FounderScoutPathPolicy.RejectReparsePoint(current);
            if (string.Equals(current, importsRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        if (File.Exists(fullPath))
        {
            FounderScoutPathPolicy.RejectReparsePoint(fullPath);
        }

        return fullPath;
    }

    private void ValidateCapture(FounderScoutCaptureEnvelope capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        if (capture.CaptureSchemaVersion != FounderScoutCaptureEnvelope.CurrentSchemaVersion
            || capture.Source != "fixture"
            || !IsStableId(capture.SourceAccountId, 64)
            || !IsStableId(capture.SourceSegmentId, 64)
            || !IsStableId(capture.SourceProfileKey, 512)
            || string.IsNullOrWhiteSpace(capture.DisplayName)
            || capture.DisplayName.Length > 200
            || capture.DisplayName.Any(char.IsControl)
            || capture.RawText.Length > 65_536
            || capture.RawText.Contains('\0')
            || capture.ExtractionCompleteness is < 0 or > 1
            || capture.LastSeenText?.Length > 500
            || capture.SourcePageFingerprint?.Length > 128
            || capture.RawHtml?.Length > 1_000_000
            || capture.RawHtml?.Contains('\0') == true
            || capture.ExtractionWarnings is { Count: > 50 }
            || capture.ExtractionWarnings?.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 200 || item.Any(char.IsControl)) == true
            || !IsStableId(capture.SourceAdapterVersion, 128)
            || capture.CapturedAtUtc == default
            || capture.CapturedAtUtc.Offset != TimeSpan.Zero
            || capture.CapturedAtUtc > nowUtc.AddMinutes(5)
            || capture.StructuredFields.ValueKind != JsonValueKind.Object
            || capture.StructuredFields.GetRawText().Length > 65_536)
        {
            throw new InvalidDataException("A Founder Scout capture envelope is invalid or outside supported bounds.");
        }

        _ = FounderScoutIdentityNormalizer.NormalizeUrl(capture.ProfileUrl);
        ValidateStructuredProperties(capture.StructuredFields);
    }

    private static void ValidateStructuredProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (ForbiddenStructuredProperties.Contains(property.Name))
                {
                    throw new InvalidDataException("Fixture structured fields contain a forbidden sensitive or protected property.");
                }

                ValidateStructuredProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateStructuredProperties(item);
            }
        }
    }

    private static FounderScoutCaptureEnvelope Deserialize(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Every Founder Scout capture must be a JSON object.");
        }

        try
        {
            return element.Deserialize<FounderScoutCaptureEnvelope>(JsonOptions)
                ?? throw new InvalidDataException("The Founder Scout capture envelope is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Founder Scout capture envelope shape is invalid.", exception);
        }
    }

    private static bool IsStableId(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);
}
