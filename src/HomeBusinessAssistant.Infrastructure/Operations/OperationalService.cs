using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Operations;

/// <summary>SQLite-backed deterministic operational feedback, retention, and diagnostics service.</summary>
public sealed class OperationalService(
    IDbContextFactory<AssistantDbContext> contextFactory,
    string dataDirectory,
    string databasePath,
    TimeProvider timeProvider,
    OperationalPolicy? policy = null,
    IOperationalSignalSource? signalSource = null,
    ProductBuildInfo? build = null) : IOperationalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string dataRoot = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private readonly string databasePath = Path.GetFullPath(databasePath);

    /// <inheritdoc />
    public OperationalPolicy Policy { get; } = policy ?? OperationalPolicy.Default;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AttentionItem>> GetAttentionAsync(bool includeResolved = false, int take = 100, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<AttentionItemEntity> query = context.Set<AttentionItemEntity>().AsNoTracking();
        if (!includeResolved)
        {
            query = query.Where(item => item.Status == nameof(AttentionStatus.Active) || item.Status == nameof(AttentionStatus.Acknowledged));
        }

        return (await query.OrderByDescending(item => item.Severity == nameof(AttentionSeverity.Critical) ? 4
                    : item.Severity == nameof(AttentionSeverity.Error) ? 3
                    : item.Severity == nameof(AttentionSeverity.Warning) ? 2 : 1)
                .ThenByDescending(item => item.LastObservedAtUtc)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<AttentionItem?> AcknowledgeAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128)
        {
            throw new ArgumentException("A bounded actor identifier is required.", nameof(actorId));
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AttentionItemEntity? entity = await context.Set<AttentionItemEntity>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        if (entity.Status == nameof(AttentionStatus.Active))
        {
            entity.Status = nameof(AttentionStatus.Acknowledged);
            entity.AcknowledgedAtUtc = nowUtc;
            entity.AcknowledgedBy = AuditRedactor.RedactText(actorId, 128);
            context.Add(Audit(nowUtc, actorId, "attention.acknowledged", "attention", id.ToString("D"), new { entity.DedupeKey }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LocalNotification>> GetNotificationsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await context.Set<LocalNotificationEntity>().AsNoTracking()
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LocalNotification>> GetDeliverableNotificationsAsync(int take = 10, CancellationToken cancellationToken = default)
    {
        if (!Policy.NotificationsEnabled || IsQuietHours(timeProvider.GetLocalNow().TimeOfDay))
        {
            return [];
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset globalSince = nowUtc.AddSeconds(-Policy.GlobalThrottleSeconds);
        bool globalThrottled = await context.Set<LocalNotificationEntity>().AsNoTracking()
            .AnyAsync(item => item.Status == nameof(LocalNotificationStatus.Delivered) && item.DeliveredAtUtc >= globalSince, cancellationToken)
            .ConfigureAwait(false);
        if (globalThrottled)
        {
            return [];
        }

        List<LocalNotificationEntity> candidates = await context.Set<LocalNotificationEntity>().AsNoTracking()
            .Where(item => item.Status == nameof(LocalNotificationStatus.Pending)
                && item.NotBeforeUtc <= nowUtc
                && (!item.ExpiresAtUtc.HasValue || item.ExpiresAtUtc > nowUtc)
                && item.DeliveryAttempts < 5)
            .OrderByDescending(item => item.Severity == nameof(AttentionSeverity.Critical) ? 4
                : item.Severity == nameof(AttentionSeverity.Error) ? 3
                : item.Severity == nameof(AttentionSeverity.Warning) ? 2 : 1)
            .ThenBy(item => item.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 25))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var deliverable = new List<LocalNotification>(candidates.Count);
        DateTimeOffset perKeySince = nowUtc.AddMinutes(-Policy.PerKeyThrottleMinutes);
        foreach (LocalNotificationEntity candidate in candidates)
        {
            bool keyThrottled = await context.Set<LocalNotificationEntity>().AsNoTracking()
                .AnyAsync(item => item.ThrottleKey == candidate.ThrottleKey
                    && item.Status == nameof(LocalNotificationStatus.Delivered)
                    && item.DeliveredAtUtc >= perKeySince, cancellationToken)
                .ConfigureAwait(false);
            if (!keyThrottled)
            {
                deliverable.Add(Map(candidate));
            }
        }

        return deliverable;
    }

    /// <inheritdoc />
    public async ValueTask MarkNotificationDeliveredAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LocalNotificationEntity? entity = await context.Set<LocalNotificationEntity>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.Status != nameof(LocalNotificationStatus.Pending))
        {
            return;
        }

        entity.Status = nameof(LocalNotificationStatus.Delivered);
        entity.DeliveredAtUtc = nowUtc;
        entity.DeliveryAttempts++;
        entity.LastErrorCode = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkNotificationAttemptFailedAsync(Guid id, string errorCode, CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        LocalNotificationEntity? entity = await context.Set<LocalNotificationEntity>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.Status != nameof(LocalNotificationStatus.Pending))
        {
            return;
        }

        entity.DeliveryAttempts++;
        entity.LastErrorCode = AuditRedactor.RedactText(errorCode, 128);
        entity.NotBeforeUtc = nowUtc.AddMinutes(Math.Min(30, Math.Pow(2, entity.DeliveryAttempts)));
        if (entity.DeliveryAttempts >= 5 || entity.ExpiresAtUtc <= nowUtc)
        {
            entity.Status = nameof(LocalNotificationStatus.Expired);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AttentionScanResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var signals = new List<Signal>();
        DateTimeOffset runWindow = nowUtc.AddDays(-7);
        List<AgentRunEntity> failedRuns = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(item => item.StartedAtUtc >= runWindow && (item.Status == nameof(AgentRunStatus.Failed)
                || item.Status == nameof(AgentRunStatus.TimedOut)
                || item.Status == nameof(AgentRunStatus.Abandoned)))
            .OrderByDescending(item => item.StartedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (AgentRunEntity run in failedRuns)
        {
            AttentionSeverity severity = run.Status == nameof(AgentRunStatus.Abandoned) ? AttentionSeverity.Error : AttentionSeverity.Warning;
            signals.Add(new("run", severity, $"{run.AgentId} run {run.Status.ToLowerInvariant()}",
                SafeMessage(run.ErrorMessage ?? run.SummaryText ?? "The run ended without a successful result."), run.AgentId, run.Id, null,
                $"run:{run.Id:D}", $"/Runs/Details?id={run.Id:D}", new { run.Status, run.ErrorType }));

            Signal? specialized = CreateSpecializedRunSignal(run);
            if (specialized is not null) signals.Add(specialized);
        }

        foreach (IGrouping<string, AgentRunEntity> group in failedRuns.GroupBy(item => item.AgentId).Where(item => item.Count() >= Policy.FailureRepeatThreshold))
        {
            signals.Add(new("repeated-failure", AttentionSeverity.Error, $"Repeated failures for {group.Key}",
                $"{group.Count()} unsuccessful runs were observed in the last seven days.", group.Key, null, null,
                $"repeated-failure:{group.Key}", $"/Runs?agentId={Uri.EscapeDataString(group.Key)}&attentionOnly=true", new { count = group.Count() }));
        }

        List<AuditEventEntity> failedOperations = await context.Set<AuditEventEntity>().AsNoTracking()
            .Where(item => item.TimestampUtc >= runWindow && item.Outcome == "Failed"
                && (item.Action.Contains("wake")
                    || item.Action.Contains("retention")
                    || item.Action.Contains("backup")
                    || item.Action.Contains("migration")))
            .OrderByDescending(item => item.TimestampUtc).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (AuditEventEntity operation in failedOperations)
        {
            string category = operation.Action.Contains("wake", StringComparison.Ordinal) ? "wake"
                : operation.Action.Contains("backup", StringComparison.Ordinal) ? "backup"
                : operation.Action.Contains("migration", StringComparison.Ordinal) ? "migration" : "retention";
            signals.Add(new(category, AttentionSeverity.Error, $"{category} operation failed",
                $"The local operation '{operation.Action}' reported a failure.", null, operation.RunId, null,
                $"audit-failure:{operation.Id:D}", "/Audit", new { operation.Action, operation.TargetType, operation.TargetId }));
        }

        List<string> missingConfigurations = await (
                from agent in context.Set<AgentDefinitionEntity>().AsNoTracking()
                where agent.Enabled && !context.Set<AgentConfigurationEntity>().Any(configuration => configuration.AgentId == agent.Id)
                select agent.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        signals.AddRange(missingConfigurations.Select(agentId => new Signal(
            "configuration", AttentionSeverity.Error, $"{agentId} configuration is missing",
            "The enabled agent cannot run until a valid configuration revision exists.", agentId, null, null,
            $"configuration-missing:{agentId}", $"/Agents/Details?id={Uri.EscapeDataString(agentId)}", new { })));

        DateTimeOffset schedulerLag = nowUtc.AddMinutes(-10);
        List<ScheduleOccurrenceEntity> late = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.DueAtUtc < schedulerLag && (item.Status == nameof(OccurrenceStatus.Ready) || item.Status == nameof(OccurrenceStatus.Planned)))
            .OrderBy(item => item.DueAtUtc).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (ScheduleOccurrenceEntity occurrence in late)
        {
            signals.Add(new("scheduler-lag", AttentionSeverity.Warning, "A scheduled occurrence is overdue",
                $"{occurrence.AgentId} was due at {occurrence.DueAtUtc:u} and remains {occurrence.Status}.", occurrence.AgentId, occurrence.RunId, occurrence.ScheduleId,
                $"scheduler-lag:{occurrence.Id:D}", $"/Schedules/Details?id={occurrence.ScheduleId:D}", new { occurrence.DueAtUtc, occurrence.Status }));
        }

        long availableBytes = GetAvailableDiskBytes();
        if (availableBytes >= 0 && availableBytes < Policy.LowDiskThresholdBytes)
        {
            signals.Add(new("storage", AttentionSeverity.Critical, "Local storage is running low",
                $"Only {availableBytes / 1_073_741_824d:N1} GiB is available on the data drive.", null, null, null,
                "storage:low-disk", "/Settings", new { availableBytes, Policy.LowDiskThresholdBytes }));
        }

        if (signalSource is not null)
        {
            IReadOnlyList<OperationalSignal> external = await signalSource.ReadSignalsAsync(cancellationToken).ConfigureAwait(false);
            signals.AddRange(external.Take(200).Select(item => new Signal(item.Category, item.Severity, item.Title, SafeMessage(item.Message),
                item.AgentId, item.RunId, item.ScheduleId, item.DedupeKey, item.LocalPath, item.Context)));
        }

        var observedKeys = signals.Select(item => item.DedupeKey).ToHashSet(StringComparer.Ordinal);
        List<AttentionItemEntity> existing = await context.Set<AttentionItemEntity>()
            .Where(item => item.Status == nameof(AttentionStatus.Active) || item.Status == nameof(AttentionStatus.Acknowledged))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int created = 0, updated = 0, resolved = 0, queued = 0;
        foreach (Signal signal in signals)
        {
            AttentionItemEntity? item = await context.Set<AttentionItemEntity>().SingleOrDefaultAsync(value => value.DedupeKey == signal.DedupeKey, cancellationToken).ConfigureAwait(false);
            bool isNew = item is null;
            if (item is null)
            {
                item = new AttentionItemEntity
                {
                    Id = Guid.NewGuid(),
                    Category = signal.Category,
                    Severity = signal.Severity.ToString(),
                    Title = signal.Title,
                    Message = signal.Message,
                    AgentId = signal.AgentId,
                    RunId = signal.RunId,
                    ScheduleId = signal.ScheduleId,
                    DedupeKey = signal.DedupeKey,
                    Status = nameof(AttentionStatus.Active),
                    FirstObservedAtUtc = nowUtc,
                    LastObservedAtUtc = nowUtc,
                    OccurrenceCount = 1,
                    ContextJson = SafeJson(signal.Context),
                    ExpiresAtUtc = nowUtc.AddDays(30),
                };
                context.Add(item);
                created++;
            }
            else
            {
                bool reactivated = item.Status is nameof(AttentionStatus.Resolved) or nameof(AttentionStatus.Suppressed);
                item.Category = signal.Category; item.Severity = signal.Severity.ToString(); item.Title = signal.Title; item.Message = signal.Message;
                item.AgentId = signal.AgentId; item.RunId = signal.RunId; item.ScheduleId = signal.ScheduleId; item.LastObservedAtUtc = nowUtc;
                item.ContextJson = SafeJson(signal.Context); item.ExpiresAtUtc = nowUtc.AddDays(30);
                if (reactivated)
                {
                    item.Status = nameof(AttentionStatus.Active); item.ResolvedAtUtc = null; item.AcknowledgedAtUtc = null; item.AcknowledgedBy = null; item.OccurrenceCount++;
                }
                updated++;
            }

            string notificationKey = $"{signal.DedupeKey}:{item.OccurrenceCount}";
            bool notificationExists = await context.Set<LocalNotificationEntity>().AnyAsync(value => value.DedupeKey == notificationKey, cancellationToken).ConfigureAwait(false);
            if (!notificationExists)
            {
                context.Add(new LocalNotificationEntity
                {
                    Id = Guid.NewGuid(),
                    AttentionItemId = item.Id,
                    Title = signal.Title,
                    Message = signal.Message,
                    Severity = signal.Severity.ToString(),
                    LocalPath = ValidateLocalPath(signal.LocalPath) ? signal.LocalPath : "/",
                    CreatedAtUtc = nowUtc,
                    NotBeforeUtc = nowUtc,
                    DeliveryAttempts = 0,
                    Status = nameof(LocalNotificationStatus.Pending),
                    DedupeKey = notificationKey,
                    ThrottleKey = signal.Category,
                    ExpiresAtUtc = nowUtc.AddDays(7),
                });
                queued++;
            }
        }

        foreach (AttentionItemEntity item in existing.Where(item => !observedKeys.Contains(item.DedupeKey)))
        {
            item.Status = nameof(AttentionStatus.Resolved); item.ResolvedAtUtc = nowUtc; resolved++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(signals.Count, created, updated, resolved, queued);
    }

    /// <inheritdoc />
    public async ValueTask<DailySummary> GenerateDailySummaryAsync(DateOnly localDate, string timeZoneId, CancellationToken cancellationToken = default)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTime localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTimeOffset startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
        DateTimeOffset endUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), zone);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentRunEntity> runs = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(item => item.StartedAtUtc >= startUtc && item.StartedAtUtc < endUtc)
            .OrderBy(item => item.StartedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        int completed = runs.Count(item => item.Status == nameof(AgentRunStatus.Completed));
        int failed = runs.Count(item => item.Status is nameof(AgentRunStatus.Failed) or nameof(AgentRunStatus.TimedOut) or nameof(AgentRunStatus.Abandoned));
        int activeAttention = await context.Set<AttentionItemEntity>().AsNoTracking()
            .CountAsync(item => item.Status == nameof(AttentionStatus.Active) || item.Status == nameof(AttentionStatus.Acknowledged), cancellationToken).ConfigureAwait(false);
        var payload = new
        {
            schemaVersion = "1.0",
            localDate = localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            timeZoneId,
            periodStartUtc = startUtc,
            periodEndUtc = endUtc,
            runs = new { total = runs.Count, completed, failed, cancelled = runs.Count - completed - failed },
            activeAttention,
            agents = runs.GroupBy(item => item.AgentId).OrderBy(item => item.Key)
                .Select(group => new { agentId = group.Key, total = group.Count(), completed = group.Count(item => item.Status == nameof(AgentRunStatus.Completed)) }),
        };
        string summaryJson = JsonSerializer.Serialize(payload, JsonOptions);
        string sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summaryJson))).ToLowerInvariant();
        int generation = (await context.Set<DailySummaryEntity>().AsNoTracking()
            .Where(item => item.LocalDate == localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) && item.TimeZoneId == timeZoneId)
            .MaxAsync(item => (int?)item.GenerationNumber, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var entity = new DailySummaryEntity
        {
            Id = Guid.NewGuid(),
            LocalDate = localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneId = timeZoneId,
            SchemaVersion = "1.0",
            SummaryText = $"{runs.Count} run(s): {completed} completed, {failed} need attention; {activeAttention} active attention item(s).",
            SummaryJson = summaryJson,
            GeneratedAtUtc = nowUtc,
            PeriodStartUtc = startUtc,
            PeriodEndUtc = endUtc,
            GenerationNumber = generation,
            SourceHash = sourceHash,
        };
        context.Add(entity);
        string dailyNotificationKey = $"daily-summary:{entity.LocalDate}:{timeZoneId}";
        if (!await context.Set<LocalNotificationEntity>().AnyAsync(item => item.DedupeKey == dailyNotificationKey, cancellationToken).ConfigureAwait(false))
        {
            context.Add(new LocalNotificationEntity
            {
                Id = Guid.NewGuid(),
                Title = $"Daily summary for {entity.LocalDate}",
                Message = entity.SummaryText,
                Severity = nameof(AttentionSeverity.Info),
                LocalPath = "/Attention",
                CreatedAtUtc = nowUtc,
                NotBeforeUtc = nowUtc,
                DeliveryAttempts = 0,
                Status = nameof(LocalNotificationStatus.Pending),
                DedupeKey = dailyNotificationKey,
                ThrottleKey = "daily-summary",
                ExpiresAtUtc = nowUtc.AddDays(2),
            });
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<string?> RegenerateRunSummaryAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? run = await context.Set<AgentRunEntity>().SingleOrDefaultAsync(item => item.Id == runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.CompletedAtUtc is null)
        {
            return null;
        }

        int eventCount = await context.Set<AgentRunEventEntity>().AsNoTracking().CountAsync(item => item.RunId == runId, cancellationToken).ConfigureAwait(false);
        int warningCount = await context.Set<AgentRunEventEntity>().AsNoTracking().CountAsync(item => item.RunId == runId && item.Level == "Warning", cancellationToken).ConfigureAwait(false);
        int errorCount = await context.Set<AgentRunEventEntity>().AsNoTracking().CountAsync(item => item.RunId == runId && item.Level == "Error", cancellationToken).ConfigureAwait(false);
        int metricCount = await context.Set<AgentRunMetricEntity>().AsNoTracking().CountAsync(item => item.RunId == runId, cancellationToken).ConfigureAwait(false);
        int artifactCount = await context.Set<RunArtifactEntity>().AsNoTracking().CountAsync(item => item.RunId == runId, cancellationToken).ConfigureAwait(false);
        string summary = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            generatedBy = "platform-summary-regeneration",
            runId = run.Id,
            run.AgentId,
            run.Status,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.DurationMilliseconds,
            run.ExitCode,
            run.ErrorType,
            persisted = new { eventCount, warningCount, errorCount, metricCount, artifactCount },
        }, JsonOptions);
        run.SummaryJson = summary;
        run.SummaryText = $"{run.AgentId} {run.Status.ToLowerInvariant()} with {eventCount} event(s), {metricCount} metric(s), and {artifactCount} artifact(s).";
        run.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        run.ConcurrencyToken++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return summary;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DailySummary>> GetDailySummariesAsync(int take = 30, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await context.Set<DailySummaryEntity>().AsNoTracking().OrderByDescending(item => item.GeneratedAtUtc)
                .Take(Math.Clamp(take, 1, 365)).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<RetentionResult> ApplyRetentionAsync(bool dryRun, string actorId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<RunArtifactEntity> artifacts = await context.Set<RunArtifactEntity>()
            .Where(item => item.DeleteAfterUtc.HasValue && item.DeleteAfterUtc <= nowUtc
                && !context.Set<AgentRunEntity>().Any(run => run.Id == item.RunId && (run.Status == nameof(AgentRunStatus.Running) || run.Status == nameof(AgentRunStatus.Starting))))
            .Take(1_000).ToListAsync(cancellationToken).ConfigureAwait(false);
        int deleted = 0, skipped = 0, failed = 0;
        long bytes = 0;
        foreach (RunArtifactEntity artifact in artifacts)
        {
            string fullPath = Path.GetFullPath(Path.Combine(dataRoot, artifact.RelativePath));
            if (!fullPath.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(fullPath, databasePath, StringComparison.OrdinalIgnoreCase))
            {
                skipped++; continue;
            }

            if (dryRun)
            {
                deleted++; bytes += artifact.SizeBytes; continue;
            }

            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                context.Remove(artifact); deleted++; bytes += artifact.SizeBytes;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        DateTimeOffset notificationsBefore = nowUtc.AddDays(-Policy.NotificationRetentionDays);
        DateTimeOffset attentionBefore = nowUtc.AddDays(-Policy.ResolvedAttentionRetentionDays);
        DateTimeOffset summariesBefore = nowUtc.AddDays(-Policy.DailySummaryRetentionDays);
        IQueryable<LocalNotificationEntity> expiredNotifications = context.Set<LocalNotificationEntity>().Where(item =>
            item.CreatedAtUtc < notificationsBefore && item.Status != nameof(LocalNotificationStatus.Pending)
            || item.AttentionItemId.HasValue && context.Set<AttentionItemEntity>().Any(attention => attention.Id == item.AttentionItemId
                && attention.ResolvedAtUtc < attentionBefore && attention.Status == nameof(AttentionStatus.Resolved)));
        int metadata = await expiredNotifications.CountAsync(cancellationToken).ConfigureAwait(false)
            + await context.Set<AttentionItemEntity>().Where(item => item.ResolvedAtUtc < attentionBefore && item.Status == nameof(AttentionStatus.Resolved)).CountAsync(cancellationToken).ConfigureAwait(false)
            + await context.Set<DailySummaryEntity>().Where(item => item.GeneratedAtUtc < summariesBefore).CountAsync(cancellationToken).ConfigureAwait(false);
        if (!dryRun)
        {
            _ = await expiredNotifications.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            _ = await context.Set<AttentionItemEntity>().Where(item => item.ResolvedAtUtc < attentionBefore && item.Status == nameof(AttentionStatus.Resolved)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            _ = await context.Set<DailySummaryEntity>().Where(item => item.GeneratedAtUtc < summariesBefore).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            context.Add(Audit(nowUtc, actorId, "retention.applied", "storage", "local-data", new { deleted, skipped, failed, bytes, metadata }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new(dryRun, artifacts.Count + metadata, deleted + metadata, skipped, failed, bytes,
            ["audit-and-configuration-history-preserved", "active-run-artifacts-protected", "paths-confined-to-data-root"]);
    }

    /// <inheritdoc />
    public async ValueTask<DiagnosticArchive> CreateDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        IReadOnlyList<AttentionItem> attention = await GetAttentionAsync(true, 100, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DailySummary> summaries = await GetDailySummariesAsync(30, cancellationToken).ConfigureAwait(false);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var runs = await context.Set<AgentRunEntity>().AsNoTracking().OrderByDescending(item => item.StartedAtUtc).Take(100)
            .Select(item => new { item.Id, item.AgentId, item.Status, item.StartedAtUtc, item.CompletedAtUtc, item.ExitCode, item.ErrorType }).ToListAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var manifest = new List<object>();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddJson(archive, manifest, "system.json", new { schemaVersion = "1.0", capturedAtUtc = nowUtc, machine = Environment.MachineName, os = Environment.OSVersion.VersionString, build });
            AddJson(archive, manifest, "attention.json", attention);
            AddJson(archive, manifest, "recent-runs.json", runs);
            AddJson(archive, manifest, "daily-summaries.json", summaries.Select(item => new { item.LocalDate, item.TimeZoneId, item.SummaryText, item.GeneratedAtUtc, item.SourceHash }));
            AddText(archive, manifest, "EXCLUSIONS.txt", "Excluded by design: secrets and DPAPI payloads; configuration JSON and revisions; database files; browser profiles, cookies, authentication state, and account paths; raw founder profiles and photographs; AI prompts/responses; draft bodies; screenshots and raw diagnostics; artifact contents; and raw log messages.\n");
            AddJson(archive, null, "manifest.json", new { schemaVersion = "1.0", capturedAtUtc = nowUtc, files = manifest });
        }

        byte[] bytes = output.ToArray();
        if (bytes.Length > 10 * 1024 * 1024) throw new InvalidOperationException("The bounded diagnostics archive exceeded 10 MiB.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(bytes, $"home-business-assistant-diagnostics-{nowUtc:yyyyMMdd-HHmmss}.zip", hash, bytes.Length);
    }

    private static AttentionItem Map(AttentionItemEntity item) => new(item.Id, item.Category, Enum.Parse<AttentionSeverity>(item.Severity), item.Title, item.Message,
        item.AgentId, item.RunId, item.ScheduleId, item.DedupeKey, Enum.Parse<AttentionStatus>(item.Status), item.FirstObservedAtUtc, item.LastObservedAtUtc,
        item.OccurrenceCount, item.AcknowledgedAtUtc, item.AcknowledgedBy, item.ResolvedAtUtc, item.ContextJson, item.ExpiresAtUtc);
    private static LocalNotification Map(LocalNotificationEntity item) => new(item.Id, item.AttentionItemId, item.Title, item.Message, Enum.Parse<AttentionSeverity>(item.Severity),
        item.LocalPath, item.CreatedAtUtc, item.NotBeforeUtc, item.DeliveredAtUtc, item.DeliveryAttempts, Enum.Parse<LocalNotificationStatus>(item.Status), item.DedupeKey,
        item.ThrottleKey, item.ExpiresAtUtc, item.LastErrorCode);
    private static DailySummary Map(DailySummaryEntity item) => new(item.Id, DateOnly.ParseExact(item.LocalDate, "yyyy-MM-dd"), item.TimeZoneId, item.SchemaVersion,
        item.SummaryText, item.SummaryJson, item.GeneratedAtUtc, item.PeriodStartUtc, item.PeriodEndUtc, item.GenerationNumber, item.SourceHash);

    private bool IsQuietHours(TimeSpan localTime)
    {
        TimeSpan start = Policy.QuietHoursStartLocal.ToTimeSpan();
        TimeSpan end = Policy.QuietHoursEndLocal.ToTimeSpan();
        return start <= end ? localTime >= start && localTime < end : localTime >= start || localTime < end;
    }

    private long GetAvailableDiskBytes()
    {
        string? root = Path.GetPathRoot(dataRoot);
        try { return string.IsNullOrWhiteSpace(root) ? -1 : new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return -1; }
    }

    private static string SafeMessage(string value) => AuditRedactor.RedactText(value, 1_000);
    private static string SafeJson(object value) => AuditRedactor.Redact(JsonSerializer.SerializeToElement(value));
    private static bool ValidateLocalPath(string? value) => value is { Length: > 0 and <= 512 } && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains("..", StringComparison.Ordinal) && !value.Contains('\\') && !value.Contains("http", StringComparison.OrdinalIgnoreCase);

    private static Signal? CreateSpecializedRunSignal(AgentRunEntity run)
    {
        string code = (run.ErrorType ?? string.Empty).ToLowerInvariant();
        (string Category, string Title, AttentionSeverity Severity)? match = code switch
        {
            _ when code.Contains("auth", StringComparison.Ordinal) => ("authentication", "Agent authentication is required", AttentionSeverity.Error),
            _ when code.Contains("access", StringComparison.Ordinal) || code.Contains("denied", StringComparison.Ordinal) => ("access-denied", "Agent access was denied", AttentionSeverity.Error),
            _ when code.Contains("thrott", StringComparison.Ordinal) => ("throttling", "Agent source is throttling requests", AttentionSeverity.Warning),
            _ when code.Contains("challenge", StringComparison.Ordinal) || code.Contains("captcha", StringComparison.Ordinal) => ("challenge", "An interactive challenge stopped automation", AttentionSeverity.Critical),
            _ when code.Contains("parser", StringComparison.Ordinal) => ("parser-health", "Agent parser health failed", AttentionSeverity.Error),
            _ when code.Contains("secret", StringComparison.Ordinal) || code.Contains("config", StringComparison.Ordinal) => ("configuration", "Agent configuration or secret reference failed", AttentionSeverity.Error),
            _ when code.Contains("wake", StringComparison.Ordinal) => ("wake", "Windows wake operation needs attention", AttentionSeverity.Error),
            _ when code.Contains("remote", StringComparison.Ordinal) || code.Contains("network", StringComparison.Ordinal) => ("remote-readiness", "Wake & Remote readiness failed", AttentionSeverity.Warning),
            _ => null,
        };
        return match is null ? null : new(match.Value.Category, match.Value.Severity, match.Value.Title,
            SafeMessage(run.ErrorMessage ?? run.ErrorType ?? "The run reported an operational stop condition."), run.AgentId, run.Id, null,
            $"{match.Value.Category}:run:{run.Id:D}", $"/Runs/Details?id={run.Id:D}", new { run.ErrorType });
    }

    private static AuditEventEntity Audit(DateTimeOffset atUtc, string actorId, string action, string targetType, string targetId, object data) => new()
    {
        Id = Guid.NewGuid(),
        TimestampUtc = atUtc,
        ActorType = "User",
        ActorId = AuditRedactor.RedactText(actorId, 128),
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Outcome = "Succeeded",
        CorrelationId = Guid.NewGuid(),
        DataJson = SafeJson(data),
    };

    private static void AddJson(ZipArchive archive, List<object>? manifest, string name, object value) => AddBytes(archive, manifest, name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static void AddText(ZipArchive archive, List<object>? manifest, string name, string value) => AddBytes(archive, manifest, name, Encoding.UTF8.GetBytes(value));
    private static void AddBytes(ZipArchive archive, List<object>? manifest, string name, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using Stream stream = entry.Open(); stream.Write(bytes);
        manifest?.Add(new { path = name, sizeBytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
    }

    private sealed record Signal(string Category, AttentionSeverity Severity, string Title, string Message, string? AgentId, Guid? RunId,
        Guid? ScheduleId, string DedupeKey, string LocalPath, object Context);
}
