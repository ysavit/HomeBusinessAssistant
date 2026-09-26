using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Configuration;

/// <summary>A safe validation error that does not contain configuration values.</summary>
public sealed record ConfigurationValidationError(string Code, string Path, string Message);

/// <summary>Validates an agent's versioned configuration schema before persistence.</summary>
public interface IAgentConfigurationValidator
{
    /// <summary>Returns structured errors for one configuration document.</summary>
    ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides baseline object/schema checks and a hook for future per-agent validators.</summary>
public sealed class BasicAgentConfigurationValidator : IAgentConfigurationValidator
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = agentId;
        var errors = new List<ConfigurationValidationError>();

        if (configuration.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new("configuration.rootMustBeObject", "$", "Configuration must be a JSON object."));
        }

        if (string.IsNullOrWhiteSpace(schemaVersion) || schemaVersion.Length > 32)
        {
            errors.Add(new("configuration.invalidSchemaVersion", "$.schemaVersion", "The schema version is invalid."));
        }

        return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>(errors);
    }
}

/// <summary>Runs baseline and agent-specific validators as one persistence boundary.</summary>
public sealed class CompositeAgentConfigurationValidator(
    IReadOnlyList<IAgentConfigurationValidator> validators) : IAgentConfigurationValidator
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validators);
        var errors = new List<ConfigurationValidationError>();
        foreach (IAgentConfigurationValidator validator in validators)
        {
            IReadOnlyList<ConfigurationValidationError> result = await validator.ValidateAsync(
                agentId,
                schemaVersion,
                configuration,
                cancellationToken).ConfigureAwait(false);
            errors.AddRange(result);
        }

        return errors;
    }
}

/// <summary>A built-in initial configuration which is installed only when an agent has no user data.</summary>
public sealed record AgentDefaultConfiguration(
    AgentId AgentId,
    string SchemaVersion,
    JsonElement Configuration,
    string ChangeSummary);

/// <summary>Supplies built-in agent defaults without coupling persistence to agent assemblies.</summary>
public interface IAgentDefaultConfigurationProvider
{
    /// <summary>Gets the immutable built-in default document.</summary>
    AgentDefaultConfiguration GetDefault();
}

/// <summary>Seeds an initial immutable revision without overwriting an existing current revision.</summary>
public sealed class AgentDefaultConfigurationSeeder(IAgentConfigurationService configurations)
{
    /// <summary>Seeds one provider and reports whether a revision was created.</summary>
    public async ValueTask<bool> SeedIfMissingAsync(
        IAgentDefaultConfigurationProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        AgentDefaultConfiguration value = provider.GetDefault();
        if (await configurations.GetCurrentAsync(value.AgentId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        SaveConfigurationResult result = await configurations.SaveAsync(new(
            value.AgentId,
            value.SchemaVersion,
            value.Configuration,
            ChangedBy: "system-bootstrap",
            value.ChangeSummary,
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        return result.Created;
    }
}

/// <summary>Raised when configuration cannot be safely validated or persisted.</summary>
public sealed class ConfigurationValidationException : Exception
{
    /// <summary>Creates an exception containing safe structured errors.</summary>
    public ConfigurationValidationException(IReadOnlyList<ConfigurationValidationError> errors)
        : base("The agent configuration is invalid.") => Errors = errors;

    /// <summary>Gets validation errors that contain paths and safe messages only.</summary>
    public IReadOnlyList<ConfigurationValidationError> Errors { get; }
}

/// <summary>Raised when an immutable configuration editor posts against a stale current revision.</summary>
public sealed class ConfigurationConcurrencyException : Exception
{
    /// <summary>Creates a safe conflict which contains revision identity, never configuration content.</summary>
    public ConfigurationConcurrencyException(long? expectedRevisionNumber, long? actualRevisionNumber)
        : base("The configuration changed after this editor was opened. Reload before saving again.")
    {
        ExpectedRevisionNumber = expectedRevisionNumber;
        ActualRevisionNumber = actualRevisionNumber;
    }

    /// <summary>Gets the revision displayed by the stale editor.</summary>
    public long? ExpectedRevisionNumber { get; }

    /// <summary>Gets the current revision observed during the save transaction.</summary>
    public long? ActualRevisionNumber { get; }
}
