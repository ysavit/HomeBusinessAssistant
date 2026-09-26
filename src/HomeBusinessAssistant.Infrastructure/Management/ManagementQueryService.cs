using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Management;

/// <summary>EF-backed bounded read model for the local management UI.</summary>
public sealed class ManagementQueryService(
    IDbContextFactory<AssistantDbContext> contextFactory,
    string databasePath,
    TimeProvider timeProvider) : IManagementQueryService
{
    private readonly string databasePath = Path.GetFullPath(databasePath);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ManagementAgentItem>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentDefinitionEntity> agents = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ManagementAgentItem>(agents.Count);
        foreach (AgentDefinitionEntity agent in agents)
        {
            results.Add(await MapAgentAsync(context, agent, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<ManagementAgentDetail?> GetAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentDefinitionEntity? entity = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == agentId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        ManagementAgentItem item = await MapAgentAsync(context, entity, cancellationToken).ConfigureAwait(false);
        AgentConfigurationRecord? configuration = await GetConfigurationAsync(context, agentId.Value, cancellationToken).ConfigureAwait(false);
        List<ConfigurationRevisionEntity> revisionEntities = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .Where(value => value.AgentId == agentId.Value)
            .OrderByDescending(value => value.RevisionNumber)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AgentScheduleEntity> scheduleEntities = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .Where(value => value.AgentId == agentId.Value)
            .OrderBy(value => value.Name)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AgentRunEntity> runEntities = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(value => value.AgentId == agentId.Value)
            .OrderByDescending(value => value.StartedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(
            item,
            configuration,
            revisionEntities.Select(PersistenceMapper.Map).ToArray(),
            scheduleEntities.Select(PersistenceMapper.Map).ToArray(),
            runEntities.Select(PersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ManagementScheduleItem>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentScheduleEntity> schedules = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Take(1_000)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ManagementScheduleItem>(schedules.Count);
        foreach (AgentScheduleEntity schedule in schedules)
        {
            results.Add(await MapScheduleAsync(context, schedule, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<ManagementScheduleDetail?> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentScheduleEntity? schedule = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scheduleId, cancellationToken)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            return null;
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        List<ScheduleOccurrenceEntity> future = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.ScheduleId == scheduleId && item.DueAtUtc >= nowUtc)
            .OrderBy(item => item.DueAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AgentRunEntity> runs = await (
                from run in context.Set<AgentRunEntity>().AsNoTracking()
                join occurrence in context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                    on run.OccurrenceId equals occurrence.Id
                where occurrence.ScheduleId == scheduleId
                orderby run.StartedAtUtc descending
                select run)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(
            await MapScheduleAsync(context, schedule, cancellationToken).ConfigureAwait(false),
            future.Select(PersistenceMapper.Map).ToArray(),
            runs.Select(PersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<ManagementPage<ManagementRunItem>> GetRunsAsync(
        ManagementRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        (int page, int pageSize) = NormalizePage(query.PageNumber, query.PageSize);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<AgentRunEntity> runs = context.Set<AgentRunEntity>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.AgentId))
        {
            runs = runs.Where(item => item.AgentId == query.AgentId);
        }

        if (query.Status.HasValue)
        {
            string status = query.Status.Value.ToString();
            runs = runs.Where(item => item.Status == status);
        }

        if (query.Trigger.HasValue)
        {
            string trigger = query.Trigger.Value.ToString();
            runs = runs.Where(item => item.TriggerType == trigger);
        }

        if (query.FromUtc.HasValue)
        {
            DateTimeOffset from = query.FromUtc.Value.ToUniversalTime();
            runs = runs.Where(item => item.StartedAtUtc >= from);
        }

        if (query.ThroughUtc.HasValue)
        {
            DateTimeOffset through = query.ThroughUtc.Value.ToUniversalTime();
            runs = runs.Where(item => item.StartedAtUtc <= through);
        }

        if (query.AttentionOnly)
        {
            runs = runs.Where(item => item.Status == nameof(AgentRunStatus.Failed)
                || item.Status == nameof(AgentRunStatus.TimedOut)
                || item.Status == nameof(AgentRunStatus.Abandoned));
        }

        if (query.ActiveOnly)
        {
            runs = runs.Where(item => item.Status == nameof(AgentRunStatus.Starting)
                || item.Status == nameof(AgentRunStatus.Running));
        }

        int count = await runs.CountAsync(cancellationToken).ConfigureAwait(false);
        List<AgentRunEntity> pageEntities = await runs
            .OrderByDescending(item => item.StartedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var items = new List<ManagementRunItem>(pageEntities.Count);
        foreach (AgentRunEntity run in pageEntities)
        {
            items.Add(await MapRunAsync(context, run, cancellationToken).ConfigureAwait(false));
        }

        return new(items, page, pageSize, count);
    }

    /// <inheritdoc />
    public async ValueTask<ManagementRunDetail?> GetRunAsync(AgentRunId runId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentRunEntity? run = await context.Set<AgentRunEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        ScheduleOccurrenceEntity occurrence = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .SingleAsync(item => item.Id == run.OccurrenceId, cancellationToken)
            .ConfigureAwait(false);
        ConfigurationRevisionEntity? revision = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == run.ConfigurationRevisionId, cancellationToken)
            .ConfigureAwait(false);
        List<AgentRunEventEntity> events = await context.Set<AgentRunEventEntity>().AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderBy(item => item.Sequence)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AgentRunMetricEntity> metrics = await context.Set<AgentRunMetricEntity>().AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderBy(item => item.TimestampUtc)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<RunArtifactEntity> artifacts = await context.Set<RunArtifactEntity>().AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AuditEventEntity> audit = await context.Set<AuditEventEntity>().AsNoTracking()
            .Where(item => item.RunId == run.Id || (item.TargetType == "occurrence" && item.TargetId == occurrence.Id.ToString()))
            .OrderBy(item => item.TimestampUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(
            await MapRunAsync(context, run, cancellationToken).ConfigureAwait(false),
            PersistenceMapper.Map(occurrence),
            revision is null ? null : PersistenceMapper.Map(revision),
            events.Select(item => new AgentRunEventRecord(
                item.Id,
                AgentRunId.FromGuid(item.RunId),
                item.Sequence,
                item.TimestampUtc,
                item.Level,
                item.EventType,
                item.Message,
                item.DataJson)).ToArray(),
            metrics.Select(item => new AgentRunMetricRecord(
                item.Id,
                AgentRunId.FromGuid(item.RunId),
                item.Name,
                item.NumericValue,
                item.TextValue,
                item.Unit,
                item.TagsJson,
                item.TimestampUtc)).ToArray(),
            artifacts.Select(PersistenceMapper.Map).ToArray(),
            audit.Select(PersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<ManagementPage<AuditEventRecord>> GetAuditAsync(
        ManagementAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        (int page, int pageSize) = NormalizePage(query.PageNumber, query.PageSize);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<AuditEventEntity> events = context.Set<AuditEventEntity>().AsNoTracking();
        if (query.FromUtc.HasValue)
        {
            DateTimeOffset from = query.FromUtc.Value.ToUniversalTime();
            events = events.Where(item => item.TimestampUtc >= from);
        }

        if (query.ThroughUtc.HasValue)
        {
            DateTimeOffset through = query.ThroughUtc.Value.ToUniversalTime();
            events = events.Where(item => item.TimestampUtc <= through);
        }

        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            events = events.Where(item => item.ActorId == query.Actor);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            events = events.Where(item => item.Action.Contains(query.Action));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            events = events.Where(item => item.TargetType == query.TargetType);
        }

        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            events = events.Where(item => item.Outcome == query.Outcome);
        }

        if (!string.IsNullOrWhiteSpace(query.AgentId))
        {
            events = events.Where(item => item.TargetId == query.AgentId || item.DataJson.Contains(query.AgentId));
        }

        if (query.RunId.HasValue)
        {
            Guid run = query.RunId.Value.Value;
            events = events.Where(item => item.RunId == run);
        }

        int count = await events.CountAsync(cancellationToken).ConfigureAwait(false);
        List<AuditEventEntity> pageItems = await events
            .OrderByDescending(item => item.TimestampUtc)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(pageItems.Select(PersistenceMapper.Map).ToArray(), page, pageSize, count);
    }

    /// <inheritdoc />
    public async ValueTask<AuditEventRecord?> GetAuditEventAsync(Guid auditId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AuditEventEntity? item = await context.Set<AuditEventEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == auditId, cancellationToken)
            .ConfigureAwait(false);
        return item is null ? null : PersistenceMapper.Map(item);
    }

    /// <inheritdoc />
    public async ValueTask<ManagementDatabaseStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string migration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).LastOrDefault() ?? "none";
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string journal = await ScalarAsync(context, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
            string foreignKeys = await ScalarAsync(context, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false);
            string busyTimeout = await ScalarAsync(context, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);
            var database = new FileInfo(databasePath);
            string? root = Path.GetPathRoot(databasePath);
            long available = string.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
            return new(
                database.Name,
                migration,
                journal,
                foreignKeys == "1",
                int.TryParse(busyTimeout, out int timeout) ? timeout : 0,
                database.Exists ? database.Length : 0,
                available);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async ValueTask<ManagementAgentItem> MapAgentAsync(
        AssistantDbContext context,
        AgentDefinitionEntity agent,
        CancellationToken cancellationToken)
    {
        ConfigurationRevisionEntity? revision = await (
                from configuration in context.Set<AgentConfigurationEntity>().AsNoTracking()
                join item in context.Set<ConfigurationRevisionEntity>().AsNoTracking()
                    on configuration.CurrentRevisionId equals item.Id
                where configuration.AgentId == agent.Id
                select item)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        ScheduleOccurrenceEntity? next = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.AgentId == agent.Id
                && (item.Status == nameof(OccurrenceStatus.Planned) || item.Status == nameof(OccurrenceStatus.Ready)))
            .OrderBy(item => item.DueAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        AgentRunEntity? active = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(item => item.AgentId == agent.Id
                && (item.Status == nameof(AgentRunStatus.Starting) || item.Status == nameof(AgentRunStatus.Running)))
            .OrderByDescending(item => item.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        AgentRunEntity? last = await context.Set<AgentRunEntity>().AsNoTracking()
            .Where(item => item.AgentId == agent.Id)
            .OrderByDescending(item => item.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        string? attention = !agent.Enabled
            ? "Agent is disabled"
            : revision is null
                ? "Configuration required"
                : last is not null && last.Status is nameof(AgentRunStatus.Failed) or nameof(AgentRunStatus.TimedOut) or nameof(AgentRunStatus.Abandoned)
                    ? last.ErrorType ?? $"Last run {last.Status.ToLowerInvariant()}"
                    : null;
        return new(
            PersistenceMapper.Map(agent),
            revision?.RevisionNumber,
            revision?.ConfigurationHash,
            next?.DueAtUtc,
            next is null ? null : Enum.Parse<OccurrenceStatus>(next.Status),
            active is null ? null : AgentRunId.FromGuid(active.Id),
            active is null ? null : Enum.Parse<AgentRunStatus>(active.Status),
            last is null ? null : AgentRunId.FromGuid(last.Id),
            last is null ? null : Enum.Parse<AgentRunStatus>(last.Status),
            last?.StartedAtUtc,
            attention);
    }

    private static async ValueTask<AgentConfigurationRecord?> GetConfigurationAsync(
        AssistantDbContext context,
        string agentId,
        CancellationToken cancellationToken)
    {
        AgentConfigurationEntity? configuration = await context.Set<AgentConfigurationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.AgentId == agentId, cancellationToken)
            .ConfigureAwait(false);
        if (configuration is null)
        {
            return null;
        }

        ConfigurationRevisionEntity revision = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .SingleAsync(item => item.Id == configuration.CurrentRevisionId, cancellationToken)
            .ConfigureAwait(false);
        return new(
            configuration.Id,
            AgentId.Parse(configuration.AgentId),
            configuration.CurrentRevisionId,
            configuration.SchemaVersion,
            PersistenceMapper.Map(revision),
            configuration.UpdatedAtUtc,
            configuration.ConcurrencyToken);
    }

    private static async ValueTask<ManagementScheduleItem> MapScheduleAsync(
        AssistantDbContext context,
        AgentScheduleEntity schedule,
        CancellationToken cancellationToken)
    {
        string agentName = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .Where(item => item.Id == schedule.AgentId)
            .Select(item => item.DisplayName)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        ScheduleOccurrenceEntity? next = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.ScheduleId == schedule.Id
                && (item.Status == nameof(OccurrenceStatus.Planned) || item.Status == nameof(OccurrenceStatus.Ready)))
            .OrderBy(item => item.DueAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        AgentRunEntity? lastRun = await (
                from run in context.Set<AgentRunEntity>().AsNoTracking()
                join occurrence in context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                    on run.OccurrenceId equals occurrence.Id
                where occurrence.ScheduleId == schedule.Id
                orderby run.StartedAtUtc descending
                select run)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(
            PersistenceMapper.Map(schedule),
            agentName,
            next?.DueAtUtc,
            next is null ? null : Enum.Parse<OccurrenceStatus>(next.Status),
            lastRun is null ? null : Enum.Parse<AgentRunStatus>(lastRun.Status),
            lastRun?.StartedAtUtc);
    }

    private static async ValueTask<ManagementRunItem> MapRunAsync(
        AssistantDbContext context,
        AgentRunEntity run,
        CancellationToken cancellationToken)
    {
        ScheduleOccurrenceEntity occurrence = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .SingleAsync(item => item.Id == run.OccurrenceId, cancellationToken)
            .ConfigureAwait(false);
        string agentName = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .Where(item => item.Id == run.AgentId)
            .Select(item => item.DisplayName)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(
            PersistenceMapper.Map(run),
            agentName,
            occurrence.CommandName,
            occurrence.DueAtUtc,
            occurrence.AttemptNumber,
            occurrence.ScheduleId,
            Enum.Parse<OccurrenceStatus>(occurrence.Status));
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static async Task<string> ScalarAsync(
        AssistantDbContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
