using System.Text.Json;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Artifacts;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Execution;
using HomeBusinessAssistant.Infrastructure.Operations;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using HomeBusinessAssistant.Windows.Desktop;
using HomeBusinessAssistant.Windows.Power;
using HomeBusinessAssistant.Windows.Processes;
using HomeBusinessAssistant.Windows.Secrets;
using HomeBusinessAssistant.Windows.Wake;
using Microsoft.EntityFrameworkCore;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Runner;

internal sealed class RunnerRuntime : IAsyncDisposable
{
    private readonly IAgentDefinitionRepository agents;
    private readonly IManualRunService manualRuns;
    private readonly IOccurrenceExecutor executor;
    private readonly IStaleRunRecoveryService recovery;
    private readonly AgentRegistryScanner registryScanner;
    private readonly IWakeTaskReconciler wakeReconciler;
    private readonly IWakeTaskSchedulerBridge wakeBridge;
    private readonly IPowerDiagnosticsService powerDiagnostics;
    private readonly IWakeTestService wakeTests;
    private readonly IDatabaseBackupService? backups;
    private readonly IHostStartupTaskScheduler hostStartup;
    private readonly ProductBuildInfo build;
    private readonly IHostPlatformStatusReader platformStatus;
    private readonly string centralMigration;
    private readonly string founderMigration;
    private readonly WindowsPowerRequestService powerRequests;
    private readonly RunnerBootstrapSettings bootstrap;

    private RunnerRuntime(
        IAgentDefinitionRepository agents,
        IManualRunService manualRuns,
        IOccurrenceExecutor executor,
        IStaleRunRecoveryService recovery,
        AgentRegistryScanner registryScanner,
        IWakeTaskReconciler wakeReconciler,
        IWakeTaskSchedulerBridge wakeBridge,
        IPowerDiagnosticsService powerDiagnostics,
        IWakeTestService wakeTests,
        IDatabaseBackupService? backups,
        IHostStartupTaskScheduler hostStartup,
        ProductBuildInfo build,
        IHostPlatformStatusReader platformStatus,
        string centralMigration,
        string founderMigration,
        WindowsPowerRequestService powerRequests,
        RunnerBootstrapSettings bootstrap)
    {
        this.agents = agents;
        this.manualRuns = manualRuns;
        this.executor = executor;
        this.recovery = recovery;
        this.registryScanner = registryScanner;
        this.wakeReconciler = wakeReconciler;
        this.wakeBridge = wakeBridge;
        this.powerDiagnostics = powerDiagnostics;
        this.wakeTests = wakeTests;
        this.backups = backups;
        this.hostStartup = hostStartup;
        this.build = build;
        this.platformStatus = platformStatus;
        this.centralMigration = centralMigration;
        this.founderMigration = founderMigration;
        this.powerRequests = powerRequests;
        this.bootstrap = bootstrap;
    }

    public static async ValueTask<RunnerRuntime> CreateAsync(
        RunnerBootstrapSettings bootstrap,
        CancellationToken cancellationToken,
        IExecutionLifetimeHook? executionLifetimeHook = null,
        bool includeOperations = false)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        string agentDirectory = Path.GetFullPath(bootstrap.AgentDirectory);
        string manifestDirectory = Path.GetFullPath(bootstrap.ManifestDirectory);
        Directory.CreateDirectory(agentDirectory);
        if (!Directory.Exists(manifestDirectory))
        {
            throw new DirectoryNotFoundException("The manifest directory does not exist.");
        }

        TimeProvider timeProvider = TimeProvider.System;
        AssistantDatabase database = await AssistantDatabaseInitializer.InitializeAsync(
            new(
                bootstrap.DataDirectory,
                bootstrap.DatabaseFileName,
                bootstrap.BusyTimeout,
                manifestDirectory),
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        RunnerSupervisionOptions options = RunnerSupervisionOptions.Default;
        var agents = new AgentDefinitionRepository(database.ContextFactory, timeProvider);
        var schemaCatalog = new FileAgentConfigurationSchemaCatalog(agentDirectory);
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new CompositeAgentConfigurationValidator([
                new BasicAgentConfigurationValidator(),
                new FounderScoutConfigurationValidator(),
                new WakeRemoteConfigurationValidator(),
                new GenericAgentConfigurationValidator(
                    schemaCatalog,
                    new HashSet<AgentId> { FounderScoutDefaults.AgentId, WakeRemoteDefaults.AgentId }),
            ]),
            timeProvider);
        _ = await new AgentDefaultConfigurationSeeder(configurations).SeedIfMissingAsync(
            new WakeRemoteDefaults(),
            cancellationToken).ConfigureAwait(false);
        _ = await new AgentDefaultConfigurationSeeder(configurations).SeedIfMissingAsync(
            new FounderScoutDefaults(),
            cancellationToken).ConfigureAwait(false);
        var schedules = new ScheduleRepository(database.ContextFactory, timeProvider);
        var occurrences = new OccurrenceRepository(database.ContextFactory, timeProvider);
        var runs = new AgentRunRepository(database.ContextFactory);
        var leases = new LeaseManager(database.ContextFactory);
        var systemSettings = new SystemSettingRepository(database.ContextFactory);
        var audit = new AuditWriter(database.ContextFactory, timeProvider);
        var registryScanner = new AgentRegistryScanner(
            agentDirectory,
            agents,
            schemaCatalog,
            configurations,
            audit,
            timeProvider);
        var secretStore = new WindowsCurrentUserSecretStore(database.DataDirectory, audit);
        var fixedDelay = new FixedDelayCompletionService(occurrences, schedules, configurations, audit);
        var integrity = new ExecutableIntegrityService(agentDirectory, manifestDirectory);
        var temporaryFiles = new RunTemporaryFileManager(database.DataDirectory);
        var artifacts = new FileSystemArtifactStore(
            database.ContextFactory,
            database.DataDirectory,
            timeProvider,
            options.MaximumAgentArtifactBytes);
        var launcher = new AgentProcessLauncher(timeProvider);
        var protocol = new AgentProtocolReader(runs, artifacts, timeProvider, options);
        var monitor = new RunCancellationMonitor(runs, occurrences, leases, timeProvider, options);
        var finalizer = new RunFinalizer(runs, occurrences, schedules, fixedDelay, audit, timeProvider);
        var processExecutor = new BoundedProcessExecutor();
        var wakeBridge = new SchtasksWakeTaskSchedulerBridge(
            processExecutor,
            SchtasksWakeTaskSchedulerBridgeOptions.CreateDefault(database.DataDirectory));
        var wakeOptions = CreateWakeOptions(bootstrap, database.DataDirectory);
        var wakeReconciler = new WakeTaskReconciler(
            leases,
            occurrences,
            schedules,
            systemSettings,
            audit,
            wakeBridge,
            new ScheduleTimeZoneService(),
            timeProvider,
            wakeOptions);
        var powerRequests = new WindowsPowerRequestService(
            new WindowsExecutionStateNativeApi(),
            timeProvider);
        var executor = new OccurrenceExecutor(
            agents,
            configurations,
            schedules,
            occurrences,
            runs,
            leases,
            audit,
            fixedDelay,
            integrity,
            temporaryFiles,
            launcher,
            protocol,
            monitor,
            finalizer,
            executionLifetimeHook ?? new WindowsExecutionLifetimeHook(powerRequests),
            new RunLogFactory(database.DataDirectory),
            timeProvider,
            options,
            secretStore);
        var recovery = new StaleRunRecoveryService(
            runs,
            occurrences,
            agents,
            integrity,
            finalizer,
            fixedDelay,
            audit,
            temporaryFiles,
            timeProvider,
            options);
        var manualRuns = new ManualRunService(agents, configurations, schedules, occurrences, audit, timeProvider);
        var wakeTests = new WakeTestService(
            agents,
            configurations,
            occurrences,
            runs,
            wakeReconciler,
            wakeBridge,
            audit,
            timeProvider);
        IPowerDiagnosticsService powerDiagnostics = PowercfgDiagnosticsService.CreateDefault(
            processExecutor,
            timeProvider);
        ProductBuildInfo build = ProductBuildInfo.Load(typeof(RunnerRuntime).Assembly);
        var platformStatus = new HostPlatformStatusReader(database.ContextFactory);
        var hostStartup = new SchtasksHostStartupTaskScheduler(processExecutor, database.DataDirectory);
        string centralMigration;
        await using (AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            centralMigration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "none";
        }

        string founderMigration = "not-loaded";
        IDatabaseBackupService? backups = null;
        if (includeOperations)
        {
            string founderRoot = Path.Combine(database.DataDirectory, "agents", FounderScoutDefaults.AgentId.Value);
            FounderScoutDatabase founderDatabase = await FounderScoutDatabaseInitializer.InitializeAsync(
                new FounderScoutDatabaseSettings(founderRoot),
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            await using (FounderScoutDbContext context = await founderDatabase.ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                founderMigration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "none";
            }

            backups = new DatabaseBackupService(
                database.ContextFactory,
                leases,
                audit,
                timeProvider,
                DatabaseBackupOptions.CreateDefault(database.DataDirectory, bootstrap.DatabaseFileName, build),
                async token =>
                {
                    _ = await AssistantDatabaseInitializer.InitializeAsync(new(
                        bootstrap.DataDirectory,
                        bootstrap.DatabaseFileName,
                        bootstrap.BusyTimeout,
                        manifestDirectory), timeProvider, token).ConfigureAwait(false);
                    _ = await FounderScoutDatabaseInitializer.InitializeAsync(
                        new FounderScoutDatabaseSettings(founderRoot), timeProvider, token).ConfigureAwait(false);
                });
        }
        return new(
            agents,
            manualRuns,
            executor,
            recovery,
            registryScanner,
            wakeReconciler,
            wakeBridge,
            powerDiagnostics,
            wakeTests,
            backups,
            hostStartup,
            build,
            platformStatus,
            centralMigration,
            founderMigration,
            powerRequests,
            bootstrap);
    }

    public async Task<int> ExecuteAsync(
        RunnerInvocation invocation,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (invocation.Operation is RunnerOperation.Execute or RunnerOperation.RunAgent)
        {
            StaleRunRecoveryResult startupRecovery = await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
            if (startupRecovery.Errors > 0)
            {
                await output.WriteLineAsync($"Recovery completed with {startupRecovery.Errors} error(s).").ConfigureAwait(false);
            }
        }

        return invocation.Operation switch
        {
            RunnerOperation.Execute => await ExecuteOccurrenceAsync(
                invocation.OccurrenceId!.Value,
                invocation.ReconcileWakeAfterExecution,
                output,
                cancellationToken).ConfigureAwait(false),
            RunnerOperation.RunAgent => await RunAgentAsync(invocation, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.Recover => await RecoverAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.Diagnose => await DiagnoseAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.ScanAgents => await ScanAgentsAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.ReconcileWake => await ReconcileWakeAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.PowerDiagnostics => await CapturePowerDiagnosticsAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.PrepareWakeTest => await PrepareWakeTestAsync(invocation.WakeTestMinutes, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.WakeTestStatus => await GetWakeTestStatusAsync(invocation.OccurrenceId!.Value, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.RemoveWakeTask => await RemoveWakeTaskAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.Migrate => await MigrateAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.CreateBackup => await CreateBackupAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.ListBackups => await ListBackupsAsync(invocation.MaximumResults, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.ValidateBackup => await ValidateBackupAsync(invocation.BackupSetId!, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.RestoreBackup => await RestoreBackupAsync(invocation, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.RegisterHostStartup => await RegisterHostStartupAsync(invocation, output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.HostStartupStatus => await HostStartupStatusAsync(output, cancellationToken).ConfigureAwait(false),
            RunnerOperation.RemoveHostStartup => await RemoveHostStartupAsync(output, cancellationToken).ConfigureAwait(false),
            _ => RunnerExitCode.InvalidArguments,
        };
    }

    private async Task<int> RunAgentAsync(
        RunnerInvocation invocation,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        AgentDefinitionRecord? agent = await agents.GetAsync(
            invocation.AgentId!.Value, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            await output.WriteLineAsync("The requested agent is not installed.").ConfigureAwait(false);
            return RunnerExitCode.NotRunnable;
        }

        OccurrenceId occurrenceId = await manualRuns.CreateAsync(new(
            agent.Id,
            invocation.CommandName!,
            invocation.ArgumentsJson,
            TriggerType.CommandLine,
            agent.DefaultConcurrencyPolicy,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            ActorId: "runner-cli",
            CorrelationId: Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        return await ExecuteOccurrenceAsync(occurrenceId, reconcileWakeAfter: false, output, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteOccurrenceAsync(
        OccurrenceId occurrenceId,
        bool reconcileWakeAfter,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        OccurrenceExecutionResult result = await executor.ExecuteAsync(
            occurrenceId, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            occurrenceId = result.OccurrenceId.ToString(),
            runId = result.RunId?.ToString(),
            status = result.RunStatus?.ToString(),
            result.AgentExitCode,
            result.OutcomeCode,
            result.Message,
        })).ConfigureAwait(false);
        if (reconcileWakeAfter)
        {
            WakeTaskReconciliationResult wake = await wakeReconciler.ReconcileAsync(
                $"runner-{Environment.ProcessId}",
                CancellationToken.None).ConfigureAwait(false);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                wakeReconciliation = wake.Error is null ? "completed" : "failed",
                wake.Changed,
                errorCode = wake.Error?.Code,
            })).ConfigureAwait(false);
        }

        return result.RunnerExitCode;
    }

    private async Task<int> RecoverAsync(TextWriter output, CancellationToken cancellationToken)
    {
        StaleRunRecoveryResult result = await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return result.Errors == 0 ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> DiagnoseAsync(TextWriter output, CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentDefinitionRecord> installed = await agents.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        HostPlatformStatus status = await platformStatus.ReadAsync(100, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            status = "healthy",
            dataDirectory = Path.GetFullPath(bootstrap.DataDirectory),
            agentDirectory = Path.GetFullPath(bootstrap.AgentDirectory),
            manifestDirectory = Path.GetFullPath(bootstrap.ManifestDirectory),
            enabledAgents = installed.Select(item => item.Id.Value).ToArray(),
            activeRuns = status.ActiveRuns.Count,
            build,
        })).ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private async Task<int> ScanAgentsAsync(TextWriter output, CancellationToken cancellationToken)
    {
        AgentRegistryScanResult result = await registryScanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return result.InvalidCount == 0 ? RunnerExitCode.Success : RunnerExitCode.InvalidExecutionEnvironment;
    }

    private async Task<int> ReconcileWakeAsync(TextWriter output, CancellationToken cancellationToken)
    {
        WakeTaskReconciliationResult result = await wakeReconciler.ReconcileAsync(
            $"runner-cli-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return result.Error is null ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> CapturePowerDiagnosticsAsync(TextWriter output, CancellationToken cancellationToken)
    {
        PowerDiagnosticsSnapshot snapshot = await powerDiagnostics.CaptureAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);
        return snapshot.Results.All(item => item.ExitCode == 0 && !item.TimedOut)
            ? RunnerExitCode.Success
            : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> PrepareWakeTestAsync(
        int minutes,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        WakeTestPreparationResult result = await wakeTests.PrepareAsync(new(
            minutes,
            "runner-cli",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return result.Reconciliation.Error is null ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> GetWakeTestStatusAsync(
        OccurrenceId occurrenceId,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        WakeTestResult result = await wakeTests.GetResultAsync(occurrenceId, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private async Task<int> RemoveWakeTaskAsync(TextWriter output, CancellationToken cancellationToken)
    {
        await wakeBridge.RemoveAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync("{\"managedWakeTask\":\"absent\"}").ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private async Task<int> MigrateAsync(TextWriter output, CancellationToken cancellationToken)
    {
        EnsureBackupsAvailable();
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            status = "migrated",
            productVersion = build.ProductVersion,
            centralDatabase = Path.Combine(Path.GetFullPath(bootstrap.DataDirectory), bootstrap.DatabaseFileName),
            centralMigration,
            founderDatabase = Path.Combine(Path.GetFullPath(bootstrap.DataDirectory), "agents", "founder-scout", "founders.db"),
            founderMigration,
        })).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return RunnerExitCode.Success;
    }

    private async Task<int> CreateBackupAsync(TextWriter output, CancellationToken cancellationToken)
    {
        BackupCreateResult result = await EnsureBackupsAvailable().CreateAsync("runner-cli", cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private async Task<int> ListBackupsAsync(int maximumResults, TextWriter output, CancellationToken cancellationToken)
    {
        IReadOnlyList<BackupSetInfo> result = await EnsureBackupsAvailable().ListAsync(maximumResults, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private async Task<int> ValidateBackupAsync(string backupSetId, TextWriter output, CancellationToken cancellationToken)
    {
        BackupValidationResult result = await EnsureBackupsAvailable().ValidateAsync(backupSetId, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(result)).ConfigureAwait(false);
        return result.IsValid ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> RestoreBackupAsync(RunnerInvocation invocation, TextWriter output, CancellationToken cancellationToken)
    {
        BackupRestoreResult result = await EnsureBackupsAvailable().RestoreAsync(new(
            invocation.BackupSetId!,
            invocation.ConfirmationToken!,
            invocation.MaintenanceMode,
            "runner-cli"), cancellationToken).ConfigureAwait(false);
        StaleRunRecoveryResult recovered = await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
        WakeTaskReconciliationResult wake = await wakeReconciler.ReconcileAsync(
            $"restore-{Environment.ProcessId}", cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { result, recovered, wake })).ConfigureAwait(false);
        return wake.Error is null && recovered.Errors == 0 ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> RegisterHostStartupAsync(RunnerInvocation invocation, TextWriter output, CancellationToken cancellationToken)
    {
        string host = invocation.HostExecutablePath!;
        string config = invocation.BootstrapConfigurationPath!;
        if (!File.Exists(host) || !File.Exists(config))
        {
            await output.WriteLineAsync("{\"hostStartup\":\"missing-files\"}").ConfigureAwait(false);
            return RunnerExitCode.NotRunnable;
        }

        string userId = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";
        HostStartupTaskState state = await hostStartup.ReconcileAsync(new(
            host,
            Path.GetDirectoryName(host)!,
            config,
            userId,
            TimeSpan.FromSeconds(invocation.StartupDelaySeconds)), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(state)).ConfigureAwait(false);
        return state.IsManaged ? RunnerExitCode.Success : RunnerExitCode.RunnerFailure;
    }

    private async Task<int> HostStartupStatusAsync(TextWriter output, CancellationToken cancellationToken)
    {
        HostStartupTaskState state = await hostStartup.GetStateAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(JsonSerializer.Serialize(state)).ConfigureAwait(false);
        return state.Exists && !state.IsManaged ? RunnerExitCode.RunnerFailure : RunnerExitCode.Success;
    }

    private async Task<int> RemoveHostStartupAsync(TextWriter output, CancellationToken cancellationToken)
    {
        await hostStartup.RemoveAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync("{\"hostStartup\":\"absent\"}").ConfigureAwait(false);
        return RunnerExitCode.Success;
    }

    private IDatabaseBackupService EnsureBackupsAvailable() => backups
        ?? throw new InvalidOperationException("Database operations were not initialized for this command.");

    public async ValueTask DisposeAsync() => await powerRequests.DisposeAsync().ConfigureAwait(false);

    private static WakeTaskReconciliationOptions CreateWakeOptions(
        RunnerBootstrapSettings bootstrap,
        string dataDirectory)
    {
        string baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string runnerExecutable = Path.Combine(baseDirectory, "HomeBusinessAssistant.Runner.exe");
        string userId = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";
        return new(
            baseDirectory,
            runnerExecutable,
            baseDirectory,
            new(
                Path.GetFullPath(dataDirectory),
                Path.GetFullPath(bootstrap.AgentDirectory),
                Path.GetFullPath(bootstrap.ManifestDirectory),
                bootstrap.DatabaseFileName),
            userId,
            TimeZoneInfo.Local.Id,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromDays(7),
            TimeSpan.FromMinutes(5),
            StartWhenAvailable: true,
            AllowStartOnBatteries: true,
            StopIfGoingOnBatteries: false);
    }

}
