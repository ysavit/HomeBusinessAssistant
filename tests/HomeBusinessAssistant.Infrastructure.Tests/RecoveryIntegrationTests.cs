using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Execution;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class RecoveryIntegrationTests
{
    private static readonly AgentId FounderScoutId = AgentId.Parse("founder-scout");

    [Test]
    public async Task RecoveryAbandonsExpiredClaimAndOrphanedStartingRunAndCleansStaleTemp()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var runs = new AgentRunRepository(temporary.Database.ContextFactory);
        ScheduleOccurrenceRecord staleClaim = await CreateClaimAsync(
            occurrences, revision.Id, now.AddMinutes(-10));
        ScheduleOccurrenceRecord runClaim = await CreateClaimAsync(
            occurrences, revision.Id, now.AddMinutes(-10));
        AgentRunId runId = AgentRunId.New();
        _ = await runs.CreateAsync(new(
            runId,
            runClaim.Id,
            FounderScoutId,
            revision.Id,
            revision.ConfigurationHash,
            TriggerType.Schedule,
            AgentRunStatus.Starting,
            "test-runner",
            "1.0.0",
            "1.0",
            new string('a', 64),
            Environment.MachineName,
            ProcessId: null,
            ProcessStartedAtUtc: null,
            StartedAtUtc: now.AddMinutes(-10),
            LastHeartbeatAtUtc: now.AddMinutes(-10),
            CompletedAtUtc: null,
            DurationMilliseconds: null,
            ExitCode: null,
            SummaryText: null,
            SummaryJson: null,
            ErrorType: null,
            ErrorMessage: null,
            CreatedAtUtc: now.AddMinutes(-10),
            UpdatedAtUtc: now.AddMinutes(-10),
            ConcurrencyToken: 1));

        string agentRoot = Path.Combine(temporary.Root, "installed-agents");
        Directory.CreateDirectory(agentRoot);
        string manifestRoot = Path.Combine(FindRepositoryRoot(), "manifests");
        var agents = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        var schedules = new ScheduleRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var fixedDelay = new FixedDelayCompletionService(occurrences, schedules, configurations, audit);
        var finalizer = new RunFinalizer(runs, occurrences, schedules, fixedDelay, audit, temporary.TimeProvider);
        var temporaryFiles = new RunTemporaryFileManager(temporary.Database.DataDirectory);
        AgentRunId staleTemporaryId = AgentRunId.New();
        string staleTemporaryPath = await temporaryFiles.WriteExecutionInputAsync(staleTemporaryId, "{}", "{}");
        File.SetLastWriteTimeUtc(staleTemporaryPath, now.AddHours(-1).UtcDateTime);
        RunnerSupervisionOptions options = RunnerSupervisionOptions.Default with
        {
            StaleRunThreshold = TimeSpan.FromMinutes(1),
            StaleTemporaryFileAge = TimeSpan.FromMinutes(1),
        };
        var recovery = new StaleRunRecoveryService(
            runs,
            occurrences,
            agents,
            new ExecutableIntegrityService(agentRoot, manifestRoot),
            finalizer,
            fixedDelay,
            audit,
            temporaryFiles,
            temporary.TimeProvider,
            options);

        StaleRunRecoveryResult result = await recovery.RecoverAsync();
        ScheduleOccurrenceRecord recoveredClaim = await occurrences.GetAsync(staleClaim.Id)
            ?? throw new InvalidOperationException("The stale claim disappeared.");
        AgentRunRecord recoveredRun = await runs.GetAsync(runId)
            ?? throw new InvalidOperationException("The stale run disappeared.");

        Assert.Multiple(() =>
        {
            Assert.That(result.StaleClaimsAbandoned, Is.EqualTo(1));
            Assert.That(result.StaleRunsAbandoned, Is.EqualTo(1));
            Assert.That(result.TemporaryFilesDeleted, Is.EqualTo(1));
            Assert.That(result.Errors, Is.Zero);
            Assert.That(recoveredClaim.Status, Is.EqualTo(OccurrenceStatus.Abandoned));
            Assert.That(recoveredRun.Status, Is.EqualTo(AgentRunStatus.Abandoned));
            Assert.That(File.Exists(staleTemporaryPath), Is.False);
        });
    }

    private static async ValueTask<ScheduleOccurrenceRecord> CreateClaimAsync(
        OccurrenceRepository occurrences,
        Guid revisionId,
        DateTimeOffset claimedAtUtc)
    {
        OccurrenceId id = OccurrenceId.New();
        _ = await occurrences.CreateIfAbsentAsync(new(
            id,
            ScheduleId: null,
            FounderScoutId,
            "run",
            "{}",
            revisionId,
            claimedAtUtc,
            TriggerType.Schedule,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            OccurrenceStatus.Ready));
        return await occurrences.TryClaimAsync(
            id,
            $"stale-runner-{id}",
            claimedAtUtc,
            TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException("The test occurrence was not claimed.");
    }

    private static async ValueTask<ConfigurationRevisionRecord> CreateConfigurationAsync(
        TemporaryAssistantDatabase temporary)
    {
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        SaveConfigurationResult result = await configurations.SaveAsync(new(
            FounderScoutId,
            "1.0",
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0" }),
            "recovery-test",
            "Create recovery test configuration.",
            Guid.NewGuid()));
        return result.Revision;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
