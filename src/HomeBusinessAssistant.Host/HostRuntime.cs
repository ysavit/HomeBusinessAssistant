using FounderScout.Application;
using FounderScout.Infrastructure.Ai;
using FounderScout.Infrastructure.Browser;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Host.Dashboard;
using HomeBusinessAssistant.Host.Desktop;
using HomeBusinessAssistant.Host.Health;
using HomeBusinessAssistant.Host.Logging;
using HomeBusinessAssistant.Host.Onboarding;
using HomeBusinessAssistant.Host.Orchestration;
using HomeBusinessAssistant.Infrastructure.Artifacts;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Execution;
using HomeBusinessAssistant.Infrastructure.Management;
using HomeBusinessAssistant.Infrastructure.Onboarding;
using HomeBusinessAssistant.Infrastructure.Operations;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using HomeBusinessAssistant.Windows.Desktop;
using HomeBusinessAssistant.Windows.Onboarding;
using HomeBusinessAssistant.Windows.Power;
using HomeBusinessAssistant.Windows.Processes;
using HomeBusinessAssistant.Windows.Secrets;
using HomeBusinessAssistant.Windows.Wake;
using Microsoft.EntityFrameworkCore;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Host;

/// <summary>Owns the composed desktop/web control-plane services and native power lifetime.</summary>
public sealed class HostRuntime : IAsyncDisposable
{
    private readonly IStaleRunRecoveryService recovery;
    private readonly WindowsPowerRequestService powerRequests;
    private readonly ILoggerFactory loggerFactory;
    private readonly Serilog.ILogger logger;
    private readonly FounderProviderReadinessProbeHost founderProviderProbeHost;
    private int disposed;

    private HostRuntime(
        HostBootstrapSettings settings,
        HostHealthState healthState,
        HostWebComposition webComposition,
        TrayController trayController,
        HostNotificationHub notifications,
        IManualKeepAwakeService keepAwake,
        IStaleRunRecoveryService recovery,
        WindowsPowerRequestService powerRequests,
        FounderProviderReadinessProbeHost founderProviderProbeHost,
        ILoggerFactory loggerFactory,
        Serilog.ILogger logger)
    {
        Settings = settings;
        HealthState = healthState;
        WebComposition = webComposition;
        TrayController = trayController;
        Notifications = notifications;
        KeepAwake = keepAwake;
        this.recovery = recovery;
        this.powerRequests = powerRequests;
        this.founderProviderProbeHost = founderProviderProbeHost;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <summary>Gets validated Host bootstrap settings.</summary>
    public HostBootstrapSettings Settings { get; }

    /// <summary>Gets shared startup and loop health.</summary>
    public HostHealthState HealthState { get; }

    /// <summary>Gets services supplied to the loopback WebApplication.</summary>
    public HostWebComposition WebComposition { get; }

    /// <summary>Gets the testable tray command controller.</summary>
    public TrayController TrayController { get; }

    /// <summary>Gets the in-process tray notification hub.</summary>
    public HostNotificationHub Notifications { get; }

    /// <summary>Gets the manual power session owned by this Host.</summary>
    public IManualKeepAwakeService KeepAwake { get; }

    /// <summary>Gets the safe structured Host logger.</summary>
    public Serilog.ILogger Logger => logger;

    /// <summary>Migrates/bootstrap persistence and composes the Stage 07 Host runtime.</summary>
    public static async ValueTask<HostRuntime> CreateAsync(
        HostBootstrapSettings settings,
        Serilog.ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        settings.Validate();
        Directory.CreateDirectory(settings.AgentDirectory);
        TimeProvider timeProvider = TimeProvider.System;
        AssistantDatabase database = await AssistantDatabaseInitializer.InitializeAsync(new(
            settings.DataDirectory,
            settings.DatabaseFileName,
            BusyTimeout: TimeSpan.FromSeconds(5),
            settings.ManifestDirectory), timeProvider, cancellationToken).ConfigureAwait(false);
        bool directoriesWritable = HostReadinessEvaluator.ProbeDirectories(database.DataDirectory);
        var healthState = new HostHealthState(timeProvider);
        healthState.RecordBootstrap(
            databaseReady: true,
            directoriesWritable,
            runnerAvailable: File.Exists(settings.RunnerExecutablePath));

        var agents = new AgentDefinitionRepository(database.ContextFactory, timeProvider);
        var schemaCatalog = new FileAgentConfigurationSchemaCatalog(settings.AgentDirectory);
        var configurationValidator = new CompositeAgentConfigurationValidator([
                new BasicAgentConfigurationValidator(),
                new FounderScoutConfigurationValidator(),
                new WakeRemoteConfigurationValidator(),
                new GenericAgentConfigurationValidator(
                    schemaCatalog,
                    new HashSet<HomeBusinessAssistant.Domain.Agents.AgentId>
                    {
                        FounderScoutDefaults.AgentId,
                        WakeRemoteDefaults.AgentId,
                    }),
            ]);
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            configurationValidator,
            timeProvider);
        _ = await new AgentDefaultConfigurationSeeder(configurations).SeedIfMissingAsync(
            new FounderScoutDefaults(),
            cancellationToken).ConfigureAwait(false);
        _ = await new AgentDefaultConfigurationSeeder(configurations).SeedIfMissingAsync(
            new WakeRemoteDefaults(),
            cancellationToken).ConfigureAwait(false);
        var schedules = new ScheduleRepository(database.ContextFactory, timeProvider);
        var occurrences = new OccurrenceRepository(database.ContextFactory, timeProvider);
        var runs = new AgentRunRepository(database.ContextFactory);
        var leases = new LeaseManager(database.ContextFactory);
        var settingsRepository = new SystemSettingRepository(database.ContextFactory);
        var audit = new AuditWriter(database.ContextFactory, timeProvider);
        var timeZones = new ScheduleTimeZoneService();
        var scheduleValidator = new AgentScheduleValidator(agents, configurations, schedules, timeZones);
        var schedulePlanner = new ScheduleOccurrencePlanner(
            new ScheduleCalculator(timeZones),
            occurrences,
            configurations,
            SchedulingOptions.Default,
            new WakeRemoteOccurrenceArgumentsProvider());
        var occurrencePolicy = new OccurrencePolicyService(occurrences, SchedulingOptions.Default);
        var scheduleReconciler = new ScheduleReconciler(
            leases,
            schedules,
            agents,
            scheduleValidator,
            schedulePlanner,
            occurrencePolicy,
            occurrences,
            audit,
            timeProvider,
            SchedulingOptions.Default);
        var fixedDelay = new FixedDelayCompletionService(occurrences, schedules, configurations, audit);
        var scheduleControls = new ScheduleControlService(schedules, scheduleValidator, audit, timeProvider);
        var globalControls = new GlobalScheduleControlService(schedules, scheduleControls, settingsRepository, timeProvider);
        var manualRuns = new ManualRunService(agents, configurations, schedules, occurrences, audit, timeProvider);

        RunnerSupervisionOptions runnerOptions = RunnerSupervisionOptions.Default;
        var integrity = new ExecutableIntegrityService(settings.AgentDirectory, settings.ManifestDirectory);
        var temporaryFiles = new RunTemporaryFileManager(database.DataDirectory);
        var finalizer = new RunFinalizer(runs, occurrences, schedules, fixedDelay, audit, timeProvider);
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
            runnerOptions);

        var boundedProcesses = new BoundedProcessExecutor();
        var wakeBridge = new SchtasksWakeTaskSchedulerBridge(
            boundedProcesses,
            SchtasksWakeTaskSchedulerBridgeOptions.CreateDefault(database.DataDirectory));
        var wakeReconciler = new WakeTaskReconciler(
            leases,
            occurrences,
            schedules,
            settingsRepository,
            audit,
            wakeBridge,
            timeZones,
            timeProvider,
            CreateWakeOptions(settings, database.DataDirectory));
        var runnerLauncher = new WindowsRunnerProcessLauncher(new(
            settings.ApplicationRoot,
            settings.RunnerExecutablePath,
            settings.RunnerWorkingDirectory,
            database.DataDirectory,
            settings.AgentDirectory,
            settings.ManifestDirectory,
            settings.DatabaseFileName));
        var dispatcher = new OccurrenceRunnerDispatcher(occurrences, runnerLauncher, audit, timeProvider);
        var manualLauncher = new ManualAgentRunLauncher(manualRuns, dispatcher);
        var managementReconciliation = new ManagementReconciliationService(scheduleReconciler, wakeReconciler);
        var managementCommands = new ManagementCommandService(
            agents,
            schedules,
            scheduleValidator,
            scheduleControls,
            new ScheduleCalculator(timeZones),
            manualRuns,
            dispatcher,
            occurrences,
            runs,
            audit,
            managementReconciliation,
            timeProvider);
        var managementQueries = new ManagementQueryService(database.ContextFactory, database.DatabasePath, timeProvider);
        var secrets = new WindowsCurrentUserSecretStore(database.DataDirectory, audit);
        var artifacts = new FileSystemArtifactStore(database.ContextFactory, database.DataDirectory, timeProvider);
        string founderScoutDataDirectory = Path.Combine(database.DataDirectory, "agents", FounderScoutDefaults.AgentId.Value);
        FounderScoutDatabase founderScoutDatabase = await FounderScoutDatabaseInitializer.InitializeAsync(
            new FounderScoutDatabaseSettings(founderScoutDataDirectory),
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        string founderDatabaseMigration;
        await using (FounderScoutDbContext context = await founderScoutDatabase.ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            founderDatabaseMigration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "none";
        }

        var founderScoutResults = new FounderScoutResultsService(
            founderScoutDatabase.ContextFactory,
            founderScoutDatabase.DataDirectory,
            timeProvider);
        var founderScoutRepository = new FounderScoutRepository(founderScoutDatabase.ContextFactory, timeProvider);
        FounderScoutSimpleModeInitializationResult simpleMode = await new FounderScoutSimpleModeInitializer(
            configurations,
            agents,
            founderScoutRepository,
            founderScoutRepository,
            timeProvider).InitializeAsync(cancellationToken).ConfigureAwait(false);
        logger.Information(
            "Founder Scout simple mode ready: configurationChanged={ConfigurationChanged}, agentEnabled={AgentEnabled}, accountChanged={AccountChanged}, segmentChanged={SegmentChanged}.",
            simpleMode.ConfigurationChanged,
            simpleMode.AgentEnabled,
            simpleMode.BrowserAccountChanged,
            simpleMode.DiscoverySegmentChanged);
        FounderProviderReadinessProbeHost founderProviderProbeHost = FounderProviderReadinessProbeHost.Create();
        ProductBuildInfo build = ProductBuildInfo.Load(typeof(HostRuntime).Assembly);
        var backups = new DatabaseBackupService(
            database.ContextFactory,
            leases,
            audit,
            timeProvider,
            DatabaseBackupOptions.CreateDefault(database.DataDirectory, settings.DatabaseFileName, build));
        var wakeTests = new WakeTestService(
            agents,
            configurations,
            occurrences,
            runs,
            wakeReconciler,
            wakeBridge,
            audit,
            timeProvider);
        IPowerDiagnosticsService powerDiagnostics = PowercfgDiagnosticsService.CreateDefault(boundedProcesses, timeProvider);
        var startupTaskScheduler = new SchtasksHostStartupTaskScheduler(boundedProcesses, database.DataDirectory);
        var onboardingRepository = new OnboardingRepository(database.ContextFactory, timeProvider);
        var onboardingReadiness = new OnboardingReadinessRunner(
        [
            new HostIdentityOnboardingCheck(settings, timeProvider),
            new CentralDatabaseOnboardingCheck(database, timeProvider),
            new ProtectedStorageReadinessCheck(secrets, timeProvider),
            new RunnerAndPackagesOnboardingCheck(
                agents,
                settings.ApplicationRoot,
                settings.RunnerExecutablePath,
                settings.AgentDirectory,
                timeProvider),
            new TaskSchedulerOnboardingCheck(startupTaskScheduler, wakeBridge, timeProvider),
            new PowerAndWakeOnboardingCheck(powerDiagnostics, timeProvider),
            new StorageOnboardingCheck(
                settings.ApplicationRoot,
                database.DataDirectory,
                database.DatabasePath,
                settings.RunnerExecutablePath,
                timeProvider),
        ], timeProvider);
        var onboarding = new OnboardingService(onboardingRepository, onboardingReadiness, audit, timeProvider);
        var packageInspector = new InstalledAgentPackageInspector(settings.AgentDirectory, schemaCatalog);
        var genericOnboardingAdapter = new GenericAgentOnboardingAdapter(configurations, configurationValidator, secrets);
        var onboardingAdapters = new AgentOnboardingAdapterRegistry(
        [
            new FounderScoutOnboardingAdapter(configurations, secrets),
            new PendingSpecializedAgentOnboardingAdapter(
                WakeRemoteDefaults.AgentId,
                "wake-remote.specialized",
                "Stage 21",
                configurations),
        ], genericOnboardingAdapter);
        var agentOnboarding = new AgentOnboardingService(
            onboardingRepository,
            onboardingRepository,
            agents,
            configurations,
            packageInspector,
            onboardingAdapters,
            secrets,
            audit,
            timeProvider);
        var founderScoutOnboarding = new FounderScoutOnboardingService(
            configurations,
            secrets,
            founderScoutRepository,
            founderScoutRepository,
            onboardingRepository,
            onboardingRepository,
            occurrences,
            runs,
            dispatcher,
            new PlaywrightBrowserRuntimeFactory(),
            founderProviderProbeHost.Probe,
            audit,
            timeProvider);
        var operations = new OperationalService(
            database.ContextFactory,
            database.DataDirectory,
            database.DatabasePath,
            timeProvider,
            signalSource: new CompositeOperationalSignalSource([
                new FounderScoutOperationalSignalSource(founderScoutResults, timeProvider),
                new OnboardingOperationalSignalSource(onboardingRepository, timeProvider),
            ]),
            build: build);
        var statusReader = new HostPlatformStatusReader(database.ContextFactory);
        var powerRequests = new WindowsPowerRequestService(new WindowsExecutionStateNativeApi(), timeProvider);
        var keepAwake = new ManualKeepAwakeService(powerRequests, timeProvider);
        var notifications = new HostNotificationHub();
        var dashboardLauncher = new WindowsLocalDashboardLauncher(settings.Url);
        var trayController = new TrayController(
            statusReader,
            manualLauncher,
            globalControls,
            keepAwake,
            dashboardLauncher,
            notifications);
        var readiness = new HostReadinessEvaluator(database, settings, healthState);
        var dashboard = new HostDashboardService(statusReader, globalControls, wakeBridge, readiness, healthState);

        var loggingProvider = new HostSerilogLoggerProvider(logger);
        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(
            builder => builder.ClearProviders().AddProvider(loggingProvider));
        IReadOnlyList<IHostedService> hostedServices =
        [
            CreateLoop(
                HostLoopNames.Schedule,
                settings.ScheduleInterval,
                new ScheduleReconciliationIteration(
                    scheduleReconciler,
                    occurrences,
                    dispatcher,
                    globalControls,
                    wakeReconciler,
                    timeProvider),
                healthState,
                timeProvider,
                loggerFactory),
            CreateLoop(
                HostLoopNames.Wake,
                settings.WakeInterval,
                new WakeTaskReconciliationIteration(wakeReconciler),
                healthState,
                timeProvider,
                loggerFactory),
            CreateLoop(
                HostLoopNames.Recovery,
                settings.RecoveryInterval,
                new StaleRunRecoveryIteration(recovery),
                healthState,
                timeProvider,
                loggerFactory),
            CreateLoop(
                HostLoopNames.Notifications,
                settings.NotificationInterval,
                new NotificationPollingIteration(operations, notifications, timeProvider),
                healthState,
                timeProvider,
                loggerFactory),
        ];
        var webComposition = new HostWebComposition(
            healthState,
            readiness,
            dashboard,
            globalControls,
            hostedServices,
            loggingProvider,
            new(
                managementQueries,
                managementCommands,
                configurations,
                secrets,
                artifacts,
                wakeTests,
                powerDiagnostics,
                wakeBridge,
                keepAwake,
                settings,
                timeProvider,
                audit,
                new FounderScoutManagementComposition(
                    founderScoutResults,
                    founderScoutResults,
                    founderScoutDatabase.DataDirectory),
                operations,
                backups,
                build,
                founderDatabaseMigration,
                schemaCatalog,
                onboarding,
                agentOnboarding,
                founderScoutOnboarding));
        return new(
            settings,
            healthState,
            webComposition,
            trayController,
            notifications,
            keepAwake,
            recovery,
            powerRequests,
            founderProviderProbeHost,
            loggerFactory,
            logger);
    }

    /// <summary>Runs conservative stale recovery before Kestrel is started.</summary>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        StaleRunRecoveryResult result = await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
        if (result.Errors > 0)
        {
            logger.Warning("Startup recovery completed with {ErrorCount} bounded error(s).", result.Errors);
        }
        else
        {
            logger.Information(
                "Startup recovery completed: claims={StaleClaims}, runs={StaleRuns}, temporary={TemporaryFiles}.",
                result.StaleClaimsAbandoned,
                result.StaleRunsAbandoned,
                result.TemporaryFilesDeleted);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await KeepAwake.DisposeAsync().ConfigureAwait(false);
        await powerRequests.DisposeAsync().ConfigureAwait(false);
        await founderProviderProbeHost.DisposeAsync().ConfigureAwait(false);
        loggerFactory.Dispose();
        if (logger is IDisposable disposableLogger)
        {
            disposableLogger.Dispose();
        }
    }

    private static HostPeriodicBackgroundService CreateLoop(
        string name,
        TimeSpan interval,
        IHostLoopIteration iteration,
        HostHealthState healthState,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory) => new(
            name,
            interval,
            iteration,
            healthState,
            timeProvider,
            loggerFactory.CreateLogger<HostPeriodicBackgroundService>());

    private static WakeTaskReconciliationOptions CreateWakeOptions(
        HostBootstrapSettings settings,
        string dataDirectory)
    {
        string userId = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";
        return new(
            settings.ApplicationRoot,
            settings.RunnerExecutablePath,
            settings.RunnerWorkingDirectory,
            new(
                Path.GetFullPath(dataDirectory),
                settings.AgentDirectory,
                settings.ManifestDirectory,
                settings.DatabaseFileName),
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
