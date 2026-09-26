using FounderScout.Application;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Host.Dashboard;
using HomeBusinessAssistant.Host.Desktop;
using HomeBusinessAssistant.Host.Health;
namespace HomeBusinessAssistant.Host;

/// <summary>Prebuilt desktop runtime services consumed by the local WebApplication.</summary>
public sealed record HostWebComposition(
    HostHealthState HealthState,
    IHostReadinessEvaluator Readiness,
    IHostDashboardService Dashboard,
    IGlobalScheduleControlService GlobalScheduleControl,
    IReadOnlyList<IHostedService> HostedServices,
    ILoggerProvider? LoggerProvider,
    HostManagementComposition? Management = null);

/// <summary>Stage 08 services kept together so the desktop lifetime remains the composition owner.</summary>
public sealed record HostManagementComposition(
    IManagementQueryService? Queries = null,
    IManagementCommandService? Commands = null,
    IAgentConfigurationService? Configurations = null,
    ISecretStore? Secrets = null,
    IArtifactStore? Artifacts = null,
    IWakeTestService? WakeTests = null,
    IPowerDiagnosticsService? PowerDiagnostics = null,
    IWakeTaskSchedulerBridge? WakeBridge = null,
    IManualKeepAwakeService? KeepAwake = null,
    HostBootstrapSettings? Bootstrap = null,
    TimeProvider? TimeProvider = null,
    IAuditWriter? Audit = null,
    FounderScoutManagementComposition? FounderScout = null,
    IOperationalService? Operations = null,
    IDatabaseBackupService? Backups = null,
    ProductBuildInfo? Build = null,
    string? FounderDatabaseMigration = null,
    IAgentConfigurationSchemaCatalog? ConfigurationSchemas = null,
    IOnboardingService? Onboarding = null,
    IAgentOnboardingService? AgentOnboarding = null,
    IFounderScoutOnboardingService? FounderScoutOnboarding = null);

/// <summary>Founder Scout results services owned by the interactive Host process.</summary>
public sealed record FounderScoutManagementComposition(
    IFounderScoutResultsQuery Queries,
    IFounderScoutResultsCommands Commands,
    string DataDirectory);

internal sealed class NoOpGlobalScheduleControlService : IGlobalScheduleControlService
{
    public ValueTask<GlobalSchedulePauseState> GetStateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new GlobalSchedulePauseState(false, []));

    public ValueTask<GlobalSchedulePauseState> PauseAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default) => GetStateAsync(cancellationToken);

    public ValueTask<GlobalSchedulePauseState> ResumeAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default) => GetStateAsync(cancellationToken);
}
