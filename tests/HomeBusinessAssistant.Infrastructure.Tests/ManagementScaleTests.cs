using System.Diagnostics;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Management;
using HomeBusinessAssistant.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class ManagementScaleTests
{
    [Test]
    public async Task OneHundredThousandRunEventsUseTheRunSequenceIndexAndReturnABoundedDetailPage()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        Guid revisionId = Guid.NewGuid();
        Guid occurrenceId = Guid.NewGuid();
        Guid runId = Guid.NewGuid();
        string at = temporary.TimeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await using (AssistantDbContext context = await temporary.Database.ContextFactory.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ConfigurationRevisions (Id, AgentId, RevisionNumber, SchemaVersion, CanonicalConfigurationJson, ConfigurationHash, ChangedBy, ChangeSummary, CreatedAtUtc) VALUES ({revisionId}, {"wake-remote"}, {1}, {"1.0"}, {"{}"}, {new string('a', 64)}, {"scale-test"}, {"Stage 16 event scale fixture."}, {at})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AgentConfigurations (Id, AgentId, CurrentRevisionId, SchemaVersion, UpdatedAtUtc, ConcurrencyToken) VALUES ({Guid.NewGuid()}, {"wake-remote"}, {revisionId}, {"1.0"}, {at}, {1})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ScheduleOccurrences (Id, ScheduleId, AgentId, CommandName, ArgumentsJson, ConfigurationRevisionId, DueAtUtc, TriggerType, Status, RequiresWake, KeepSystemAwake, KeepDisplayOn, AttemptNumber, ParentOccurrenceId, ClaimedBy, ClaimedAtUtc, ClaimExpiresAtUtc, StartedAtUtc, CompletedAtUtc, CancellationRequestedAtUtc, CancellationReason, TerminalReasonCode, TerminalMessage, RunId, CreatedAtUtc, UpdatedAtUtc) VALUES ({occurrenceId}, {null}, {"wake-remote"}, {"run"}, {"{}"}, {revisionId}, {at}, {"ManualUi"}, {"Completed"}, {false}, {false}, {false}, {0}, {null}, {null}, {null}, {null}, {at}, {at}, {null}, {null}, {"completed"}, {"Scale fixture completed."}, {runId}, {at}, {at})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AgentRuns (Id, OccurrenceId, AgentId, ConfigurationRevisionId, ConfigurationHash, TriggerType, Status, RunnerVersion, AgentVersion, ManifestVersion, ExecutableHash, MachineName, ProcessId, ProcessStartedAtUtc, StartedAtUtc, LastHeartbeatAtUtc, CompletedAtUtc, DurationMilliseconds, ExitCode, SummaryText, SummaryJson, ErrorType, ErrorMessage, CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken) VALUES ({runId}, {occurrenceId}, {"wake-remote"}, {revisionId}, {new string('a', 64)}, {"ManualUi"}, {"Completed"}, {"1.0.0"}, {"1.0.0"}, {"1.0"}, {new string('b', 64)}, {"scale-test"}, {null}, {null}, {at}, {at}, {at}, {1000}, {0}, {"Scale fixture."}, {"{}"}, {null}, {null}, {at}, {at}, {1})");
        }

        await using var connection = new SqliteConnection(
            $"Data Source={temporary.Database.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.CommandText = """
                WITH digits(value) AS (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)),
                sequence(value) AS (
                    SELECT ones.value + tens.value * 10 + hundreds.value * 100 + thousands.value * 1000 + tenThousands.value * 10000 + 1
                    FROM digits ones
                    CROSS JOIN digits tens
                    CROSS JOIN digits hundreds
                    CROSS JOIN digits thousands
                    CROSS JOIN digits tenThousands
                )
                INSERT INTO AgentRunEvents (Id, RunId, Sequence, TimestampUtc, Level, EventType, Message, DataJson)
                SELECT
                    printf('%08x-0000-4000-8000-%012x', value, value),
                    $runId,
                    value,
                    '2026-08-31 12:00:00+00:00',
                    'Information',
                    'progress',
                    'Synthetic bounded event',
                    '{}'
                FROM sequence;
                """;
            insert.Parameters.AddWithValue("$runId", runId);
            Assert.That(await insert.ExecuteNonQueryAsync(), Is.EqualTo(100_000));
        }

        string plan;
        await using (SqliteCommand explain = connection.CreateCommand())
        {
            explain.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM AgentRunEvents WHERE RunId = $runId ORDER BY Sequence LIMIT 500";
            explain.Parameters.AddWithValue("$runId", runId);
            await using SqliteDataReader reader = await explain.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(3));
            }

            plan = string.Join(" | ", rows);
        }

        var service = new ManagementQueryService(
            temporary.Database.ContextFactory,
            temporary.Database.DatabasePath,
            temporary.TimeProvider);
        Stopwatch stopwatch = Stopwatch.StartNew();
        var detail = await service.GetRunAsync(AgentRunId.FromGuid(runId));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.Events, Has.Count.EqualTo(500));
            Assert.That(detail.Events.Select(item => item.Sequence), Is.EqualTo(Enumerable.Range(1, 500).Select(value => (long)value)));
            Assert.That(plan, Does.Contain("UX_AgentRunEvents_RunId_Sequence"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        });
    }
}
