using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of focused installed-agent queries and manifest upserts.</summary>
public sealed class AgentDefinitionRepository(
    IDbContextFactory<AssistantDbContext> contextFactory,
    TimeProvider? timeProvider = null)
    : IAgentDefinitionRepository
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask<AgentDefinitionRecord?> GetAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentDefinitionEntity? entity = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == agentId.Value, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentDefinitionEntity> entities = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<AgentDefinitionEntity> entities = await context.Set<AgentDefinitionEntity>().AsNoTracking()
            .Where(item => item.Enabled)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinitionRecord> UpsertManifestAsync(
        AgentDefinitionRecord definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentDefinitionEntity? entity = await context.Set<AgentDefinitionEntity>()
            .SingleOrDefaultAsync(item => item.Id == definition.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        bool created = entity is null;
        bool manifestChanged = created
            || entity!.ManifestVersion != definition.ManifestVersion
            || entity.InstalledVersion != definition.InstalledVersion;

        if (entity is null)
        {
            entity = new AgentDefinitionEntity
            {
                Id = definition.Id.Value,
                Enabled = definition.Enabled,
                CreatedAtUtc = definition.CreatedAtUtc.ToUniversalTime(),
                DisplayName = string.Empty,
                Description = string.Empty,
                ManifestVersion = string.Empty,
                InstalledVersion = string.Empty,
                ExecutableRelativePath = string.Empty,
                WorkingDirectoryRelativePath = string.Empty,
                CapabilitiesJson = "[]",
                SupportedCommandsJson = "[]",
                DefaultConcurrencyPolicy = ConcurrencyPolicy.Forbid.ToString(),
            };
            context.Add(entity);
        }

        entity.DisplayName = definition.DisplayName;
        entity.Description = definition.Description;
        entity.ManifestVersion = definition.ManifestVersion;
        entity.InstalledVersion = definition.InstalledVersion;
        entity.ExecutableRelativePath = definition.ExecutableRelativePath;
        entity.WorkingDirectoryRelativePath = definition.WorkingDirectoryRelativePath;
        entity.CapabilitiesJson = definition.CapabilitiesJson;
        entity.SupportedCommandsJson = definition.SupportedCommandsJson;
        entity.DefaultConcurrencyPolicy = definition.DefaultConcurrencyPolicy.ToString();
        entity.SupportsScheduling = definition.SupportsScheduling;
        entity.SupportsManualRun = definition.SupportsManualRun;
        entity.RequiresInteractiveUserSession = definition.RequiresInteractiveUserSession;
        entity.UpdatedAtUtc = definition.UpdatedAtUtc.ToUniversalTime();
        if (manifestChanged)
        {
            context.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                TimestampUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                ActorType = AuditActorType.System.ToString(),
                ActorId = "platform",
                Action = created ? "agent.installed" : "agent.manifest-updated",
                TargetType = "agent",
                TargetId = definition.Id.Value,
                Outcome = AuditOutcome.Succeeded.ToString(),
                CorrelationId = Guid.NewGuid(),
                DataJson = AuditRedactor.Redact(JsonSerializer.SerializeToElement(new
                {
                    definition.ManifestVersion,
                    definition.InstalledVersion,
                })),
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<bool> SetEnabledAsync(
        AgentId agentId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<AgentDefinitionEntity>()
            .Where(item => item.Id == agentId.Value && item.Enabled != enabled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Enabled, enabled)
                .SetProperty(item => item.UpdatedAtUtc, timeProvider.GetUtcNow().ToUniversalTime()), cancellationToken)
            .ConfigureAwait(false);
        if (updated == 1)
        {
            context.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                TimestampUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                ActorType = AuditActorType.User.ToString(),
                ActorId = "current-user",
                Action = enabled ? "agent.enabled" : "agent.disabled",
                TargetType = "agent",
                TargetId = agentId.Value,
                Outcome = AuditOutcome.Succeeded.ToString(),
                CorrelationId = Guid.NewGuid(),
                DataJson = AuditRedactor.Redact(JsonSerializer.SerializeToElement(new { enabled })),
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
    }

    /// <inheritdoc />
    public async ValueTask<bool> SetEnabledBySystemAsync(
        AgentId agentId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<AgentDefinitionEntity>()
            .Where(item => item.Id == agentId.Value && item.Enabled != enabled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Enabled, enabled)
                .SetProperty(item => item.UpdatedAtUtc, timeProvider.GetUtcNow().ToUniversalTime()), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }
}
