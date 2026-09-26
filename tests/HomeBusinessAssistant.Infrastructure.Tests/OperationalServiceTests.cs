using System.IO.Compression;
using System.Text.Json;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Infrastructure.Operations;
using HomeBusinessAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class OperationalServiceTests
{
    private static readonly string[] DiagnosticEntries = ["system.json", "attention.json", "recent-runs.json", "daily-summaries.json", "EXCLUSIONS.txt", "manifest.json"];

    [Test]
    public async Task DetectionIsDurableDeduplicatedAndAcknowledgementDoesNotResolve()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var service = new OperationalService(
            temporary.Database.ContextFactory,
            temporary.Database.DataDirectory,
            temporary.Database.DatabasePath,
            temporary.TimeProvider,
            OperationalPolicy.Default with { LowDiskThresholdBytes = long.MaxValue, QuietHoursStartLocal = new(23, 0), QuietHoursEndLocal = new(23, 1) });

        AttentionScanResult first = await service.DetectAsync();
        AttentionScanResult second = await service.DetectAsync();
        IReadOnlyList<AttentionItem> attention = await service.GetAttentionAsync();
        AttentionItem storage = attention.Single(item => item.DedupeKey == "storage:low-disk");
        IReadOnlyList<LocalNotification> notifications = await service.GetNotificationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Created, Is.GreaterThanOrEqualTo(1));
            Assert.That(second.Created, Is.Zero);
            Assert.That(storage.OccurrenceCount, Is.EqualTo(1));
            Assert.That(notifications.Count(item => item.DedupeKey == "storage:low-disk:1"), Is.EqualTo(1));
        });

        AttentionItem acknowledged = await service.AcknowledgeAsync(storage.Id, "test")
            ?? throw new InvalidOperationException("Attention disappeared.");
        Assert.That(acknowledged.Status, Is.EqualTo(AttentionStatus.Acknowledged));
        Assert.That(acknowledged.ResolvedAtUtc, Is.Null);
    }

    [Test]
    public async Task QuietHoursLeaveNotificationsPendingAndAcceptedDeliveryIsDurable()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var quiet = new OperationalService(
            temporary.Database.ContextFactory, temporary.Database.DataDirectory, temporary.Database.DatabasePath, temporary.TimeProvider,
            OperationalPolicy.Default with { LowDiskThresholdBytes = long.MaxValue, QuietHoursStartLocal = TimeOnly.MinValue, QuietHoursEndLocal = new(23, 59) });
        _ = await quiet.DetectAsync();
        Assert.That(await quiet.GetDeliverableNotificationsAsync(), Is.Empty);
        Assert.That((await quiet.GetNotificationsAsync()).All(item => item.Status == LocalNotificationStatus.Pending), Is.True);

        var active = new OperationalService(
            temporary.Database.ContextFactory, temporary.Database.DataDirectory, temporary.Database.DatabasePath, temporary.TimeProvider,
            OperationalPolicy.Default with { LowDiskThresholdBytes = long.MaxValue, QuietHoursStartLocal = new(23, 0), QuietHoursEndLocal = new(23, 1), GlobalThrottleSeconds = 0 });
        LocalNotification item = (await active.GetDeliverableNotificationsAsync())[0];
        await active.MarkNotificationDeliveredAsync(item.Id);
        Assert.That((await active.GetNotificationsAsync()).Single(value => value.Id == item.Id).Status, Is.EqualTo(LocalNotificationStatus.Delivered));
    }

    [Test]
    public async Task DailySummaryAndDiagnosticsAreDeterministicBoundedAndExcludeSensitiveSources()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var service = new OperationalService(
            temporary.Database.ContextFactory, temporary.Database.DataDirectory, temporary.Database.DatabasePath, temporary.TimeProvider);

        DailySummary summary = await service.GenerateDailySummaryAsync(new DateOnly(2026, 8, 29), "UTC");
        DiagnosticArchive diagnostics = await service.CreateDiagnosticsAsync();
        using var stream = new MemoryStream(diagnostics.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        string[] names = archive.Entries.Select(item => item.FullName).ToArray();
        string exclusions;
        using (var reader = new StreamReader(archive.GetEntry("EXCLUSIONS.txt")!.Open())) exclusions = await reader.ReadToEndAsync();
        using JsonDocument manifest = await JsonDocument.ParseAsync(archive.GetEntry("manifest.json")!.Open());

        Assert.Multiple(() =>
        {
            Assert.That(summary.SchemaVersion, Is.EqualTo("1.0"));
            Assert.That(summary.SummaryJson, Does.Contain("\"runs\""));
            Assert.That(names, Is.EquivalentTo(DiagnosticEntries));
            Assert.That(names, Has.None.Matches<string>(name => name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)));
            Assert.That(exclusions, Does.Contain("browser profiles"));
            Assert.That(manifest.RootElement.GetProperty("files").GetArrayLength(), Is.EqualTo(5));
            Assert.That(diagnostics.SizeBytes, Is.LessThan(10 * 1024 * 1024));
        });
    }

    [Test]
    public async Task Stage14SyntheticSmokeCoversRequiredOperationalConditionsAndRetention()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();
        Guid revisionId = Guid.NewGuid();
        Guid occurrenceId = Guid.NewGuid();
        Guid runId = Guid.NewGuid();
        string relativeArtifact = Path.Combine("artifacts", "expired.txt");
        string artifactPath = Path.Combine(temporary.Root, relativeArtifact);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        await File.WriteAllTextAsync(artifactPath, "expired synthetic diagnostic");
        await using (AssistantDbContext context = await temporary.Database.ContextFactory.CreateDbContextAsync())
        {
            string at = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            string started = now.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            string created = now.AddDays(-10).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            string expires = now.AddDays(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ConfigurationRevisions (Id, AgentId, RevisionNumber, SchemaVersion, CanonicalConfigurationJson, ConfigurationHash, ChangedBy, ChangeSummary, CreatedAtUtc) VALUES ({revisionId}, {'f' + "ounder-scout"}, {1}, {"1.0"}, {"{}"}, {new string('a', 64)}, {"smoke"}, {"Synthetic smoke configuration."}, {at})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AgentConfigurations (Id, AgentId, CurrentRevisionId, SchemaVersion, UpdatedAtUtc, ConcurrencyToken) VALUES ({Guid.NewGuid()}, {"founder-scout"}, {revisionId}, {"1.0"}, {at}, {1})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ScheduleOccurrences (Id, ScheduleId, AgentId, CommandName, ArgumentsJson, ConfigurationRevisionId, DueAtUtc, TriggerType, Status, RequiresWake, KeepSystemAwake, KeepDisplayOn, AttemptNumber, ParentOccurrenceId, ClaimedBy, ClaimedAtUtc, ClaimExpiresAtUtc, StartedAtUtc, CompletedAtUtc, CancellationRequestedAtUtc, CancellationReason, TerminalReasonCode, TerminalMessage, RunId, CreatedAtUtc, UpdatedAtUtc) VALUES ({occurrenceId}, {null}, {"founder-scout"}, {"run"}, {"{}"}, {revisionId}, {started}, {"Manual"}, {"Failed"}, {false}, {false}, {false}, {0}, {null}, {null}, {null}, {null}, {started}, {at}, {null}, {null}, {"smoke.failed"}, {"Authentication is required."}, {runId}, {started}, {at})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AgentRuns (Id, OccurrenceId, AgentId, ConfigurationRevisionId, ConfigurationHash, TriggerType, Status, RunnerVersion, AgentVersion, ManifestVersion, ExecutableHash, MachineName, ProcessId, ProcessStartedAtUtc, StartedAtUtc, LastHeartbeatAtUtc, CompletedAtUtc, DurationMilliseconds, ExitCode, SummaryText, SummaryJson, ErrorType, ErrorMessage, CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken) VALUES ({runId}, {occurrenceId}, {"founder-scout"}, {revisionId}, {new string('a', 64)}, {"Manual"}, {"Failed"}, {"test"}, {"test"}, {"1.0"}, {new string('b', 64)}, {"test"}, {null}, {null}, {started}, {started}, {at}, {300000}, {20}, {"Authentication stopped the run."}, {"{}"}, {"browser.authentication-required"}, {"Authentication is required."}, {started}, {at}, {1})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO RunArtifacts (Id, RunId, AgentId, ArtifactType, FileName, RelativePath, ContentType, SizeBytes, Sha256, CreatedAtUtc, DeleteAfterUtc) VALUES ({Guid.NewGuid()}, {runId}, {"founder-scout"}, {"diagnostic"}, {"expired.txt"}, {relativeArtifact}, {"text/plain"}, {new FileInfo(artifactPath).Length}, {new string('c', 64)}, {created}, {expires})");
        }

        IOperationalSignalSource external = new FixedSignalSource(
        [
            new("authentication", AttentionSeverity.Error, "Authentication required", "A browser account needs authentication.", "founder-scout", null, null, "smoke:authentication", "/FounderScout/Operations", new { }),
            new("remote-readiness", AttentionSeverity.Error, "Wake readiness failed", "The remote provider was not ready.", "wake-remote", null, null, "smoke:wake", "/WakeRemote", new { }),
            new("founder-opportunity", AttentionSeverity.Info, "Five Strong Connect candidates", "5 candidates are ready for review.", "founder-scout", null, null, "smoke:strong-connect:group", "/FounderScout/Candidates", new { count = 5 }),
            new("draft-review", AttentionSeverity.Warning, "Draft needs review", "1 draft requires manual review.", "founder-scout", null, null, "smoke:draft-review", "/FounderScout/Candidates", new { count = 1 }),
        ]);
        var service = new OperationalService(
            temporary.Database.ContextFactory, temporary.Database.DataDirectory, temporary.Database.DatabasePath, temporary.TimeProvider,
            OperationalPolicy.Default with { LowDiskThresholdBytes = long.MaxValue, QuietHoursStartLocal = new(23, 0), QuietHoursEndLocal = new(23, 1) }, external);

        AttentionScanResult scan = await service.DetectAsync();
        IReadOnlyList<AttentionItem> attention = await service.GetAttentionAsync();
        RetentionResult preview = await service.ApplyRetentionAsync(true, "smoke");
        RetentionResult applied = await service.ApplyRetentionAsync(false, "smoke");
        DailySummary daily = await service.GenerateDailySummaryAsync(new DateOnly(2026, 8, 29), "UTC");
        DiagnosticArchive diagnostics = await service.CreateDiagnosticsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(scan.NotificationsQueued, Is.EqualTo(attention.Count));
            Assert.That(attention.Select(item => item.Category), Does.Contain("run"));
            Assert.That(attention.Select(item => item.Category), Does.Contain("authentication"));
            Assert.That(attention.Select(item => item.Category), Does.Contain("remote-readiness"));
            Assert.That(attention.Single(item => item.Category == "founder-opportunity").Message, Does.StartWith("5 candidates"));
            Assert.That(attention.Select(item => item.Category), Does.Contain("draft-review"));
            Assert.That(attention.Select(item => item.DedupeKey), Does.Contain("storage:low-disk"));
            Assert.That(preview.Deleted, Is.GreaterThanOrEqualTo(1));
            Assert.That(applied.Deleted, Is.GreaterThanOrEqualTo(1));
            Assert.That(File.Exists(artifactPath), Is.False);
            Assert.That(daily.SummaryJson, Does.Contain("\"failed\": 1"));
            Assert.That(diagnostics.Content, Is.Not.Empty);
        });
    }

    private sealed class FixedSignalSource(IReadOnlyList<OperationalSignal> signals) : IOperationalSignalSource
    {
        public ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(signals);
        }
    }
}
