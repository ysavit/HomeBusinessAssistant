using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed storage for schedule definitions without recurrence calculations.</summary>
public sealed class ScheduleRepository(
    IDbContextFactory<AssistantDbContext> contextFactory,
    TimeProvider timeProvider) : IScheduleRepository
{
    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentScheduleEntity? entity = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == scheduleId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentScheduleRecord>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentScheduleEntity> entities = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .Where(item => item.IsEnabled
                && (!item.IsPaused || item.PausedUntilUtc <= nowUtc))
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentScheduleRecord>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentScheduleEntity> entities = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord?> GetByAgentAndNameAsync(
        HomeBusinessAssistant.Domain.Agents.AgentId agentId,
        string name,
        Guid? excludingScheduleId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentScheduleEntity? entity = await context.Set<AgentScheduleEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.AgentId == agentId.Value
                && item.Name == name
                && (!excludingScheduleId.HasValue || item.Id != excludingScheduleId.Value), cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> SaveAsync(
        AgentScheduleRecord schedule,
        long? expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Validate(schedule);
        ScheduleDefinitionParseResult definitionResult = ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind);
        if (!definitionResult.IsValid)
        {
            throw new ScheduleDefinitionValidationException(definitionResult.Errors);
        }

        string canonicalDefinition = ScheduleDefinitionJson.Serialize(definitionResult.Definition!);
        (RetryPolicyDefinition? retryPolicy, IReadOnlyList<ScheduleValidationError> retryErrors) = RetryPolicyJson.Parse(schedule.RetryPolicyJson);
        if (retryPolicy is null)
        {
            throw new ScheduleDefinitionValidationException(retryErrors);
        }

        string canonicalRetryPolicy = RetryPolicyJson.Serialize(retryPolicy);
        using JsonDocument argumentsDocument = JsonDocument.Parse(schedule.ArgumentsJson, new JsonDocumentOptions { MaxDepth = 64 });
        string canonicalArguments = CanonicalJson.Serialize(argumentsDocument.RootElement);

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentScheduleEntity? entity = await context.Set<AgentScheduleEntity>()
            .SingleOrDefaultAsync(item => item.Id == schedule.Id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            if (expectedConcurrencyToken.HasValue)
            {
                throw new DbUpdateConcurrencyException("The schedule no longer exists.");
            }

            entity = new AgentScheduleEntity
            {
                Id = schedule.Id,
                AgentId = schedule.AgentId.Value,
                CreatedAtUtc = schedule.CreatedAtUtc.ToUniversalTime(),
                Name = string.Empty,
                CommandName = string.Empty,
                ArgumentsJson = "{}",
                Kind = string.Empty,
                DefinitionJson = "{}",
                TimeZoneId = string.Empty,
                MisfirePolicy = string.Empty,
                ConcurrencyPolicy = string.Empty,
                RetryPolicyJson = "{}",
                WakePolicy = string.Empty,
                ConcurrencyToken = 1,
            };
            context.Add(entity);
        }
        else
        {
            if (!expectedConcurrencyToken.HasValue || entity.ConcurrencyToken != expectedConcurrencyToken.Value)
            {
                throw new DbUpdateConcurrencyException("The schedule changed before it could be saved.");
            }

            entity.ConcurrencyToken++;
        }

        entity.AgentId = schedule.AgentId.Value;
        entity.Name = schedule.Name;
        entity.CommandName = schedule.CommandName;
        entity.ArgumentsJson = canonicalArguments;
        entity.Kind = schedule.Kind.ToString();
        entity.DefinitionJson = canonicalDefinition;
        entity.TimeZoneId = schedule.TimeZoneId;
        entity.MisfirePolicy = schedule.MisfirePolicy.ToString();
        entity.ConcurrencyPolicy = schedule.ConcurrencyPolicy.ToString();
        entity.TimeoutSeconds = checked((int)schedule.Timeout.TotalSeconds);
        entity.MisfireGracePeriodSeconds = checked((int)schedule.MisfireGracePeriod.TotalSeconds);
        entity.RetryPolicyJson = canonicalRetryPolicy;
        entity.WakePolicy = schedule.WakePolicy.ToString();
        entity.PinnedConfigurationRevisionId = schedule.PinnedConfigurationRevisionId;
        entity.AllowDisabledAgent = schedule.AllowDisabledAgent;
        entity.IsEnabled = schedule.IsEnabled;
        entity.IsPaused = schedule.IsPaused;
        entity.PausedUntilUtc = schedule.PausedUntilUtc?.ToUniversalTime();
        entity.UpdatedAtUtc = schedule.UpdatedAtUtc.ToUniversalTime();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PersistenceMapper.Map(entity);
    }

    private static void Validate(AgentScheduleRecord schedule)
    {
        if (schedule.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(schedule.Name)
            || schedule.Name.Length > 100
            || string.IsNullOrWhiteSpace(schedule.CommandName)
            || schedule.CommandName.Length > 64
            || string.IsNullOrWhiteSpace(schedule.TimeZoneId)
            || schedule.TimeZoneId.Length > 128
            || schedule.Timeout <= TimeSpan.Zero
            || schedule.Timeout > TimeSpan.FromDays(1)
            || schedule.MisfireGracePeriod < TimeSpan.Zero
            || schedule.MisfireGracePeriod > TimeSpan.FromHours(1)
            || (!schedule.IsPaused && schedule.PausedUntilUtc.HasValue))
        {
            throw new ArgumentException("The schedule definition is invalid.", nameof(schedule));
        }

        if (!Enum.IsDefined(schedule.Kind)
            || !Enum.IsDefined(schedule.MisfirePolicy)
            || !Enum.IsDefined(schedule.ConcurrencyPolicy)
            || !Enum.IsDefined(schedule.WakePolicy))
        {
            throw new ArgumentException("The schedule contains an invalid policy.", nameof(schedule));
        }
    }
}
