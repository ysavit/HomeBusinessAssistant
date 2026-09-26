using System.Security.Cryptography;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Infrastructure.Files;

/// <summary>Atomic content-addressed raw capture storage beneath the Founder Scout data root.</summary>
public sealed class FounderScoutRawArtifactStore(
    string dataDirectory,
    string snapshotsDirectory) : IFounderScoutRawArtifactStore, IFounderScoutRawArtifactReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
    private readonly string snapshotsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshotsDirectory));

    /// <inheritdoc />
    public async ValueTask<FounderScoutRawArtifact> WriteAsync(
        Guid candidateId,
        string contentHash,
        FounderScoutCaptureEnvelope capture,
        CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty
            || contentHash.Length != 64
            || !contentHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The raw artifact identity is invalid.", nameof(candidateId));
        }

        FounderScoutPathPolicy.RejectReparsePoint(dataDirectory);
        FounderScoutPathPolicy.RejectReparsePoint(snapshotsDirectory);
        string candidateDirectory = FounderScoutPathPolicy.CombineContained(
            snapshotsDirectory,
            capture.CapturedAtUtc.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture),
            capture.CapturedAtUtc.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            candidateId.ToString("N"));
        Directory.CreateDirectory(candidateDirectory);
        FounderScoutPathPolicy.RejectReparsePoint(candidateDirectory);
        string path = FounderScoutPathPolicy.CombineContained(candidateDirectory, $"{contentHash}.capture.json");
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(capture, JsonOptions);
        if (content.LongLength > 262_144)
        {
            throw new InvalidDataException("The serialized raw capture artifact is too large.");
        }

        bool created = false;
        if (File.Exists(path))
        {
            FounderScoutPathPolicy.RejectReparsePoint(path);
            byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(content))
            {
                throw new InvalidDataException("A content-addressed raw artifact does not match the existing file.");
            }
        }
        else
        {
            string temporary = FounderScoutPathPolicy.CombineContained(candidateDirectory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporary, path, overwrite: false);
                    created = true;
                }
                catch (IOException) when (File.Exists(path))
                {
                    File.Delete(temporary);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        string fileHash = Convert.ToHexStringLower(SHA256.HashData(content));
        return new(
            FounderScoutPathPolicy.GetRelativeContainedPath(dataDirectory, path),
            fileHash,
            content.LongLength,
            created);
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutCaptureEnvelope> ReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath) || relativePath.Length > 1_024)
        {
            throw new InvalidDataException("The raw artifact path is invalid.");
        }

        FounderScoutPathPolicy.RejectReparsePoint(dataDirectory);
        string path = FounderScoutPathPolicy.CombineContained(dataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The raw Founder Scout capture artifact does not exist.", path);
        }

        FounderScoutPathPolicy.RejectReparsePoint(path);
        var file = new FileInfo(path);
        if (file.Length is <= 0 or > 262_144)
        {
            throw new InvalidDataException("The raw capture artifact size is invalid.");
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        FounderScoutCaptureEnvelope? capture = await JsonSerializer.DeserializeAsync<FounderScoutCaptureEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return capture ?? throw new InvalidDataException("The raw capture artifact is invalid.");
    }
}
