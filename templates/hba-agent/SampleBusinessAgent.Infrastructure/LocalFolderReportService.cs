using System.Text.Json;
using SampleBusinessAgent.Application;

namespace SampleBusinessAgent.Infrastructure;

/// <summary>One bounded local-file observation included in the sample report.</summary>
public sealed record SampleFileObservation(string Name, long SizeBytes, DateTimeOffset LastWriteAtUtc);

/// <summary>The result written to the assigned run artifact directory.</summary>
public sealed record SampleFolderReport(
    string SourceRelativePath,
    int MatchingFiles,
    bool Truncated,
    IReadOnlyList<SampleFileObservation> Files,
    string ArtifactRelativePath);

/// <summary>Creates a harmless top-level file metadata report beneath Runner-assigned roots.</summary>
public sealed class LocalFolderReportService
{
    /// <summary>Creates the report and returns its bounded metadata.</summary>
    public static async ValueTask<SampleFolderReport> CreateAsync(
        SampleBusinessAgentConfiguration configuration,
        string dataDirectory,
        string artifactDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string dataRoot = ValidateRoot(dataDirectory, nameof(dataDirectory));
        string artifactRoot = ValidateRoot(artifactDirectory, nameof(artifactDirectory));
        string source = CombineContained(dataRoot, configuration.SourceRelativePath);
        Directory.CreateDirectory(source);
        RejectReparse(source);

        HashSet<string> extensions = configuration.IncludedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var observations = new List<SampleFileObservation>();
        bool truncated = false;
        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!extensions.Contains(Path.GetExtension(path))) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            if (observations.Count == configuration.MaximumFiles)
            {
                truncated = true;
                break;
            }

            var file = new FileInfo(path);
            observations.Add(new(file.Name, file.Length, file.LastWriteTimeUtc));
        }

        const string artifactName = "sample-business-report.json";
        string artifactPath = CombineContained(artifactRoot, artifactName);
        var report = new SampleFolderReport(
            configuration.SourceRelativePath,
            observations.Count,
            truncated,
            observations,
            artifactName);
        await using FileStream stream = new(
            artifactPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, report, cancellationToken: cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return report;
    }

    private static string ValidateRoot(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("A Runner-assigned directory does not exist.");
        RejectReparse(full);
        return full;
    }

    private static string CombineContained(string root, string relative)
    {
        string candidate = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The sample path escapes its assigned directory.");
        return candidate;
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The sample does not traverse reparse points.");
    }
}
