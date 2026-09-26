using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>Explicit adapter resolution with a safe schema-gated generic fallback.</summary>
public sealed class AgentOnboardingAdapterRegistry : IAgentOnboardingAdapterResolver
{
    private readonly Dictionary<AgentId, IAgentOnboardingAdapter> explicitAdapters;
    private readonly IAgentOnboardingAdapter genericAdapter;

    /// <summary>Creates a registry from explicitly composed adapters.</summary>
    public AgentOnboardingAdapterRegistry(
        IEnumerable<IAgentOnboardingAdapter> explicitAdapters,
        IAgentOnboardingAdapter genericAdapter)
    {
        ArgumentNullException.ThrowIfNull(explicitAdapters);
        ArgumentNullException.ThrowIfNull(genericAdapter);
        IAgentOnboardingAdapter[] adapters = explicitAdapters.ToArray();
        if (adapters.Any(item => item.Descriptor.SupportedAgentId is null))
        {
            throw new ArgumentException("Every explicit onboarding adapter must name one supported agent.", nameof(explicitAdapters));
        }

        this.explicitAdapters = adapters.ToDictionary(
            item => item.Descriptor.SupportedAgentId!.Value,
            item => item);
        this.genericAdapter = genericAdapter;
    }

    /// <inheritdoc />
    public IAgentOnboardingAdapter? Resolve(AgentId agentId, GenericConfigurationSchema? schema)
    {
        if (explicitAdapters.TryGetValue(agentId, out IAgentOnboardingAdapter? adapter))
        {
            return adapter;
        }

        return schema?.SupportsGenericEditor == true ? genericAdapter : null;
    }
}

/// <summary>Safe generic adapter backed by the existing schema validator, configuration service, and secret store.</summary>
public sealed class GenericAgentOnboardingAdapter(
    IAgentConfigurationService configurations,
    IAgentConfigurationValidator validator,
    ISecretStore secrets) : IAgentOnboardingAdapter
{
    /// <inheritdoc />
    public AgentOnboardingAdapterDescriptor Descriptor { get; } = new(
        "platform.generic-schema",
        "1.0",
        SupportedAgentId: null,
        AgentOnboardingSupport.SafeGeneric,
        CanConfigure: true,
        [
            new("generic.configuration", "Review configuration", "Review the safe primitive fields from the installed schema."),
            new("generic.secrets", "Protected secrets", "Set required protected values separately for the current Windows user."),
            new("generic.review", "Ready for validation", "Review the immutable configuration identity before later diagnostics."),
        ]);

    /// <inheritdoc />
    public async ValueTask<AgentOnboardingAssessment> AssessAsync(
        AgentOnboardingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        GenericConfigurationSchema schema = context.Package.ConfigurationSchema
            ?? throw new InvalidOperationException("The generic configuration schema is unavailable.");
        AgentConfigurationRecord? configuration = context.Configuration;
        IReadOnlyList<ConfigurationValidationError> errors = configuration is null
            ? [new("configuration.missing", "$", "No current configuration revision exists.")]
            : await ValidateCurrentAsync(configuration, cancellationToken).ConfigureAwait(false);
        JsonElement values = configuration is null
            ? schema.CreateDefaultDocument()
            : ParseConfiguration(configuration.CurrentRevision.CanonicalConfigurationJson);
        var secretStatuses = new List<AgentOnboardingSecretStatus>();
        var prerequisites = new List<AgentOnboardingPrerequisite>();
        foreach (GenericConfigurationField field in schema.Fields.Where(item => item.IsSecretReference))
        {
            if (!values.TryGetProperty(field.Name, out JsonElement referenceValue)
                || referenceValue.ValueKind != JsonValueKind.String
                || !SecretReference.TryParse(referenceValue.GetString(), out SecretReference reference))
            {
                secretStatuses.Add(new(field.Name, field.Title, default, field.Required, false, "onboarding.secret-reference-invalid"));
                prerequisites.Add(new(
                    $"secret.{field.Name}",
                    field.Title,
                    field.Required ? OnboardingCheckStatus.Blocked : OnboardingCheckStatus.Warning,
                    "onboarding.secret-reference-invalid",
                    "The configured opaque secret reference is invalid.",
                    field.Required));
                continue;
            }

            try
            {
                bool configured = await secrets.ExistsAsync(reference, cancellationToken).ConfigureAwait(false);
                secretStatuses.Add(new(
                    field.Name,
                    field.Title,
                    reference,
                    field.Required,
                    configured,
                    configured ? "onboarding.secret-configured" : "onboarding.secret-missing"));
                prerequisites.Add(new(
                    $"secret.{field.Name}",
                    field.Title,
                    configured ? OnboardingCheckStatus.Passed : field.Required ? OnboardingCheckStatus.Blocked : OnboardingCheckStatus.Warning,
                    configured ? "onboarding.secret-configured" : "onboarding.secret-missing",
                    configured ? "The protected value is configured." : "The protected value is not configured.",
                    field.Required));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                secretStatuses.Add(new(field.Name, field.Title, reference, field.Required, null, "onboarding.secret-status-unavailable"));
                prerequisites.Add(new(
                    $"secret.{field.Name}",
                    field.Title,
                    OnboardingCheckStatus.Unknown,
                    "onboarding.secret-status-unavailable",
                    "Protected secret status could not be checked safely.",
                    field.Required));
            }
        }

        bool configuredSuccessfully = configuration is not null && errors.Count == 0;
        bool ready = configuredSuccessfully
            && prerequisites.All(item => !item.Blocking || item.Status == OnboardingCheckStatus.Passed);
        bool reviewedCurrent = context.Selection is not null
            && string.Equals(context.Selection.SavedConfigurationHash, configuration?.CurrentRevision.ConfigurationHash, StringComparison.Ordinal)
            && string.Equals(context.Selection.ReviewedConfigurationSchemaVersion, schema.SchemaVersion, StringComparison.Ordinal);
        string reason = !configuredSuccessfully
            ? errors[0].Code
            : !ready
                ? prerequisites.First(item => item.Blocking && item.Status != OnboardingCheckStatus.Passed).ReasonCode
                : reviewedCurrent ? "onboarding.agent-reviewed" : "onboarding.agent-review-required";
        return new(
            configuredSuccessfully,
            RequiresReview: !reviewedCurrent,
            ReadyForValidation: ready,
            reason,
            !configuredSuccessfully
                ? "The current configuration does not pass the installed schema."
                : !ready
                    ? "One or more required protected values need attention."
                    : reviewedCurrent
                        ? "The current configuration was reviewed and its prerequisites pass."
                        : "The current valid configuration still needs explicit owner review.",
            prerequisites,
            secretStatuses,
            new("diagnose", "{}", context.Definition.RequiresInteractiveUserSession),
            new(
                context.Definition.SupportsScheduling,
                context.Definition.SupportsManualRun,
                context.Definition.RequiresInteractiveUserSession,
                ParseCommands(context.Definition.SupportedCommandsJson)),
            ready ? "Configuration is ready for a later diagnostic; the agent remains disabled." : "Configuration needs attention before diagnostics.");
    }

    /// <inheritdoc />
    public ValueTask<AgentConfigurationRecord?> LoadConfigurationAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default) => configurations.GetCurrentAsync(agentId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SaveConfigurationResult> SaveConfigurationAsync(
        SaveConfigurationRequest request,
        CancellationToken cancellationToken = default) => configurations.SaveAsync(request, cancellationToken);

    private async ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateCurrentAsync(
        AgentConfigurationRecord configuration,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(configuration.CurrentRevision.CanonicalConfigurationJson);
        return await validator.ValidateAsync(
            configuration.AgentId,
            configuration.SchemaVersion,
            document.RootElement,
            cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement ParseConfiguration(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string[] ParseCommands(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Explicit Stage 19 placeholder for a built-in adapter delivered by a later stage.</summary>
public sealed class PendingSpecializedAgentOnboardingAdapter(
    AgentId supportedAgentId,
    string adapterId,
    string nextStage,
    IAgentConfigurationService configurations) : IAgentOnboardingAdapter
{
    /// <inheritdoc />
    public AgentOnboardingAdapterDescriptor Descriptor { get; } = new(
        adapterId,
        "0.1-pending",
        supportedAgentId,
        AgentOnboardingSupport.Specialized,
        CanConfigure: false,
        [new("specialized.pending", "Guided setup arrives next", $"The specialized {nextStage} workflow is not part of Stage 19.")]);

    /// <inheritdoc />
    public ValueTask<AgentOnboardingAssessment> AssessAsync(
        AgentOnboardingContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> commands;
        try
        {
            commands = JsonSerializer.Deserialize<string[]>(context.Definition.SupportedCommandsJson) ?? [];
        }
        catch (JsonException)
        {
            commands = [];
        }

        return ValueTask.FromResult(new AgentOnboardingAssessment(
            context.Configuration is not null,
            RequiresReview: true,
            ReadyForValidation: false,
            "onboarding.specialized-setup-pending",
            $"Guided setup for this agent arrives in {nextStage}. Existing Agent Settings remain available.",
            [],
            [],
            null,
            new(
                context.Definition.SupportsScheduling,
                context.Definition.SupportsManualRun,
                context.Definition.RequiresInteractiveUserSession,
                commands),
            "Specialized setup has not been completed. The agent remains unchanged."));
    }

    /// <inheritdoc />
    public ValueTask<AgentConfigurationRecord?> LoadConfigurationAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        if (agentId != supportedAgentId)
        {
            throw new InvalidOperationException("The specialized adapter does not support this agent.");
        }

        return configurations.GetCurrentAsync(agentId, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<SaveConfigurationResult> SaveConfigurationAsync(
        SaveConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException($"This specialized onboarding configuration is delivered by {nextStage}.");
    }
}
