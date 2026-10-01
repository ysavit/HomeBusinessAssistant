using System.Text.Json;

namespace FounderScout.SimpleCli;

/// <summary>Local settings for the two-command console. The source file is ignored by Git.</summary>
public sealed record SimpleScoutSettings(
    string DatabasePath,
    int MaxCandidatesPerRun,
    int ListLimit,
    int DelaySeconds,
    string Model,
    string? ApiKey,
    string? FounderContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Loads and validates settings without creating data files or printing secrets.</summary>
    public static async Task<(SimpleScoutSettings? Settings, string? Error)> LoadAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        string file = path ?? DefaultPath();
        if (!File.Exists(file))
        {
            return (null, $"Missing {file}. Copy appsettings.example.json to appsettings.json and edit it.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("SimpleScout", out JsonElement section))
            {
                return (null, "appsettings.json needs a SimpleScout section.");
            }
            SimpleScoutSettings? parsed = section.Deserialize<SimpleScoutSettings>(JsonOptions);
            if (parsed is null)
            {
                return (null, "appsettings.json has an invalid SimpleScout section.");
            }

            string expanded = Environment.ExpandEnvironmentVariables(parsed.DatabasePath ?? string.Empty);
            if (!Path.IsPathFullyQualified(expanded)
                || !Path.GetFileName(expanded).EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Path.GetDirectoryName(expanded)))
            {
                return (null, "SimpleScout:DatabasePath must be an absolute path to a .db file.");
            }
            if (parsed.MaxCandidatesPerRun is < 1 or > 5 || parsed.ListLimit is < 1 or > 100
                || parsed.DelaySeconds is < 1 or > 60)
            {
                return (null, "SimpleScout limits must be: MaxCandidatesPerRun 1..5, ListLimit 1..100, DelaySeconds 1..60.");
            }
            if (string.IsNullOrWhiteSpace(parsed.Model) || parsed.Model.Length > 128
                || !parsed.Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            {
                return (null, "SimpleScout:Model must be a valid model identifier (up to 128 characters).");
            }
            if (parsed.FounderContext is { Length: > 4000 })
            {
                return (null, "SimpleScout:FounderContext must be at most 4000 characters.");
            }

            return (parsed with { DatabasePath = Path.GetFullPath(expanded) }, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, "Could not read appsettings.json. Check that it is valid JSON and accessible.");
        }
    }

    private static string DefaultPath()
    {
        string source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "appsettings.json"));
        return File.Exists(Path.Combine(Path.GetDirectoryName(source)!, "FounderScout.SimpleCli.csproj"))
            ? source
            : Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }
}
