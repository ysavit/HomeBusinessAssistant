using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

namespace HomeBusinessAssistant.Runner;

internal static class SchedulingDemoCommand
{
    private static readonly DateTimeOffset ControlledStart = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);

    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string? dataDirectory = ParseDataDirectory(arguments);
        bool ownsDirectory = dataDirectory is null;
        dataDirectory ??= Path.Combine(Path.GetTempPath(), $"hba-scheduling-demo-{Guid.NewGuid():N}");
        var timeProvider = new DemoTimeProvider(ControlledStart);

        try
        {
            AssistantDatabase database = await AssistantDatabase.InitializeAsync(
                new AssistantDatabaseSettings(dataDirectory),
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            SchedulingServices services = CreateServices(database, timeProvider);
            ConfigurationRevisionRecord founderRevision = await SaveConfigurationAsync(
                services.Configurations,
                AgentId.Parse("founder-scout"),
                cancellationToken).ConfigureAwait(false);
            _ = await SaveConfigurationAsync(
                services.Configurations,
                AgentId.Parse("wake-remote"),
                cancellationToken).ConfigureAwait(false);

            AgentScheduleRecord founderSchedule = await SaveScheduleAsync(
                services,
                CreateSchedule(
                    AgentId.Parse("founder-scout"),
                    "Founder discovery cooldown",
                    new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true),
                    WakePolicy.Never,
                    timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
            AgentScheduleRecord wakeSchedule = await SaveScheduleAsync(
                services,
                CreateSchedule(
                    AgentId.Parse("wake-remote"),
                    "Sunday wake window",
                    new WeekdayScheduleDefinition([DayOfWeek.Sunday], new TimeOnly(13, 0)),
                    WakePolicy.Required,
                    timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);

            ScheduleReconciliationSummary first = await services.Reconciler.ReconcileAsync(
                "scheduling-demo",
                cancellationToken).ConfigureAwait(false);
            ScheduleOccurrenceRecord founderInitial = (await services.Occurrences.GetForScheduleAsync(
                founderSchedule.Id,
                dueFromUtc: null,
                dueThroughUtc: null,
                maximumResults: 10,
                cancellationToken).ConfigureAwait(false)).Single();
            ScheduleOccurrenceRecord wakeOccurrence = (await services.Occurrences.GetForScheduleAsync(
                wakeSchedule.Id,
                dueFromUtc: null,
                dueThroughUtc: null,
                maximumResults: 10,
                cancellationToken).ConfigureAwait(false)).Single();

            DateTimeOffset completedAt = ControlledStart.AddMinutes(2);
            await MoveToRunningAsync(services.Occurrences, founderInitial.Id, ControlledStart, cancellationToken).ConfigureAwait(false);
            _ = await TransitionAsync(
                services.Occurrences,
                founderInitial.Id,
                OccurrenceStatus.Running,
                OccurrenceStatus.Completed,
                completedAt,
                "run.completed",
                cancellationToken).ConfigureAwait(false);
            timeProvider.SetUtcNow(completedAt);
            FixedDelayCompletionResult completion = await services.FixedDelay.OnOccurrenceTerminalAsync(
                founderInitial.Id,
                completedAt,
                cancellationToken).ConfigureAwait(false);
            ScheduleReconciliationSummary second = await services.Reconciler.ReconcileAsync(
                "scheduling-demo",
                cancellationToken).ConfigureAwait(false);
            DateTimeOffset expectedNext = completedAt.AddMinutes(10);
            bool verified = completion.Created
                && completion.NextDueAtUtc == expectedNext
                && founderInitial.ConfigurationRevisionId == founderRevision.Id;

            await output.WriteLineAsync($"firstReconciliationCreated={first.OccurrencesCreated}").ConfigureAwait(false);
            await output.WriteLineAsync($"founderOccurrenceId={founderInitial.Id}").ConfigureAwait(false);
            await output.WriteLineAsync($"founderInitialDueUtc={founderInitial.DueAtUtc:O}").ConfigureAwait(false);
            await output.WriteLineAsync($"wakeOccurrenceId={wakeOccurrence.Id}").ConfigureAwait(false);
            await output.WriteLineAsync($"wakeDueUtc={wakeOccurrence.DueAtUtc:O}").ConfigureAwait(false);
            await output.WriteLineAsync($"founderCompletedAtUtc={completedAt:O}").ConfigureAwait(false);
            await output.WriteLineAsync($"founderNextOccurrenceId={completion.NextOccurrenceId}").ConfigureAwait(false);
            await output.WriteLineAsync($"founderNextDueUtc={completion.NextDueAtUtc:O}").ConfigureAwait(false);
            await output.WriteLineAsync($"expectedFounderNextDueUtc={expectedNext:O}").ConfigureAwait(false);
            await output.WriteLineAsync($"secondReconciliationCreated={second.OccurrencesCreated}").ConfigureAwait(false);
            await output.WriteLineAsync($"fixedDelayVerified={verified}").ConfigureAwait(false);
            await output.WriteLineAsync(verified ? "schedulingDemo=passed" : "schedulingDemo=failed").ConfigureAwait(false);
            return verified ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            await error.WriteLineAsync($"Scheduling demonstration failed: {exception.GetType().Name}.").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            if (ownsDirectory)
            {
                DeleteOwnedDirectory(dataDirectory);
            }
        }
    }

    private static SchedulingServices CreateServices(AssistantDatabase database, TimeProvider timeProvider)
    {
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            timeProvider);
        var schedules = new ScheduleRepository(database.ContextFactory, timeProvider);
        var occurrences = new OccurrenceRepository(database.ContextFactory, timeProvider);
        var agentDefinitions = new AgentDefinitionRepository(database.ContextFactory, timeProvider);
        var audit = new AuditWriter(database.ContextFactory, timeProvider);
        var leases = new LeaseManager(database.ContextFactory);
        var timeZones = new ScheduleTimeZoneService();
        var validator = new AgentScheduleValidator(agentDefinitions, configurations, schedules, timeZones);
        var planner = new ScheduleOccurrencePlanner(
            new ScheduleCalculator(timeZones),
            occurrences,
            configurations,
            SchedulingOptions.Default);
        var policy = new OccurrencePolicyService(occurrences, SchedulingOptions.Default);
        var reconciler = new ScheduleReconciler(
            leases,
            schedules,
            agentDefinitions,
            validator,
            planner,
            policy,
            occurrences,
            audit,
            timeProvider,
            SchedulingOptions.Default);
        var fixedDelay = new FixedDelayCompletionService(occurrences, schedules, configurations, audit);
        return new(configurations, schedules, occurrences, validator, reconciler, fixedDelay);
    }

    private static async ValueTask<ConfigurationRevisionRecord> SaveConfigurationAsync(
        IAgentConfigurationService configurations,
        AgentId agentId,
        CancellationToken cancellationToken)
    {
        SaveConfigurationResult result = await configurations.SaveAsync(new(
            agentId,
            "1.0",
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", demo = true }),
            "scheduling-demo",
            "Created non-sensitive scheduling demonstration configuration.",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        return result.Revision;
    }

    private static AgentScheduleRecord CreateSchedule(
        AgentId agentId,
        string name,
        ScheduleDefinition definition,
        WakePolicy wakePolicy,
        DateTimeOffset nowUtc) => new(
            Guid.NewGuid(),
            agentId,
            name,
            "run",
            "{}",
            definition.Kind,
            ScheduleDefinitionJson.Serialize(definition),
            SchedulingDefaults.DefaultWindowsTimeZoneId,
            MisfirePolicy.RunImmediately,
            ConcurrencyPolicy.Forbid,
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(30),
            RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
            wakePolicy,
            PinnedConfigurationRevisionId: null,
            AllowDisabledAgent: false,
            IsEnabled: true,
            IsPaused: false,
            PausedUntilUtc: null,
            nowUtc,
            nowUtc,
            ConcurrencyToken: 0);

    private static async ValueTask<AgentScheduleRecord> SaveScheduleAsync(
        SchedulingServices services,
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken)
    {
        AgentScheduleValidationResult validation = await services.Validator.ValidateAsync(schedule, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new AgentScheduleValidationException(validation.Errors);
        }

        return await services.Schedules.SaveAsync(schedule, expectedConcurrencyToken: null, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask MoveToRunningAsync(
        IOccurrenceRepository occurrences,
        OccurrenceId occurrenceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ScheduleOccurrenceRecord claimed = await occurrences.TryClaimAsync(
            occurrenceId,
            "scheduling-demo",
            nowUtc,
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The demonstration occurrence could not be claimed.");
        ScheduleOccurrenceRecord starting = await TransitionAsync(
            occurrences,
            claimed.Id,
            OccurrenceStatus.Claimed,
            OccurrenceStatus.Starting,
            nowUtc,
            "runner.starting",
            cancellationToken).ConfigureAwait(false);
        _ = await TransitionAsync(
            occurrences,
            starting.Id,
            OccurrenceStatus.Starting,
            OccurrenceStatus.Running,
            nowUtc,
            "runner.running",
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<ScheduleOccurrenceRecord> TransitionAsync(
        IOccurrenceRepository occurrences,
        OccurrenceId occurrenceId,
        OccurrenceStatus expected,
        OccurrenceStatus target,
        DateTimeOffset atUtc,
        string reason,
        CancellationToken cancellationToken) =>
        await occurrences.TryTransitionAsync(new(
            occurrenceId,
            [expected],
            target,
            atUtc,
            reason,
            Message: null), cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The demonstration occurrence transition failed.");

    private static string? ParseDataDirectory(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        if (arguments.Count != 2 || arguments[0] != "--data-directory" || string.IsNullOrWhiteSpace(arguments[1]))
        {
            throw new ArgumentException("Usage: scheduling-demo [--data-directory <absolute-path>]");
        }

        return Path.GetFullPath(arguments[1]);
    }

    private static void DeleteOwnedDirectory(string dataDirectory)
    {
        string fullPath = Path.GetFullPath(dataDirectory);
        string tempPath = Path.GetFullPath(Path.GetTempPath());
        if (!fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith("hba-scheduling-demo-", StringComparison.Ordinal))
        {
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed record SchedulingServices(
        IAgentConfigurationService Configurations,
        IScheduleRepository Schedules,
        IOccurrenceRepository Occurrences,
        IAgentScheduleValidator Validator,
        IScheduleReconciler Reconciler,
        IFixedDelayCompletionService FixedDelay);

    private sealed class DemoTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value.ToUniversalTime();
    }
}
