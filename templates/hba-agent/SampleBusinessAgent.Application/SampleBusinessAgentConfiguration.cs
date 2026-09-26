using System.Text.Json;
using HomeBusinessAssistant.Application.Secrets;

namespace SampleBusinessAgent.Application;

/// <summary>Typed, bounded settings for the local sample folder report.</summary>
public sealed record SampleBusinessAgentConfiguration(
    string SchemaVersion,
    string SourceRelativePath,
    int MaximumFiles,
    IReadOnlyList<string> IncludedExtensions,
    bool RequireAtLeastOneFile,
    string NotificationSecretReference)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The schema version implemented by this sample.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Parses and validates one configuration document without performing I/O.</summary>
    public static SampleBusinessAgentConfigurationParseResult Parse(JsonElement value)
    {
        try
        {
            SampleBusinessAgentConfiguration? configuration = value.Deserialize<SampleBusinessAgentConfiguration>(JsonOptions);
            if (configuration is null) return Invalid("sample.configuration.empty", "The sample configuration is empty.");
            var errors = new List<string>();
            if (configuration.SchemaVersion != CurrentSchemaVersion) errors.Add("sample.configuration.schemaVersion");
            if (!IsSafeRelativePath(configuration.SourceRelativePath)) errors.Add("sample.configuration.sourceRelativePath");
            if (configuration.MaximumFiles is < 1 or > 500) errors.Add("sample.configuration.maximumFiles");
            if (configuration.IncludedExtensions.Count is < 1 or > 20
                || configuration.IncludedExtensions.Any(extension => extension.Length is < 2 or > 16 || extension[0] != '.' || extension.Skip(1).Any(character => !char.IsLetterOrDigit(character))))
                errors.Add("sample.configuration.includedExtensions");
            if (!SecretReference.TryParse(configuration.NotificationSecretReference, out _)) errors.Add("sample.configuration.notificationSecretReference");
            return errors.Count == 0 ? new(configuration, []) : new(null, errors);
        }
        catch (JsonException)
        {
            return Invalid("sample.configuration.json", "The sample configuration JSON is invalid.");
        }
    }

    private static bool IsSafeRelativePath(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 256
        && !Path.IsPathRooted(value)
        && value.Split(['/', '\\']).All(segment => segment.Length > 0 && segment is not "." and not "..");

    private static SampleBusinessAgentConfigurationParseResult Invalid(string code, string _) => new(null, [code]);
}

/// <summary>The safe result of configuration parsing.</summary>
public sealed record SampleBusinessAgentConfigurationParseResult(
    SampleBusinessAgentConfiguration? Configuration,
    IReadOnlyList<string> ErrorCodes);
