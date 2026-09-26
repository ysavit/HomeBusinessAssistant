using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed occurrence storage with database-atomic transitions and claims.</summary>
public sealed class OccurrenceRepository(
    IDbContextFactory<AssistantDbContext> contextFactory,
    TimeProvider timeProvider) : IOccurrenceRepository
{
    private static readonly string[] ActiveStatuses =
    [
        OccurrenceStatus.Claimed.ToString(),
        OccurrenceStatus.Starting.ToString(),
        OccurrenceStatus.Running.ToString(),
        OccurrenceStatus.CancellationRequested.ToString(),
    ];

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord> CreateIfAbsentAsync(
        CreateOccurrenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AttemptNumber < 0
            || !Enum.IsDefined(request.TriggerType)
            || request.InitialStatus is not (OccurrenceStatus.Planned or OccurrenceStatus.Ready))
        {
            throw new ArgumentException("The occurrence request is invalid.", nameof(request));
        }

        ValidateCommandName(request.CommandName);
        using JsonDocument argumentsDocument = JsonDocument.Parse(request.ArgumentsJson, new JsonDocumentOptions { MaxDepth = 64 });
        string canonicalArguments = CanonicalJson.Serialize(argumentsDocument.RootElement);

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ScheduleOccurrenceEntity? existing = await FindExistingAsync(context, request, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return PersistenceMapper.Map(existing);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var entity = new ScheduleOccurrenceEntity
        {
            Id = request.Id.Value,
            ScheduleId = request.ScheduleId,
            AgentId = request.AgentId.Value,
            CommandName = request.CommandName,
            ArgumentsJson = canonicalArguments,
            ConfigurationRevisionId = request.ConfigurationRevisionId,
            DueAtUtc = request.DueAtUtc.ToUniversalTime(),
            TriggerType = request.TriggerType.ToString(),
            Status = request.InitialStatus.ToString(),
            RequiresWake = request.RequiresWake,
            KeepSystemAwake = request.KeepSystemAwake,
            KeepDisplayOn = request.KeepDisplayOn,
            AllowDisabledAgent = request.AllowDisabledAgent,
            AttemptNumber = request.AttemptNumber,
            ParentOccurrenceId = request.ParentOccurrenceId?.Value,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        context.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return PersistenceMapper.Map(entity);
        }
        catch (DbUpdateException) when (request.ScheduleId.HasValue || request.ParentOccurrenceId.HasValue)
        {
            context.ChangeTracker.Clear();
            ScheduleOccurrenceEntity raced;
            if (request.ScheduleId.HasValue)
            {
                Guid scheduleId = request.ScheduleId.Value;
                DateTimeOffset dueAtUtc = request.DueAtUtc.ToUniversalTime();
                raced = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                    .SingleAsync(item => item.ScheduleId == scheduleId && item.DueAtUtc == dueAtUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                Guid parentOccurrenceId = request.ParentOccurrenceId!.Value.Value;
                raced = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                    .SingleAsync(item => item.ParentOccurrenceId == parentOccurrenceId
                        && item.AttemptNumber == request.AttemptNumber, cancellationToken)
                    .ConfigureAwait(false);
            }

            return PersistenceMapper.Map(raced);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord?> GetAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ScheduleOccurrenceEntity? entity = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == occurrenceId.Value, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetForScheduleAsync(
        Guid scheduleId,
        DateTimeOffset? dueFromUtc,
        DateTimeOffset? dueThroughUtc,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        DateTimeOffset? from = dueFromUtc?.ToUniversalTime();
        DateTimeOffset? through = dueThroughUtc?.ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<ScheduleOccurrenceEntity> query = context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.ScheduleId == scheduleId);
        if (from.HasValue)
        {
            query = query.Where(item => item.DueAtUtc >= from.Value);
        }

        if (through.HasValue)
        {
            query = query.Where(item => item.DueAtUtc <= through.Value);
        }

        List<ScheduleOccurrenceEntity> entities = await query
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord?> GetLatestForScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ScheduleOccurrenceEntity? entity = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.ScheduleId == scheduleId)
            .OrderByDescending(item => item.DueAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetActiveAsync(
        AgentId agentId,
        Guid? scheduleId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<ScheduleOccurrenceEntity> query = context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.AgentId == agentId.Value && ActiveStatuses.Contains(item.Status));
        if (scheduleId.HasValue)
        {
            query = query.Where(item => item.ScheduleId == scheduleId.Value);
        }

        List<ScheduleOccurrenceEntity> entities = await query
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetPendingAsync(
        AgentId agentId,
        Guid? scheduleId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        string planned = OccurrenceStatus.Planned.ToString();
        string ready = OccurrenceStatus.Ready.ToString();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<ScheduleOccurrenceEntity> query = context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.AgentId == agentId.Value && (item.Status == planned || item.Status == ready));
        if (scheduleId.HasValue)
        {
            query = query.Where(item => item.ScheduleId == scheduleId.Value);
        }
        else
        {
            query = query.Where(item => item.ScheduleId == null);
        }

        List<ScheduleOccurrenceEntity> entities = await query
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetDueAsync(
        DateTimeOffset nowUtc,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        DateTimeOffset normalized = nowUtc.ToUniversalTime();
        string ready = OccurrenceStatus.Ready.ToString();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ScheduleOccurrenceEntity> entities = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.Status == ready && item.DueAtUtc <= normalized)
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetStaleClaimsAsync(
        DateTimeOffset nowUtc,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        DateTimeOffset normalized = nowUtc.ToUniversalTime();
        string claimed = OccurrenceStatus.Claimed.ToString();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ScheduleOccurrenceEntity> entities = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .Where(item => item.Status == claimed
                && item.RunId == null
                && item.ClaimExpiresAtUtc <= normalized)
            .OrderBy(item => item.ClaimExpiresAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord?> GetEarliestWakeAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset normalized = nowUtc.ToUniversalTime();
        string planned = OccurrenceStatus.Planned.ToString();
        string ready = OccurrenceStatus.Ready.ToString();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ScheduleOccurrenceEntity? entity = await (
            from occurrence in context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            join linkedSchedule in context.Set<AgentScheduleEntity>().AsNoTracking()
                on occurrence.ScheduleId equals linkedSchedule.Id into scheduleGroup
            from schedule in scheduleGroup.DefaultIfEmpty()
            where occurrence.RequiresWake
                && (occurrence.Status == planned || occurrence.Status == ready)
                && (occurrence.ScheduleId == null
                    || (schedule != null
                        && schedule.IsEnabled
                        && (!schedule.IsPaused || schedule.PausedUntilUtc <= normalized)))
            orderby occurrence.DueAtUtc, occurrence.Id
            select occurrence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord?> TryClaimAsync(
        OccurrenceId occurrenceId,
        string ownerId,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128 || duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1))
        {
            throw new ArgumentException("The occurrence claim is invalid.", nameof(ownerId));
        }

        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateTimeOffset expiresAt = normalizedNow.Add(duration);
        string ready = OccurrenceStatus.Ready.ToString();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<ScheduleOccurrenceEntity>()
            .Where(item => item.Id == occurrenceId.Value && item.Status == ready && item.DueAtUtc <= normalizedNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, OccurrenceStatus.Claimed.ToString())
                .SetProperty(item => item.ClaimedBy, ownerId)
                .SetProperty(item => item.ClaimedAtUtc, normalizedNow)
                .SetProperty(item => item.ClaimExpiresAtUtc, expiresAt)
                .SetProperty(item => item.UpdatedAtUtc, normalizedNow), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1 ? await GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleOccurrenceRecord?> TryTransitionAsync(
        OccurrenceTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedStatuses.Count == 0
            || request.ExpectedStatuses.Any(status => !OccurrenceStateMachine.CanTransition(status, request.TargetStatus)))
        {
            throw new InvalidOperationException("The requested occurrence transition is illegal.");
        }

        ValidateReason(request.ReasonCode, request.Message);
        string[] expected = request.ExpectedStatuses.Select(item => item.ToString()).ToArray();
        DateTimeOffset transitioned = request.TransitionedAtUtc.ToUniversalTime();
        string? safeMessage = request.Message is null ? null : AuditRedactor.RedactText(request.Message, 1_000);
        string? reasonCode = request.ReasonCode;
        bool terminal = OccurrenceStateMachine.IsTerminal(request.TargetStatus);
        bool cancellation = request.TargetStatus == OccurrenceStatus.CancellationRequested;

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<ScheduleOccurrenceEntity>()
            .Where(item => item.Id == request.OccurrenceId.Value && expected.Contains(item.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, request.TargetStatus.ToString())
                .SetProperty(item => item.CompletedAtUtc, item => terminal ? transitioned : item.CompletedAtUtc)
                .SetProperty(item => item.TerminalReasonCode, item => terminal ? reasonCode : item.TerminalReasonCode)
                .SetProperty(item => item.TerminalMessage, item => terminal ? safeMessage : item.TerminalMessage)
                .SetProperty(item => item.CancellationRequestedAtUtc, item => cancellation ? transitioned : item.CancellationRequestedAtUtc)
                .SetProperty(item => item.CancellationReason, item => cancellation ? safeMessage : item.CancellationReason)
                .SetProperty(item => item.UpdatedAtUtc, transitioned), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1 ? await GetAsync(request.OccurrenceId, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> RequestCancellationAsync(
        OccurrenceId occurrenceId,
        DateTimeOffset requestedAtUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4_096)
        {
            throw new ArgumentException("The cancellation reason is missing or too long.", nameof(reason));
        }

        OccurrenceStatus[] expected =
        [
            OccurrenceStatus.Planned,
            OccurrenceStatus.Ready,
            OccurrenceStatus.Claimed,
            OccurrenceStatus.Starting,
            OccurrenceStatus.Running,
        ];
        ScheduleOccurrenceRecord? transitioned = await TryTransitionAsync(new(
            occurrenceId,
            expected,
            OccurrenceStatus.CancellationRequested,
            requestedAtUtc,
            "operator.cancellation-requested",
            reason), cancellationToken).ConfigureAwait(false);
        return transitioned is not null;
    }

    private static async ValueTask<ScheduleOccurrenceEntity?> FindExistingAsync(
        AssistantDbContext context,
        CreateOccurrenceRequest request,
        CancellationToken cancellationToken)
    {
        ScheduleOccurrenceEntity? existing = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null && request.ScheduleId.HasValue)
        {
            Guid scheduleId = request.ScheduleId.Value;
            DateTimeOffset dueAtUtc = request.DueAtUtc.ToUniversalTime();
            existing = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.ScheduleId == scheduleId && item.DueAtUtc == dueAtUtc, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (existing is null && request.ParentOccurrenceId.HasValue)
        {
            Guid parentOccurrenceId = request.ParentOccurrenceId.Value.Value;
            existing = await context.Set<ScheduleOccurrenceEntity>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.ParentOccurrenceId == parentOccurrenceId
                    && item.AttemptNumber == request.AttemptNumber, cancellationToken)
                .ConfigureAwait(false);
        }

        return existing;
    }

    private static void ValidateCommandName(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)
            || commandName.Length > 64
            || !char.IsAsciiLetterOrDigit(commandName[0])
            || !char.IsAsciiLetterOrDigit(commandName[^1])
            || commandName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("The occurrence command name is invalid.", nameof(commandName));
        }
    }

    private static void ValidateMaximum(int maximumResults)
    {
        if (maximumResults is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }
    }

    private static void ValidateReason(string? reasonCode, string? message)
    {
        if (reasonCode?.Length > 128
            || (reasonCode is not null && (string.IsNullOrWhiteSpace(reasonCode)
                || reasonCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))))
            || message?.Length > 4_096)
        {
            throw new ArgumentException("The occurrence transition reason is invalid.");
        }
    }
}
