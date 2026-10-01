using System.Data.Common;
using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Artifacts;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class PersistenceIntegrationTests
{
    private static readonly AgentId FounderScoutId = AgentId.Parse("founder-scout");
    private static readonly string[] ExpectedTables =
    [
        "AgentConfigurations",
        "AgentDefinitions",
        "AgentLeases",
        "AgentRunEvents",
        "AgentRunMetrics",
        "AgentRuns",
        "AgentSchedules",
        "AttentionItems",
        "AuditEvents",
        "ConfigurationRevisions",
        "DailySummaries",
        "LocalNotifications",
        "OnboardingChecks",
        "OnboardingAgentSelections",
        "OnboardingSessions",
        "RunArtifacts",
        "ScheduleOccurrences",
        "SystemSettings",
        "__EFMigrationsHistory",
    ];
    private static readonly string[] ExpectedBuiltInAgents = ["founder-scout", "wake-remote"];

    [Test]
    public async Task InitializationAppliesMigrationPragmasIndexesAndSeedDataIdempotently()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        await using AssistantDbContext context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        string[] tableNames = await ReadNamesAsync(context, "table");
        string[] indexNames = await ReadNamesAsync(context, "index");

        await context.Database.OpenConnectionAsync();
        string journalMode = await ScalarAsync(context.Database.GetDbConnection(), "PRAGMA journal_mode;");
        string foreignKeys = await ScalarAsync(context.Database.GetDbConnection(), "PRAGMA foreign_keys;");
        string busyTimeout = await ScalarAsync(context.Database.GetDbConnection(), "PRAGMA busy_timeout;");
        string synchronous = await ScalarAsync(context.Database.GetDbConnection(), "PRAGMA synchronous;");

        Assert.Multiple(() =>
        {
            Assert.That(tableNames, Is.SupersetOf(ExpectedTables));
            Assert.That(indexNames, Does.Contain("UX_ConfigurationRevisions_AgentId_Hash"));
            Assert.That(indexNames, Does.Contain("UX_ScheduleOccurrences_ScheduleId_DueAtUtc"));
            Assert.That(indexNames, Does.Contain("IX_ScheduleOccurrences_ScheduleId_Status_DueAtUtc"));
            Assert.That(indexNames, Does.Contain("IX_ScheduleOccurrences_RequiresWake_Status_DueAtUtc"));
            Assert.That(indexNames, Does.Contain("UX_AgentRunEvents_RunId_Sequence"));
            Assert.That(indexNames, Does.Contain("UX_OnboardingSessions_ActiveSlot"));
            Assert.That(indexNames, Does.Contain("UX_OnboardingChecks_LatestIdentity"));
            Assert.That(journalMode, Is.EqualTo("wal").IgnoreCase);
            Assert.That(foreignKeys, Is.EqualTo("1"));
            Assert.That(busyTimeout, Is.EqualTo("5000"));
            Assert.That(synchronous, Is.EqualTo("1"));
        });

        var definitions = new AgentDefinitionRepository(temporary.Database.ContextFactory);
        Assert.That((await definitions.GetEnabledAsync()).Select(item => item.Id.Value),
            Is.EqualTo(ExpectedBuiltInAgents));
        Assert.That(await definitions.SetEnabledAsync(FounderScoutId, enabled: false), Is.True);
        string initializationAuditCountBefore = await ScalarAsync(
            context.Database.GetDbConnection(),
            "SELECT COUNT(*) FROM AuditEvents WHERE Action = 'database.initialized';");

        _ = await AssistantDatabase.InitializeAsync(
            new AssistantDatabaseSettings(
                temporary.Root,
                ManifestDirectory: Path.Combine(FindRepositoryRoot(), "manifests")),
            temporary.TimeProvider);
        AgentDefinitionRecord founderScout = await definitions.GetAsync(FounderScoutId)
            ?? throw new InvalidOperationException("Founder Scout seed is missing.");
        var settings = new SystemSettingRepository(temporary.Database.ContextFactory);
        SystemSettingRecord defaultTimeZone = await settings.GetAsync("scheduler.default-time-zone")
            ?? throw new InvalidOperationException("The default scheduling time zone was not seeded.");
        string initializationAuditCountAfter = await ScalarAsync(
            context.Database.GetDbConnection(),
            "SELECT COUNT(*) FROM AuditEvents WHERE Action = 'database.initialized';");
        Assert.Multiple(() =>
        {
            Assert.That(founderScout.Enabled, Is.False, "Manifest reseeding must preserve mutable enabled state.");
            Assert.That(founderScout.SupportedCommandsJson,
                Is.EqualTo("[\"start\",\"run\",\"discover\",\"analyze\",\"analyze-candidate\",\"authenticate\",\"import\",\"report\",\"diagnose\",\"record-fixture\"]"));
            Assert.That(founderScout.SupportsScheduling, Is.True);
            Assert.That(founderScout.SupportsManualRun, Is.True);
            Assert.That(defaultTimeZone.ValueJson, Is.EqualTo("{\"timeZoneId\":\"Central Standard Time\"}"));
            Assert.That(initializationAuditCountBefore, Is.EqualTo("1"));
            Assert.That(initializationAuditCountAfter, Is.EqualTo(initializationAuditCountBefore));
        });
    }

    [Test]
    public async Task ConfigurationSaveIsCanonicalImmutableIdempotentAndConcurrencySafe()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var service = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        JsonElement firstJson = JsonSerializer.Deserialize<JsonElement>("{\"z\":2,\"schemaVersion\":\"1.0\",\"a\":1}");
        JsonElement reorderedJson = JsonSerializer.Deserialize<JsonElement>("{\"a\":1,\"schemaVersion\":\"1.0\",\"z\":2}");

        Task<SaveConfigurationResult>[] racing = Enumerable.Range(0, 6)
            .Select(index => service.SaveAsync(CreateConfigurationRequest(index % 2 == 0 ? firstJson : reorderedJson)).AsTask())
            .ToArray();
        SaveConfigurationResult[] results = await Task.WhenAll(racing);
        SaveConfigurationResult first = results.Single(result => result.Created);
        SaveConfigurationResult noOp = await service.SaveAsync(CreateConfigurationRequest(reorderedJson));
        AgentConfigurationRecord current = await service.GetCurrentAsync(FounderScoutId)
            ?? throw new InvalidOperationException("Current configuration was not persisted.");
        IReadOnlyList<ConfigurationRevisionRecord> history = await service.GetHistoryAsync(FounderScoutId);

        Assert.Multiple(() =>
        {
            Assert.That(first.Created, Is.True);
            Assert.That(noOp.Created, Is.False);
            Assert.That(noOp.CurrentChanged, Is.False);
            Assert.That(noOp.Revision.Id, Is.EqualTo(first.Revision.Id));
            Assert.That(current.CurrentRevisionId, Is.EqualTo(first.Revision.Id));
            Assert.That(first.Revision.CanonicalConfigurationJson,
                Is.EqualTo("{\"a\":1,\"schemaVersion\":\"1.0\",\"z\":2}"));
        });

        Assert.That(results.Select(result => result.Revision.Id), Is.All.EqualTo(first.Revision.Id));
        Assert.That(await service.GetHistoryAsync(FounderScoutId), Has.Count.EqualTo(1));

        await using AssistantDbContext context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        int audits = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM AuditEvents WHERE Action = 'configuration.revision-created'")
            .SingleAsync();
        Assert.That(audits, Is.EqualTo(1), "Idempotent saves must not duplicate audit events.");
    }

    [Test]
    public async Task ConfigurationSaveRejectsAStaleDisplayedRevisionAndHash()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var service = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        SaveConfigurationResult first = await service.SaveAsync(CreateConfigurationRequest(
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", value = 1 })));
        SaveConfigurationResult second = await service.SaveAsync(new(
            FounderScoutId,
            "1.0",
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", value = 2 }),
            "test-user",
            "Second revision",
            Guid.NewGuid(),
            first.Revision.RevisionNumber,
            first.Revision.ConfigurationHash));

        ConfigurationConcurrencyException? exception = Assert.ThrowsAsync<ConfigurationConcurrencyException>(async () =>
            await service.SaveAsync(new(
                FounderScoutId,
                "1.0",
                JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", value = 3 }),
                "test-user",
                "Stale revision",
                Guid.NewGuid(),
                first.Revision.RevisionNumber,
                first.Revision.ConfigurationHash)));
        AgentConfigurationRecord current = await service.GetCurrentAsync(FounderScoutId)
            ?? throw new InvalidOperationException("Current configuration was not persisted.");
        IReadOnlyList<ConfigurationRevisionRecord> history = await service.GetHistoryAsync(FounderScoutId);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ExpectedRevisionNumber, Is.EqualTo(first.Revision.RevisionNumber));
            Assert.That(exception.ActualRevisionNumber, Is.EqualTo(second.Revision.RevisionNumber));
            Assert.That(current.CurrentRevisionId, Is.EqualTo(second.Revision.Id));
            Assert.That(history, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task CompetingOccurrenceClaimsAndLeasesHaveOneWinner()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        OccurrenceId occurrenceId = OccurrenceId.New();
        _ = await occurrences.CreateIfAbsentAsync(new CreateOccurrenceRequest(
            occurrenceId,
            ScheduleId: null,
            FounderScoutId,
            "run",
            "{}",
            revision.Id,
            temporary.TimeProvider.GetUtcNow(),
            TriggerType.ManualUi,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Ready));

        Task<ScheduleOccurrenceRecord?>[] claimTasks = Enumerable.Range(0, 8)
            .Select(index => occurrences.TryClaimAsync(
                occurrenceId,
                $"runner-{index}",
                temporary.TimeProvider.GetUtcNow(),
                TimeSpan.FromMinutes(1)).AsTask())
            .ToArray();
        ScheduleOccurrenceRecord?[] claims = await Task.WhenAll(claimTasks);
        Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));

        var leases = new LeaseManager(temporary.Database.ContextFactory);
        Task<AgentLeaseRecord?>[] leaseTasks = Enumerable.Range(0, 8)
            .Select(index => leases.TryAcquireAsync(
                "agent:founder-scout",
                $"runner-{index}",
                temporary.TimeProvider.GetUtcNow(),
                TimeSpan.FromMinutes(1)).AsTask())
            .ToArray();
        AgentLeaseRecord?[] acquired = await Task.WhenAll(leaseTasks);
        AgentLeaseRecord winner = acquired.Single(lease => lease is not null)!;
        Assert.That(winner.FencingToken, Is.EqualTo(1));
        AgentLeaseRecord renewed = await leases.TryRenewAsync(
            winner,
            temporary.TimeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(2)) ?? throw new InvalidOperationException("Lease was not renewed.");
        Assert.That(renewed.ExpiresAtUtc, Is.GreaterThan(winner.ExpiresAtUtc));
        Assert.That(await leases.ReleaseAsync(winner), Is.True);

        AgentLeaseRecord reacquired = await leases.TryAcquireAsync(
            "agent:founder-scout",
            "runner-new",
            temporary.TimeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(1)) ?? throw new InvalidOperationException("Lease was not reacquired.");
        bool staleRelease = await leases.ReleaseAsync(winner);
        Assert.Multiple(() =>
        {
            Assert.That(reacquired.FencingToken, Is.EqualTo(2));
            Assert.That(staleRelease, Is.False,
                "A stale fencing token must not release a newer lease.");
        });
    }

    [Test]
    public async Task OnboardingAuthenticationOccurrencePermissionRoundTripsAndDefaultsFalse()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        ScheduleOccurrenceRecord authentication = await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            null,
            FounderScoutId,
            "authenticate",
            "{\"accountId\":\"synthetic-account\"}",
            revision.Id,
            temporary.TimeProvider.GetUtcNow(),
            TriggerType.ManualUi,
            0,
            null,
            OccurrenceStatus.Ready,
            KeepSystemAwake: true,
            KeepDisplayOn: true,
            AllowDisabledAgent: true));
        ScheduleOccurrenceRecord ordinary = await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            null,
            FounderScoutId,
            "run",
            "{}",
            revision.Id,
            temporary.TimeProvider.GetUtcNow(),
            TriggerType.ManualUi,
            0,
            null,
            OccurrenceStatus.Ready));

        Assert.Multiple(() =>
        {
            Assert.That(authentication.AllowDisabledAgent, Is.True);
            Assert.That(authentication.CommandName, Is.EqualTo("authenticate"));
            Assert.That(ordinary.AllowDisabledAgent, Is.False);
        });
    }

    [Test]
    public async Task RunEventsAuditAndArtifactsPersistWithIntegrityAndRedaction()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        (AgentRunRecord run, AgentRunRepository runs) = await CreateClaimedRunAsync(temporary);
        await runs.AppendEventAsync(new AgentRunEventRecord(
            Guid.NewGuid(), run.Id, -1, temporary.TimeProvider.GetUtcNow(), "Information", "runner.created", "Created", "{}"));
        Assert.ThrowsAsync<DbUpdateException>(async () => await runs.AppendEventAsync(new AgentRunEventRecord(
            Guid.NewGuid(), run.Id, -1, temporary.TimeProvider.GetUtcNow(), "Information", "runner.duplicate", "Duplicate", "{}")));
        await runs.AppendMetricAsync(new AgentRunMetricRecord(
            Guid.NewGuid(), run.Id, "items", 1, TextValue: null, "count", "{}", temporary.TimeProvider.GetUtcNow()));

        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        _ = await occurrences.TryTransitionAsync(new(
            run.OccurrenceId,
            [OccurrenceStatus.Starting],
            OccurrenceStatus.Running,
            temporary.TimeProvider.GetUtcNow(),
            "test.running",
            Message: null));
        Assert.That(await occurrences.RequestCancellationAsync(
            run.OccurrenceId,
            temporary.TimeProvider.GetUtcNow(),
            "Operator requested cancellation; Bearer sensitive-token"), Is.True);
        AgentRunRecord terminal = await runs.MarkTerminalAsync(
            run.Id,
            AgentRunStatus.Completed,
            temporary.TimeProvider.GetUtcNow().AddSeconds(2),
            exitCode: 0,
            summaryText: "Completed",
            summaryJson: "{\"password\":\"must-not-persist\",\"items\":1}",
            errorType: null,
            errorMessage: null,
            expectedConcurrencyToken: run.ConcurrencyToken)
            ?? throw new InvalidOperationException("Run was not marked terminal.");
        ScheduleOccurrenceRecord completedOccurrence = await occurrences.GetAsync(run.OccurrenceId)
            ?? throw new InvalidOperationException("Occurrence was not found.");

        var auditWriter = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        AuditEventRecord audit = await auditWriter.WriteAsync(new WriteAuditEventRequest(
            AuditActorType.System,
            "runner",
            "run.checked",
            "agent-run",
            run.Id.ToString(),
            AuditOutcome.Succeeded,
            Guid.NewGuid(),
            run.Id,
            JsonSerializer.SerializeToElement(new
            {
                password = "must-not-persist",
                reference = "secret://tests/value",
                authorization = "Bearer sensitive-token",
            })));

        byte[] contents = Encoding.UTF8.GetBytes("bounded artifact contents");
        var store = new FileSystemArtifactStore(temporary.Database.ContextFactory, temporary.Root, temporary.TimeProvider);
        RunArtifactRecord artifact;
        await using (var input = new MemoryStream(contents, writable: false))
        {
            artifact = await store.WriteAsync(new ArtifactWriteRequest(
                run.Id,
                FounderScoutId,
                "diagnostic",
                "report.txt",
                "text/plain",
                input,
                DeleteAfterUtc: null));
        }

        await using Stream stored = await store.OpenReadAsync(artifact.Id);
        using var reader = new StreamReader(stored, Encoding.UTF8);
        string persistedContents = await reader.ReadToEndAsync();
        Assert.Multiple(() =>
        {
            Assert.That(audit.DataJson, Does.Not.Contain("must-not-persist"));
            Assert.That(audit.DataJson, Does.Not.Contain("sensitive-token"));
            Assert.That(audit.DataJson, Does.Not.Contain("tests/value"));
            Assert.That(terminal.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(terminal.SummaryJson, Does.Not.Contain("must-not-persist"));
            Assert.That(completedOccurrence.Status, Is.EqualTo(OccurrenceStatus.Completed));
            Assert.That(completedOccurrence.TerminalReasonCode, Is.EqualTo("run.completed"));
            Assert.That(completedOccurrence.CancellationReason, Does.Not.Contain("sensitive-token"));
            Assert.That(persistedContents, Is.EqualTo("bounded artifact contents"));
            Assert.That(artifact.SizeBytes, Is.EqualTo(contents.Length));
            Assert.That(artifact.Sha256, Has.Length.EqualTo(64));
            Assert.That(Path.IsPathRooted(artifact.RelativePath), Is.False);
        });

        await using var traversalContent = new MemoryStream(contents, writable: false);
        Assert.ThrowsAsync<ArgumentException>(async () => await store.WriteAsync(new ArtifactWriteRequest(
            run.Id,
            FounderScoutId,
            "diagnostic",
            "../escape.txt",
            "text/plain",
            traversalContent,
            DeleteAfterUtc: null)));

        var boundedStore = new FileSystemArtifactStore(
            temporary.Database.ContextFactory,
            temporary.Root,
            temporary.TimeProvider,
            maximumArtifactBytes: 8);
        await using var oversizedContent = new MemoryStream(new byte[9], writable: false);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await boundedStore.WriteAsync(new ArtifactWriteRequest(
            run.Id,
            FounderScoutId,
            "diagnostic",
            "oversized.bin",
            "application/octet-stream",
            oversizedContent,
            DeleteAfterUtc: null)));
        Assert.That(Directory.GetFiles(Path.Combine(temporary.Root, "artifacts"), "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task AuditRowsAreAppendOnlyThroughTheDbContext()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var auditWriter = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        AuditEventRecord audit = await auditWriter.WriteAsync(new WriteAuditEventRequest(
            AuditActorType.System,
            "test",
            "append-only.checked",
            "database",
            "assistant",
            AuditOutcome.Informational,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(new { safe = true })));

        await using AssistantDbContext context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        Type auditEntityType = context.Model.GetEntityTypes()
            .Single(entityType => entityType.ClrType.Name == "AuditEventEntity")
            .ClrType;
        object entity = await context.FindAsync(auditEntityType, [audit.Id])
            ?? throw new InvalidOperationException("Audit entity was not found.");
        context.Entry(entity).State = EntityState.Deleted;

        Assert.ThrowsAsync<InvalidOperationException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public void BootstrapRejectsAnExistingFileAsTheDataDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hba-bootstrap-file-{Guid.NewGuid():N}");
        File.WriteAllText(path, "not a directory");
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await AssistantDatabase.InitializeAsync(
                new AssistantDatabaseSettings(path, ManifestDirectory: Path.Combine(FindRepositoryRoot(), "manifests"))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ScheduleAndSystemSettingRoundTripUseCanonicalJsonAndConcurrency()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var schedules = new ScheduleRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();
        var schedule = new AgentScheduleRecord(
            Guid.NewGuid(),
            FounderScoutId,
            "Daily scout",
            "run",
            "{}",
            ScheduleKind.Daily,
            ScheduleDefinitionJson.Serialize(new DailyScheduleDefinition(new TimeOnly(9, 30))),
            "Central Standard Time",
            MisfirePolicy.RunImmediately,
            ConcurrencyPolicy.Forbid,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromSeconds(30),
            RetryPolicyJson.Serialize(new RetryPolicyDefinition(2, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1))),
            WakePolicy.IfSleeping,
            PinnedConfigurationRevisionId: null,
            AllowDisabledAgent: false,
            IsEnabled: true,
            IsPaused: false,
            PausedUntilUtc: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            ConcurrencyToken: 0);
        AgentScheduleRecord savedSchedule = await schedules.SaveAsync(schedule, expectedConcurrencyToken: null);
        Assert.That((await schedules.GetActiveAsync()).Single().DefinitionJson,
            Is.EqualTo("{\"localTime\":\"09:30:00\",\"type\":\"daily\",\"version\":\"1.0\"}"));
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await schedules.SaveAsync(
            savedSchedule with { Name = "Stale" },
            expectedConcurrencyToken: 0));

        var settings = new SystemSettingRepository(temporary.Database.ContextFactory);
        SystemSettingRecord savedSetting = await settings.SaveAsync(
            new SystemSettingRecord("retention.audit", "{\"days\":30,\"enabled\":true}", "1.0", now, 0),
            expectedConcurrencyToken: null);
        SystemSettingRecord loaded = await settings.GetAsync("retention.audit")
            ?? throw new InvalidOperationException("Setting was not persisted.");
        Assert.Multiple(() =>
        {
            Assert.That(savedSetting.ConcurrencyToken, Is.EqualTo(1));
            Assert.That(loaded.ValueJson, Is.EqualTo("{\"days\":30,\"enabled\":true}"));
        });
    }

    private static SaveConfigurationRequest CreateConfigurationRequest(JsonElement configuration) => new(
        FounderScoutId,
        "1.0",
        configuration,
        "integration-test",
        "Saved a non-sensitive test configuration.",
        Guid.NewGuid());

    private static async ValueTask<ConfigurationRevisionRecord> CreateConfigurationAsync(TemporaryAssistantDatabase temporary)
    {
        var service = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        SaveConfigurationResult result = await service.SaveAsync(CreateConfigurationRequest(
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", enabled = true })));
        return result.Revision;
    }

    private static async ValueTask<(AgentRunRecord Run, AgentRunRepository Repository)> CreateClaimedRunAsync(
        TemporaryAssistantDatabase temporary)
    {
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        OccurrenceId occurrenceId = OccurrenceId.New();
        _ = await occurrences.CreateIfAbsentAsync(new CreateOccurrenceRequest(
            occurrenceId,
            ScheduleId: null,
            FounderScoutId,
            "run",
            "{}",
            revision.Id,
            temporary.TimeProvider.GetUtcNow(),
            TriggerType.CommandLine,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Ready));
        _ = await occurrences.TryClaimAsync(
            occurrenceId,
            "integration-test",
            temporary.TimeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(1));

        var repository = new AgentRunRepository(temporary.Database.ContextFactory);
        var run = new AgentRunRecord(
            AgentRunId.New(),
            occurrenceId,
            FounderScoutId,
            revision.Id,
            revision.ConfigurationHash,
            TriggerType.CommandLine,
            AgentRunStatus.Starting,
            "test-runner",
            "1.0.0",
            "1.0",
            new string('0', 64),
            "test-machine",
            123,
            ProcessStartedAtUtc: temporary.TimeProvider.GetUtcNow(),
            StartedAtUtc: temporary.TimeProvider.GetUtcNow(),
            LastHeartbeatAtUtc: null,
            CompletedAtUtc: null,
            DurationMilliseconds: null,
            ExitCode: null,
            SummaryText: null,
            SummaryJson: null,
            ErrorType: null,
            ErrorMessage: null,
            CreatedAtUtc: temporary.TimeProvider.GetUtcNow(),
            UpdatedAtUtc: temporary.TimeProvider.GetUtcNow(),
            ConcurrencyToken: 1);
        return (await repository.CreateAsync(run), repository);
    }

    private static async Task<string[]> ReadNamesAsync(AssistantDbContext context, string type)
    {
        await context.Database.OpenConnectionAsync();
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type ORDER BY name;";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$type";
        parameter.Value = type;
        _ = command.Parameters.Add(parameter);
        var names = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static async Task<string> ScalarAsync(DbConnection connection, string commandText)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
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
