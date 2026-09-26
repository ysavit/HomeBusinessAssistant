using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Manifest;

/// <summary>Loads a UTF-8 JSON agent manifest and returns safe structured errors.</summary>
public static class AgentManifestLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    /// <summary>Loads and validates a manifest without exposing its contents in errors.</summary>
    public static async ValueTask<AgentManifestLoadResult> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return Failure("manifest.invalidPath", "$.file", "A manifest file path is required.");
        }

        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<ManifestJsonModel>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return document is null
                ? Failure("manifest.invalidJson", "$", "The manifest must contain one JSON object.")
                : CreateAndValidate(document);
        }
        catch (JsonException exception)
        {
            return Failure("manifest.invalidJson", exception.Path ?? "$", "The manifest contains invalid JSON or a value of the wrong type.");
        }
        catch (DecoderFallbackException)
        {
            return Failure("manifest.invalidUtf8", "$", "The manifest is not valid UTF-8.");
        }
        catch (IOException)
        {
            return Failure("manifest.ioFailure", "$.file", "The manifest file could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("manifest.accessDenied", "$.file", "Access to the manifest file was denied.");
        }
    }

    private static AgentManifestLoadResult CreateAndValidate(ManifestJsonModel document)
    {
        var errors = new List<ContractValidationError>();

        var manifestVersion = ParseVersion(document.ManifestVersion, "$.manifestVersion", "manifestVersion", errors);
        var agentId = ParseAgentId(document.Id, errors);
        var agentVersion = ParseAgentVersion(document.Version, errors);
        var configurationSchemaVersion = ParseVersion(
            document.ConfigurationSchemaVersion,
            "$.configurationSchemaVersion",
            "configurationSchemaVersion",
            errors);
        var concurrencyPolicy = ParseConcurrencyPolicy(document.DefaultConcurrencyPolicy, errors);

        AddRequiredStringError(document.DisplayName, "$.displayName", "displayName", errors);
        AddRequiredStringError(document.Description, "$.description", "description", errors);
        AddRequiredStringError(document.Executable, "$.executable", "executable", errors);
        AddRequiredCollectionError(document.SupportedCommands, "$.supportedCommands", "supportedCommands", errors);
        AddRequiredCollectionError(document.Capabilities, "$.capabilities", "capabilities", errors);
        AddRequiredValueError(document.DefaultTimeoutSeconds, "$.defaultTimeoutSeconds", "defaultTimeoutSeconds", errors);
        AddRequiredValueError(document.SupportsScheduling, "$.supportsScheduling", "supportsScheduling", errors);
        AddRequiredValueError(document.SupportsManualRun, "$.supportsManualRun", "supportsManualRun", errors);
        AddRequiredValueError(
            document.RequiresInteractiveUserSession,
            "$.requiresInteractiveUserSession",
            "requiresInteractiveUserSession",
            errors);

        if (errors.Count > 0)
        {
            return new AgentManifestLoadResult(null, errors);
        }

        var manifest = new AgentManifest(
            manifestVersion!.Value,
            agentId!.Value,
            document.DisplayName!,
            document.Description!,
            agentVersion!.Value,
            document.Executable!,
            document.SupportedCommands!.AsReadOnly(),
            document.Capabilities!.AsReadOnly(),
            document.DefaultTimeoutSeconds!.Value,
            concurrencyPolicy!.Value,
            document.SupportsScheduling!.Value,
            document.SupportsManualRun!.Value,
            document.RequiresInteractiveUserSession!.Value,
            configurationSchemaVersion!.Value);

        var validation = AgentManifestValidator.Validate(manifest);
        return validation.IsValid
            ? new AgentManifestLoadResult(manifest, Array.Empty<ContractValidationError>())
            : new AgentManifestLoadResult(null, validation.Errors);
    }

    private static AgentProtocolVersion? ParseVersion(
        string? value,
        string path,
        string name,
        List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, path, name);
            return null;
        }

        if (AgentProtocolVersion.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ContractValidationError("manifest.invalidVersion", path, $"The {name} value is invalid."));
        return null;
    }

    private static AgentId? ParseAgentId(string? value, List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, "$.id", "id");
            return null;
        }

        if (AgentId.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ContractValidationError("manifest.invalidAgentId", "$.id", "The agent identifier is invalid."));
        return null;
    }

    private static AgentVersion? ParseAgentVersion(string? value, List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, "$.version", "version");
            return null;
        }

        if (AgentVersion.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ContractValidationError("manifest.invalidAgentVersion", "$.version", "The agent version is invalid."));
        return null;
    }

    private static ConcurrencyPolicy? ParseConcurrencyPolicy(
        string? value,
        List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, "$.defaultConcurrencyPolicy", "defaultConcurrencyPolicy");
            return null;
        }

        if (Enum.TryParse<ConcurrencyPolicy>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors.Add(new ContractValidationError(
            "manifest.invalidConcurrencyPolicy",
            "$.defaultConcurrencyPolicy",
            "The concurrency policy is invalid."));
        return null;
    }

    private static void AddRequiredStringError(
        string? value,
        string path,
        string name,
        List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, path, name);
        }
    }

    private static void AddRequiredCollectionError(
        IReadOnlyCollection<string>? value,
        string path,
        string name,
        List<ContractValidationError> errors)
    {
        if (value is null)
        {
            AddRequired(errors, path, name);
        }
    }

    private static void AddRequiredValueError<T>(
        T? value,
        string path,
        string name,
        List<ContractValidationError> errors)
        where T : struct
    {
        if (!value.HasValue)
        {
            AddRequired(errors, path, name);
        }
    }

    private static void AddRequired(List<ContractValidationError> errors, string path, string name) =>
        errors.Add(new ContractValidationError("manifest.required", path, $"The {name} property is required."));

    private static AgentManifestLoadResult Failure(string code, string path, string message) =>
        new(null, [new ContractValidationError(code, path, message)]);

    private sealed class ManifestJsonModel
    {
        public string? ManifestVersion { get; init; }

        public string? Id { get; init; }

        public string? DisplayName { get; init; }

        public string? Description { get; init; }

        public string? Version { get; init; }

        public string? Executable { get; init; }

        public List<string>? SupportedCommands { get; init; }

        public List<string>? Capabilities { get; init; }

        public int? DefaultTimeoutSeconds { get; init; }

        public string? DefaultConcurrencyPolicy { get; init; }

        public bool? SupportsScheduling { get; init; }

        public bool? SupportsManualRun { get; init; }

        public bool? RequiresInteractiveUserSession { get; init; }

        public string? ConfigurationSchemaVersion { get; init; }
    }
}
