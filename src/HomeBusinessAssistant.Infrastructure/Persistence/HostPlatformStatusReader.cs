using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>EF-backed bounded read model for the tray and foundational local dashboard.</summary>
public sealed class HostPlatformStatusReader(AssistantDbContextFactory contextFactory) : IHostPlatformStatusReader
{
    private static readonly string[] PendingOccurrenceStatuses =
    [
        nameof(OccurrenceStatus.Planned),
        nameof(OccurrenceStatus.Ready),
    ];

    private static readonly string[] ActiveRunStatuses =
    [
        nameof(AgentRunStatus.Starting),
        nameof(AgentRunStatus.Running),
    ];

    private static readonly string[] FailureRunStatuses =
    [
        nameof(AgentRunStatus.Failed),
        nameof(AgentRunStatus.TimedOut),
        nameof(AgentRunStatus.Abandoned),
    ];

    /// <inheritdoc />
    public async ValueTask<HostPlatformStatus> ReadAsync(
        int maximumItems,
        CancellationToken cancellationToken = default)
    {
        if (maximumItems is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> configuredAgents = (await context.Set<AgentConfigurationEntity>()
            .AsNoTracking()
            .Select(item => item.AgentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        List<AgentDefinitionEntity> agentEntities = await context.Set<AgentDefinitionEntity>()
            .AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Take(maximumItems)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HostAgentStatus[] agents = agentEntities.Select(item => new HostAgentStatus(
            AgentId.Parse(item.Id),
            item.DisplayName,
            item.Description,
            item.Enabled,
            item.SupportsManualRun,
            configuredAgents.Contains(item.Id))).ToArray();
        Dictionary<string, string> agentNames = agentEntities.ToDictionary(
            item => item.Id,
            item => item.DisplayName,
            StringComparer.Ordinal);

        List<OccurrenceProjection> nextEntities = await (
            from occurrence in context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            join agent in context.Set<AgentDefinitionEntity>().AsNoTracking()
                on occurrence.AgentId equals agent.Id
            where PendingOccurrenceStatuses.Contains(occurrence.Status)
            orderby occurrence.DueAtUtc, occurrence.Id
            select new OccurrenceProjection(
                occurrence.Id,
                occurrence.AgentId,
                agent.DisplayName,
                occurrence.CommandName,
                occurrence.DueAtUtc,
                occurrence.Status,
                occurrence.RequiresWake))
            .Take(maximumItems)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HostOccurrenceStatus[] next = nextEntities.Select(MapOccurrence).ToArray();

        HostRunStatus[] active = await ReadRunsAsync(
            context,
            ActiveRunStatuses,
            maximumItems,
            newestFirst: false,
            requireSummary: false,
            agentNames,
            cancellationToken).ConfigureAwait(false);
        HostRunStatus[] failures = await ReadRunsAsync(
            context,
            FailureRunStatuses,
            maximumItems,
            newestFirst: true,
            requireSummary: false,
            agentNames,
            cancellationToken).ConfigureAwait(false);
        HostRunStatus? latestSummary = (await ReadRunsAsync(
            context,
            Enum.GetNames<AgentRunStatus>(),
            1,
            newestFirst: true,
            requireSummary: true,
            agentNames,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();

        return new(agents, next, active, failures, latestSummary);
    }

    private static async Task<HostRunStatus[]> ReadRunsAsync(
        AssistantDbContext context,
        IReadOnlyCollection<string> statuses,
        int maximumItems,
        bool newestFirst,
        bool requireSummary,
        Dictionary<string, string> agentNames,
        CancellationToken cancellationToken)
    {
        string[] statusValues = statuses.ToArray();
        IQueryable<AgentRunEntity> query = context.Set<AgentRunEntity>().AsNoTracking()
            .Where(run => statusValues.Contains(run.Status));
        if (requireSummary)
        {
            query = query.Where(run => run.SummaryText != null);
        }

        query = newestFirst
            ? query.OrderByDescending(item => item.CompletedAtUtc ?? item.StartedAtUtc).ThenByDescending(item => item.Id)
            : query.OrderBy(item => item.StartedAtUtc).ThenBy(item => item.Id);
        List<AgentRunEntity> rows = await query.Take(maximumItems).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(item => MapRun(
            item,
            agentNames.TryGetValue(item.AgentId, out string? name) ? name : item.AgentId)).ToArray();
    }

    private static HostOccurrenceStatus MapOccurrence(OccurrenceProjection item) => new(
        OccurrenceId.FromGuid(item.Id),
        AgentId.Parse(item.AgentId),
        item.AgentDisplayName,
        item.CommandName,
        item.DueAtUtc,
        Enum.Parse<OccurrenceStatus>(item.Status),
        item.RequiresWake);

    private static HostRunStatus MapRun(AgentRunEntity item, string agentDisplayName) => new(
        AgentRunId.FromGuid(item.Id),
        AgentId.Parse(item.AgentId),
        agentDisplayName,
        Enum.Parse<AgentRunStatus>(item.Status),
        item.StartedAtUtc,
        item.CompletedAtUtc,
        item.SummaryText,
        item.ErrorType);

    private sealed record OccurrenceProjection(
        Guid Id,
        string AgentId,
        string AgentDisplayName,
        string CommandName,
        DateTimeOffset DueAtUtc,
        string Status,
        bool RequiresWake);

}
