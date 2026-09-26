using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed run storage and append-only event/metric persistence.</summary>
public sealed class AgentRunRepository(IDbContextFactory<AssistantDbContext> contextFactory) : IAgentRunRepository
{
    private static readonly string[] ActiveRunStatuses =
    [
        AgentRunStatus.Starting.ToString(),
        AgentRunStatus.Running.ToString(),
    ];

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord> CreateAsync(
        AgentRunRecord run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateRun(run);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int linked = await context.Set<ScheduleOccurrenceEntity>()
            .Where(item => item.Id == run.OccurrenceId.Value
                && item.Status == AgentRunStatus.Claimed.ToString()
                && item.RunId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RunId, run.Id.Value)
                .SetProperty(item => item.Status, AgentRunStatus.Starting.ToString())
                .SetProperty(item => item.StartedAtUtc, run.StartedAtUtc.ToUniversalTime())
                .SetProperty(item => item.UpdatedAtUtc, run.StartedAtUtc.ToUniversalTime()), cancellationToken)
            .ConfigureAwait(false);
        if (linked != 1)
        {
            throw new InvalidOperationException("A run can only be created for one claimed, unlinked occurrence.");
        }

        AgentRunEntity entity = ToEntity(run);
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord?> GetAsync(AgentRunId runId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? entity = await context.Set<AgentRunEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord?> GetForOccurrenceAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? entity = await context.Set<AgentRunEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OccurrenceId == occurrenceId.Value, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentRunRecord>> GetStaleAsync(
        StaleRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaximumResults is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "The stale-run result limit is invalid.");
        }

        DateTimeOffset beforeUtc = query.HeartbeatBeforeUtc.ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentRunEntity> entities = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(item => ActiveRunStatuses.Contains(item.Status)
                && (item.LastHeartbeatAtUtc == null || item.LastHeartbeatAtUtc < beforeUtc))
            .OrderBy(item => item.LastHeartbeatAtUtc)
            .ThenBy(item => item.Id)
            .Take(query.MaximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord?> MarkRunningAsync(
        AgentRunId runId,
        int processId,
        DateTimeOffset processStartedAtUtc,
        DateTimeOffset heartbeatAtUtc,
        long expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        if (processId <= 0 || processStartedAtUtc == default || heartbeatAtUtc == default)
        {
            throw new ArgumentException("The child-process identity is invalid.", nameof(processId));
        }

        DateTimeOffset processStarted = processStartedAtUtc.ToUniversalTime();
        DateTimeOffset heartbeat = heartbeatAtUtc.ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? current = await context.Set<AgentRunEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (current is null || current.ConcurrencyToken != expectedConcurrencyToken || current.Status != AgentRunStatus.Starting.ToString())
        {
            return null;
        }

        int occurrenceUpdated = await context.Set<ScheduleOccurrenceEntity>()
            .Where(item => item.Id == current.OccurrenceId
                && item.RunId == runId.Value
                && item.Status == OccurrenceStatus.Starting.ToString())
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, OccurrenceStatus.Running.ToString())
                .SetProperty(item => item.UpdatedAtUtc, heartbeat), cancellationToken)
            .ConfigureAwait(false);
        if (occurrenceUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        int runUpdated = await context.Set<AgentRunEntity>()
            .Where(item => item.Id == runId.Value
                && item.ConcurrencyToken == expectedConcurrencyToken
                && item.Status == AgentRunStatus.Starting.ToString())
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AgentRunStatus.Running.ToString())
                .SetProperty(item => item.ProcessId, processId)
                .SetProperty(item => item.ProcessStartedAtUtc, processStarted)
                .SetProperty(item => item.LastHeartbeatAtUtc, heartbeat)
                .SetProperty(item => item.UpdatedAtUtc, heartbeat)
                .SetProperty(item => item.ConcurrencyToken, item => item.ConcurrencyToken + 1), cancellationToken)
            .ConfigureAwait(false);
        if (runUpdated != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(runId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord?> UpdateHeartbeatAsync(
        AgentRunId runId,
        DateTimeOffset heartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset heartbeat = heartbeatAtUtc.ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<AgentRunEntity>()
            .Where(item => item.Id == runId.Value && ActiveRunStatuses.Contains(item.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastHeartbeatAtUtc, heartbeat)
                .SetProperty(item => item.UpdatedAtUtc, heartbeat)
                .SetProperty(item => item.ConcurrencyToken, item => item.ConcurrencyToken + 1), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1 ? await GetAsync(runId, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public async ValueTask AppendEventAsync(
        AgentRunEventRecord runEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);
        if (runEvent.Id == Guid.Empty
            || runEvent.Sequence == 0
            || string.IsNullOrWhiteSpace(runEvent.Level)
            || runEvent.Level.Length > 32
            || string.IsNullOrWhiteSpace(runEvent.EventType)
            || runEvent.EventType.Length > 64
            || runEvent.Message.Length > 4_096
            || runEvent.DataJson.Length > 262_144)
        {
            throw new ArgumentException("The run event is invalid.", nameof(runEvent));
        }

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(new AgentRunEventEntity
        {
            Id = runEvent.Id,
            RunId = runEvent.RunId.Value,
            Sequence = runEvent.Sequence,
            TimestampUtc = runEvent.TimestampUtc.ToUniversalTime(),
            Level = runEvent.Level,
            EventType = runEvent.EventType,
            Message = runEvent.Message,
            DataJson = runEvent.DataJson,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AppendMetricAsync(
        AgentRunMetricRecord metric,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (metric.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(metric.Name)
            || metric.Name.Length > 128
            || (metric.NumericValue.HasValue && !double.IsFinite(metric.NumericValue.Value))
            || (!metric.NumericValue.HasValue && string.IsNullOrWhiteSpace(metric.TextValue))
            || metric.TextValue?.Length > 4_096
            || metric.Unit?.Length > 32)
        {
            throw new ArgumentException("The run metric is invalid.", nameof(metric));
        }

        using JsonDocument tagsDocument = JsonDocument.Parse(metric.TagsJson, new JsonDocumentOptions { MaxDepth = 32 });
        string canonicalTags = CanonicalJson.Serialize(tagsDocument.RootElement);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(new AgentRunMetricEntity
        {
            Id = metric.Id,
            RunId = metric.RunId.Value,
            Name = metric.Name,
            NumericValue = metric.NumericValue,
            TextValue = metric.TextValue,
            Unit = metric.Unit,
            TagsJson = canonicalTags,
            TimestampUtc = metric.TimestampUtc.ToUniversalTime(),
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AgentRunRecord?> MarkTerminalAsync(
        AgentRunId runId,
        AgentRunStatus terminalStatus,
        DateTimeOffset completedAtUtc,
        int? exitCode,
        string? summaryText,
        string? summaryJson,
        string? errorType,
        string? errorMessage,
        long expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        if (terminalStatus is not (AgentRunStatus.Completed
            or AgentRunStatus.Failed
            or AgentRunStatus.TimedOut
            or AgentRunStatus.Cancelled
            or AgentRunStatus.Abandoned))
        {
            throw new ArgumentException("The requested run status is not terminal.", nameof(terminalStatus));
        }

        string? safeSummaryJson = null;
        if (summaryJson is not null)
        {
            using JsonDocument summaryDocument = JsonDocument.Parse(summaryJson, new JsonDocumentOptions { MaxDepth = 64 });
            safeSummaryJson = AuditRedactor.Redact(summaryDocument.RootElement);
        }

        string? safeSummaryText = summaryText is null ? null : AuditRedactor.RedactText(summaryText, 8_192);
        string? safeErrorType = errorType is null ? null : AuditRedactor.RedactText(errorType, 256);
        string? safeErrorMessage = errorMessage is null ? null : AuditRedactor.RedactText(errorMessage, 8_192);

        DateTimeOffset normalizedCompleted = completedAtUtc.ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? current = await context.Set<AgentRunEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        if (IsTerminal(current.Status))
        {
            return PersistenceMapper.Map(current);
        }

        if (current.ConcurrencyToken != expectedConcurrencyToken)
        {
            return null;
        }

        long durationMilliseconds = Math.Max(0, (long)(normalizedCompleted - current.StartedAtUtc).TotalMilliseconds);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string starting = OccurrenceStatus.Starting.ToString();
        string running = OccurrenceStatus.Running.ToString();
        string cancellationRequested = OccurrenceStatus.CancellationRequested.ToString();
        bool occurrenceCanComplete = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .AnyAsync(item => item.Id == current.OccurrenceId
                && item.RunId == runId.Value
                && (item.Status == starting || item.Status == running || item.Status == cancellationRequested), cancellationToken)
            .ConfigureAwait(false);
        if (!occurrenceCanComplete)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The occurrence must be starting, running, or cancellation-requested before terminal completion.");
        }

        int updated = await context.Set<AgentRunEntity>()
            .Where(item => item.Id == runId.Value && item.ConcurrencyToken == expectedConcurrencyToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, terminalStatus.ToString())
                .SetProperty(item => item.CompletedAtUtc, normalizedCompleted)
                .SetProperty(item => item.DurationMilliseconds, durationMilliseconds)
                .SetProperty(item => item.ExitCode, exitCode)
                .SetProperty(item => item.SummaryText, safeSummaryText)
                .SetProperty(item => item.SummaryJson, safeSummaryJson)
                .SetProperty(item => item.ErrorType, safeErrorType)
                .SetProperty(item => item.ErrorMessage, safeErrorMessage)
                .SetProperty(item => item.UpdatedAtUtc, normalizedCompleted)
                .SetProperty(item => item.ConcurrencyToken, item => item.ConcurrencyToken + 1), cancellationToken)
            .ConfigureAwait(false);
        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        _ = await context.Set<ScheduleOccurrenceEntity>()
            .Where(item => item.Id == current.OccurrenceId && item.RunId == runId.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, terminalStatus.ToString())
                .SetProperty(item => item.CompletedAtUtc, normalizedCompleted)
                .SetProperty(item => item.TerminalReasonCode, GetOccurrenceTerminalReason(terminalStatus))
                .SetProperty(item => item.TerminalMessage, safeErrorMessage ?? safeSummaryText)
                .SetProperty(item => item.UpdatedAtUtc, normalizedCompleted), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(runId, cancellationToken).ConfigureAwait(false);
    }

    private static AgentRunEntity ToEntity(AgentRunRecord run) => new()
    {
        Id = run.Id.Value,
        OccurrenceId = run.OccurrenceId.Value,
        AgentId = run.AgentId.Value,
        ConfigurationRevisionId = run.ConfigurationRevisionId,
        ConfigurationHash = run.ConfigurationHash,
        TriggerType = run.TriggerType.ToString(),
        Status = run.Status.ToString(),
        RunnerVersion = run.RunnerVersion,
        AgentVersion = run.AgentVersion,
        ManifestVersion = run.ManifestVersion,
        ExecutableHash = run.ExecutableHash,
        MachineName = run.MachineName,
        ProcessId = run.ProcessId,
        ProcessStartedAtUtc = run.ProcessStartedAtUtc?.ToUniversalTime(),
        StartedAtUtc = run.StartedAtUtc.ToUniversalTime(),
        LastHeartbeatAtUtc = run.LastHeartbeatAtUtc?.ToUniversalTime(),
        CompletedAtUtc = run.CompletedAtUtc?.ToUniversalTime(),
        DurationMilliseconds = run.DurationMilliseconds,
        ExitCode = run.ExitCode,
        SummaryText = run.SummaryText,
        SummaryJson = run.SummaryJson,
        ErrorType = run.ErrorType,
        ErrorMessage = run.ErrorMessage,
        CreatedAtUtc = run.CreatedAtUtc.ToUniversalTime(),
        UpdatedAtUtc = run.UpdatedAtUtc.ToUniversalTime(),
        ConcurrencyToken = run.ConcurrencyToken,
    };

    private static void ValidateRun(AgentRunRecord run)
    {
        string[] required =
        [
            run.ConfigurationHash,
            run.RunnerVersion,
            run.AgentVersion,
            run.ManifestVersion,
            run.ExecutableHash,
            run.MachineName,
        ];
        if (required.Any(string.IsNullOrWhiteSpace)
            || !Enum.IsDefined(run.TriggerType)
            || !Enum.IsDefined(run.Status)
            || run.ProcessId is < 1)
        {
            throw new ArgumentException("The run record is invalid.", nameof(run));
        }
    }

    private static string GetOccurrenceTerminalReason(AgentRunStatus status) => status switch
    {
        AgentRunStatus.Completed => "run.completed",
        AgentRunStatus.Failed => "run.failed",
        AgentRunStatus.TimedOut => "run.timed-out",
        AgentRunStatus.Cancelled => "run.cancelled",
        AgentRunStatus.Abandoned => "run.abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static bool IsTerminal(string status) => status is nameof(AgentRunStatus.Completed)
        or nameof(AgentRunStatus.Failed)
        or nameof(AgentRunStatus.TimedOut)
        or nameof(AgentRunStatus.Cancelled)
        or nameof(AgentRunStatus.Abandoned);
}
