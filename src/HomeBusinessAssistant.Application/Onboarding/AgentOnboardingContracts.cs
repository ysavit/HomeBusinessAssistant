using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>The owner's durable choice for one agent in one onboarding session.</summary>
public enum OnboardingAgentSelectionStatus
{
    /// <summary>The owner selected the agent for this session.</summary>
    Selected = 0,
    /// <summary>The owner explicitly chose to configure the agent later.</summary>
    Deferred = 1,
    /// <summary>The package is currently invalid or cannot be configured safely.</summary>
    Unavailable = 2,
    /// <summary>The package disappeared after it had been included in the session.</summary>
    Removed = 3,
}

/// <summary>Durable per-agent progress which is independent from enablement.</summary>
public enum OnboardingAgentProgressStatus
{
    /// <summary>No agent-specific step has been started.</summary>
    NotStarted = 0,
    /// <summary>The owner is reviewing or editing the agent.</summary>
    Configuring = 1,
    /// <summary>Configuration and declared blocking prerequisites are ready for Stage 22 validation.</summary>
    ReadyForValidation = 2,
    /// <summary>A safe prerequisite or configuration issue requires attention.</summary>
    NeedsAttention = 3,
    /// <summary>The agent-specific onboarding workflow completed.</summary>
    Completed = 4,
    /// <summary>The owner explicitly skipped the agent in this session.</summary>
    Skipped = 5,
}

/// <summary>The code-owned onboarding presentation path selected for an installed agent.</summary>
public enum AgentOnboardingSupport
{
    /// <summary>A code-owned specialized adapter is explicitly registered.</summary>
    Specialized = 0,
    /// <summary>The bounded Stage 17 schema subset can use the platform generic adapter.</summary>
    SafeGeneric = 1,
    /// <summary>The installed schema is too complex and no specialized adapter is installed.</summary>
    TypedAdapterRequired = 2,
}

/// <summary>One durable selection row. It stores immutable revision identities, never configuration JSON.</summary>
public sealed record OnboardingAgentSelection(
    Guid SessionId,
    AgentId AgentId,
    OnboardingAgentSelectionStatus SelectionStatus,
    OnboardingAgentProgressStatus ProgressStatus,
    string AdapterId,
    string CurrentStepKey,
    string? ReviewedManifestVersion,
    string? ReviewedConfigurationSchemaVersion,
    Guid? StartingConfigurationRevisionId,
    string? StartingConfigurationHash,
    Guid? SavedConfigurationRevisionId,
    string? SavedConfigurationHash,
    string LastReasonCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long Revision);

/// <summary>A bounded non-executing inspection of one registered package.</summary>
public sealed record InstalledAgentPackageInspection(
    AgentId AgentId,
    bool IsAvailable,
    bool IsRemoved,
    string ReasonCode,
    string Message,
    string InstalledVersion,
    string ManifestVersion,
    string? ConfigurationSchemaVersion,
    GenericConfigurationSchema? ConfigurationSchema);

/// <summary>Reads installed package metadata without loading or executing agent assemblies.</summary>
public interface IInstalledAgentPackageInspector
{
    /// <summary>Inspects one persisted definition against its current package files.</summary>
    ValueTask<InstalledAgentPackageInspection> InspectAsync(
        AgentDefinitionRecord definition,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable code-owned wizard step metadata.</summary>
public sealed record AgentOnboardingStepDescriptor(string Key, string Title, string Description);

/// <summary>One safe prerequisite result declared by an adapter.</summary>
public sealed record AgentOnboardingPrerequisite(
    string Key,
    string Title,
    OnboardingCheckStatus Status,
    string ReasonCode,
    string Message,
    bool Blocking);

/// <summary>One declared opaque secret reference and existence status.</summary>
public sealed record AgentOnboardingSecretStatus(
    string FieldKey,
    string Title,
    SecretReference Reference,
    bool Required,
    bool? IsConfigured,
    string ReasonCode);

/// <summary>A later diagnostic command plan. Stage 19 describes it but never executes it.</summary>
public sealed record AgentOnboardingDiagnosticPlan(string CommandName, string ArgumentsJson, bool RequiresInteractiveSession);

/// <summary>Code-owned schedule capability constraints for later onboarding stages.</summary>
public sealed record AgentOnboardingScheduleConstraints(
    bool SupportsScheduling,
    bool SupportsManualRun,
    bool RequiresInteractiveSession,
    IReadOnlyList<string> SupportedCommands);

/// <summary>Static adapter identity and safe presentation metadata.</summary>
public sealed record AgentOnboardingAdapterDescriptor(
    string AdapterId,
    string AdapterVersion,
    AgentId? SupportedAgentId,
    AgentOnboardingSupport Support,
    bool CanConfigure,
    IReadOnlyList<AgentOnboardingStepDescriptor> Steps);

/// <summary>Current adapter assessment derived from authoritative configuration and protected-secret status.</summary>
public sealed record AgentOnboardingAssessment(
    bool IsConfigured,
    bool RequiresReview,
    bool ReadyForValidation,
    string ReasonCode,
    string Message,
    IReadOnlyList<AgentOnboardingPrerequisite> Prerequisites,
    IReadOnlyList<AgentOnboardingSecretStatus> Secrets,
    AgentOnboardingDiagnosticPlan? DiagnosticPlan,
    AgentOnboardingScheduleConstraints ScheduleConstraints,
    string CompletionSummary);

/// <summary>UI-neutral input supplied to an onboarding adapter.</summary>
public sealed record AgentOnboardingContext(
    AgentDefinitionRecord Definition,
    InstalledAgentPackageInspection Package,
    AgentConfigurationRecord? Configuration,
    OnboardingAgentSelection? Selection);

/// <summary>Application-owned code adapter. Host registers implementations explicitly.</summary>
public interface IAgentOnboardingAdapter
{
    /// <summary>Gets stable adapter metadata and code-owned steps.</summary>
    AgentOnboardingAdapterDescriptor Descriptor { get; }

    /// <summary>Assesses the current authoritative configuration and safe prerequisites.</summary>
    ValueTask<AgentOnboardingAssessment> AssessAsync(
        AgentOnboardingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the current immutable configuration through the existing configuration service.</summary>
    ValueTask<AgentConfigurationRecord?> LoadConfigurationAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves configuration through the existing authoritative configuration service.</summary>
    ValueTask<SaveConfigurationResult> SaveConfigurationAsync(
        SaveConfigurationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves only explicitly composed adapters plus the bounded generic fallback.</summary>
public interface IAgentOnboardingAdapterResolver
{
    /// <summary>Returns a safe adapter or null when the schema requires unavailable typed support.</summary>
    IAgentOnboardingAdapter? Resolve(AgentId agentId, GenericConfigurationSchema? schema);
}

/// <summary>One choice submitted by the owner after service-side package validation.</summary>
public sealed record SaveOnboardingAgentChoice(
    AgentId AgentId,
    OnboardingAgentSelectionStatus SelectionStatus,
    OnboardingAgentProgressStatus ProgressStatus,
    string AdapterId,
    string CurrentStepKey,
    string ManifestVersion,
    string? ConfigurationSchemaVersion,
    Guid? StartingConfigurationRevisionId,
    string? StartingConfigurationHash,
    string ReasonCode);

/// <summary>The result of a transactional idempotent selection save.</summary>
public sealed record SaveOnboardingAgentChoicesResult(
    OnboardingSession Session,
    IReadOnlyList<OnboardingAgentSelection> Selections,
    IReadOnlyList<AgentId> ChangedAgentIds);

/// <summary>An optimistic per-agent progress update.</summary>
public sealed record UpdateOnboardingAgentProgressRequest(
    Guid SessionId,
    AgentId AgentId,
    long ExpectedRevision,
    OnboardingAgentSelectionStatus SelectionStatus,
    OnboardingAgentProgressStatus ProgressStatus,
    string AdapterId,
    string CurrentStepKey,
    string? ReviewedManifestVersion,
    string? ReviewedConfigurationSchemaVersion,
    Guid? SavedConfigurationRevisionId,
    string? SavedConfigurationHash,
    string ReasonCode,
    DateTimeOffset? CompletedAtUtc);

/// <summary>Focused durable boundary for per-agent onboarding state.</summary>
public interface IOnboardingAgentSelectionRepository
{
    /// <summary>Creates or joins the one active AddAgents or ReconfigureAgent session.</summary>
    ValueTask<OnboardingSession> StartAgentSelectionSessionAsync(
        OnboardingSessionKind kind,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a session's durable selections in stable agent order.</summary>
    ValueTask<IReadOnlyList<OnboardingAgentSelection>> GetSelectionsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the most recent durable review per agent across all sessions.</summary>
    ValueTask<IReadOnlyDictionary<AgentId, OnboardingAgentSelection>> GetLatestSelectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Saves an explicit select/defer batch transactionally with session-revision protection.</summary>
    ValueTask<SaveOnboardingAgentChoicesResult> SaveChoicesAsync(
        Guid sessionId,
        long expectedSessionRevision,
        IReadOnlyList<SaveOnboardingAgentChoice> choices,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates one agent's independent optimistic progress row.</summary>
    ValueTask<OnboardingAgentSelection> UpdateProgressAsync(
        UpdateOnboardingAgentProgressRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Marks selected packages that became unavailable without deleting their history.</summary>
    ValueTask<IReadOnlyList<AgentId>> ReconcileUnavailableAsync(
        Guid sessionId,
        IReadOnlyDictionary<AgentId, bool> unavailableAgents,
        CancellationToken cancellationToken = default);
}

/// <summary>Bounded owner-facing row for agent selection and later settings entry.</summary>
public sealed record AgentOnboardingCard(
    AgentDefinitionRecord Definition,
    InstalledAgentPackageInspection Package,
    AgentOnboardingSupport Support,
    bool IsPending,
    bool IsConfigured,
    bool ReadyForValidation,
    string PendingReasonCode,
    string PendingMessage,
    OnboardingAgentSelection? Selection,
    AgentOnboardingAssessment? Assessment);

/// <summary>Complete selection page projection.</summary>
public sealed record AgentSelectionOverview(
    OnboardingSession Session,
    IReadOnlyList<AgentOnboardingCard> Agents,
    int AvailableCount,
    int PendingCount,
    int SelectedCount,
    int DeferredCount);

/// <summary>Non-blocking dashboard status for newly available agents.</summary>
public sealed record AgentOnboardingDashboardStatus(int PendingAgentCount, IReadOnlyList<AgentId> PendingAgentIds)
{
    /// <summary>Gets whether the dashboard should show the setup banner.</summary>
    public bool HasPendingAgents => PendingAgentCount > 0;
}

/// <summary>Generic editor state loaded without putting secret values in the model.</summary>
public sealed record GenericAgentOnboardingEditor(
    AgentSelectionOverview Overview,
    AgentOnboardingCard Agent,
    OnboardingAgentSelection Selection,
    GenericConfigurationSchema Schema,
    AgentConfigurationRecord? Configuration,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<AgentOnboardingSecretStatus> Secrets);

/// <summary>Stage 19 selection, pending-agent, generic configuration, and re-entry use cases.</summary>
public interface IAgentOnboardingService
{
    /// <summary>Gets non-blocking pending-agent status without starting a session.</summary>
    ValueTask<AgentOnboardingDashboardStatus> GetDashboardStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts or joins an AddAgents session.</summary>
    ValueTask<OnboardingSession> StartAddAgentsAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>Starts or joins a ReconfigureAgent session and selects the requested agent.</summary>
    ValueTask<OnboardingSession> StartReconfigureAgentAsync(
        AgentId agentId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the current explicit selection workflow.</summary>
    ValueTask<AgentSelectionOverview> GetSelectionOverviewAsync(
        Guid sessionId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves an explicit choice for every currently available agent.</summary>
    ValueTask<AgentSelectionOverview> SaveChoicesAsync(
        Guid sessionId,
        long expectedSessionRevision,
        IReadOnlySet<AgentId> selectedAgentIds,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the safe generic configuration step.</summary>
    ValueTask<GenericAgentOnboardingEditor> GetGenericEditorAsync(
        Guid sessionId,
        AgentId agentId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves/reviews a generic configuration and records only its immutable identity.</summary>
    ValueTask<OnboardingAgentSelection> SaveGenericConfigurationAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        JsonElement configuration,
        long? expectedConfigurationRevision,
        string? expectedConfigurationHash,
        string changeSummary,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a separately protected generic secret without returning its value.</summary>
    ValueTask<OnboardingAgentSelection> SetGenericSecretAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string fieldKey,
        string secretValue,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a separately protected generic secret and re-evaluates prerequisites.</summary>
    ValueTask<OnboardingAgentSelection> DeleteGenericSecretAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string fieldKey,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Defers one selected agent without changing its working configuration or schedules.</summary>
    ValueTask<OnboardingAgentSelection> ConfigureLaterAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string actorId,
        CancellationToken cancellationToken = default);
}
