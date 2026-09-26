using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Manifest;

/// <summary>Validates typed agent manifests without performing file-system side effects.</summary>
public static class AgentManifestValidator
{
    /// <summary>The minimum allowed default timeout.</summary>
    public const int MinimumTimeoutSeconds = 1;

    /// <summary>The maximum allowed default timeout.</summary>
    public const int MaximumTimeoutSeconds = 86_400;

    /// <summary>Validates a typed manifest.</summary>
    public static ContractValidationResult Validate(AgentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<ContractValidationError>();

        if (manifest.ManifestVersion != AgentProtocolVersion.CurrentManifest)
        {
            Add(errors, "manifest.unsupportedVersion", "$.manifestVersion", "The manifest version is not supported.");
        }

        if (!AgentId.TryParse(manifest.Id.Value, out _))
        {
            Add(errors, "manifest.invalidAgentId", "$.id", "The agent identifier is invalid.");
        }

        ValidateText(manifest.DisplayName, 1, 100, "$.displayName", "displayName", errors);
        ValidateText(manifest.Description, 1, 1_000, "$.description", "description", errors);

        if (!AgentVersion.TryParse(manifest.Version.Value, out _))
        {
            Add(errors, "manifest.invalidAgentVersion", "$.version", "The agent version is invalid.");
        }

        if (!ContractNameRules.IsSafeRelativePath(manifest.Executable))
        {
            Add(errors, "manifest.invalidExecutable", "$.executable", "The executable must be a safe relative path without traversal segments.");
        }

        ValidateNames(manifest.SupportedCommands, "$.supportedCommands", "command", requireAtLeastOne: true, errors);
        ValidateNames(manifest.Capabilities, "$.capabilities", "capability", requireAtLeastOne: false, errors);

        if (manifest.DefaultTimeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
        {
            Add(errors, "manifest.invalidTimeout", "$.defaultTimeoutSeconds", $"The timeout must be between {MinimumTimeoutSeconds} and {MaximumTimeoutSeconds} seconds.");
        }

        if (!Enum.IsDefined(manifest.DefaultConcurrencyPolicy))
        {
            Add(errors, "manifest.invalidConcurrencyPolicy", "$.defaultConcurrencyPolicy", "The concurrency policy is invalid.");
        }

        if (manifest.ConfigurationSchemaVersion.Major < 1)
        {
            Add(errors, "manifest.invalidConfigurationSchemaVersion", "$.configurationSchemaVersion", "The configuration schema version must be 1.0 or later.");
        }

        return new ContractValidationResult(errors);
    }

    private static void ValidateText(
        string? value,
        int minimumLength,
        int maximumLength,
        string path,
        string name,
        List<ContractValidationError> errors)
    {
        if (value is null || value.Length < minimumLength || value.Length > maximumLength || string.IsNullOrWhiteSpace(value))
        {
            Add(errors, $"manifest.invalid{name[..1].ToUpperInvariant()}{name[1..]}", path, $"The {name} length is invalid.");
        }
    }

    private static void ValidateNames(
        IReadOnlyList<string>? values,
        string path,
        string kind,
        bool requireAtLeastOne,
        List<ContractValidationError> errors)
    {
        if (values is null || values.Count > 64 || (requireAtLeastOne && values.Count == 0))
        {
            Add(errors, $"manifest.invalid{kind[..1].ToUpperInvariant()}{kind[1..]}Count", path, $"The {kind} collection size is invalid.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            if (!ContractNameRules.IsValid(values[index]))
            {
                Add(errors, $"manifest.invalid{kind[..1].ToUpperInvariant()}{kind[1..]}", $"{path}[{index}]", $"The {kind} name is invalid.");
            }
            else if (!seen.Add(values[index]))
            {
                Add(errors, $"manifest.duplicate{kind[..1].ToUpperInvariant()}{kind[1..]}", $"{path}[{index}]", $"The {kind} name is duplicated.");
            }
        }
    }

    private static void Add(List<ContractValidationError> errors, string code, string path, string message) =>
        errors.Add(new ContractValidationError(code, path, message));
}
