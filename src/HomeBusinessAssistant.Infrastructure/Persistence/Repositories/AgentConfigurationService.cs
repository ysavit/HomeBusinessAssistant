using System.Data;
using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed atomic configuration revision service.</summary>
public sealed class AgentConfigurationService(
    IDbContextFactory<AssistantDbContext> contextFactory,
    IAgentConfigurationValidator validator,
    TimeProvider timeProvider) : IAgentConfigurationService
{
    private const int MaximumConfigurationUtf8Bytes = 1_048_576;

    /// <inheritdoc />
    public async ValueTask<AgentConfigurationRecord?> GetCurrentAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        AgentConfigurationEntity? configuration = await context.Set<AgentConfigurationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.AgentId == agentId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (configuration is null)
        {
            return null;
        }

        ConfigurationRevisionEntity revision = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .SingleAsync(item => item.Id == configuration.CurrentRevisionId, cancellationToken)
            .ConfigureAwait(false);
        return new AgentConfigurationRecord(
            configuration.Id,
            agentId,
            configuration.CurrentRevisionId,
            configuration.SchemaVersion,
            PersistenceMapper.Map(revision),
            configuration.UpdatedAtUtc,
            configuration.ConcurrencyToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConfigurationRevisionRecord>> GetHistoryAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ConfigurationRevisionEntity> revisions = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .Where(item => item.AgentId == agentId.Value)
            .OrderByDescending(item => item.RevisionNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return revisions.Select(PersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<ConfigurationRevisionRecord?> GetRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ConfigurationRevisionEntity? revision = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == revisionId, cancellationToken)
            .ConfigureAwait(false);
        return revision is null ? null : PersistenceMapper.Map(revision);
    }

    /// <inheritdoc />
    public async ValueTask<SaveConfigurationResult> SaveAsync(
        SaveConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestText(request);
        IReadOnlyList<ConfigurationValidationError> schemaErrors = await validator.ValidateAsync(
            request.AgentId,
            request.SchemaVersion,
            request.Configuration,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ConfigurationValidationError> secretErrors = ConfigurationSecretPolicy.Validate(request.Configuration);
        if (schemaErrors.Count + secretErrors.Count > 0)
        {
            throw new ConfigurationValidationException([.. schemaErrors, .. secretErrors]);
        }

        string canonicalJson = CanonicalJson.Serialize(request.Configuration);
        if (System.Text.Encoding.UTF8.GetByteCount(canonicalJson) > MaximumConfigurationUtf8Bytes)
        {
            throw new ConfigurationValidationException(
                [new("configuration.tooLarge", "$", "Configuration exceeds the maximum supported size.")]);
        }

        string configurationHash = CanonicalJson.ComputeHash(canonicalJson);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SaveAttemptAsync(request, canonicalJson, configurationHash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 4 && IsRetryableConcurrencyFailure(exception))
            {
                // Retrying re-reads the unique hash/current pointer after the competing writer commits.
            }
        }
    }

    private async ValueTask<SaveConfigurationResult> SaveAttemptAsync(
        SaveConfigurationRequest request,
        string canonicalJson,
        string configurationHash,
        CancellationToken cancellationToken)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        bool installed = await context.Set<AgentDefinitionEntity>().AnyAsync(
            item => item.Id == request.AgentId.Value,
            cancellationToken).ConfigureAwait(false);
        if (!installed)
        {
            throw new InvalidOperationException("Configuration cannot be saved for an agent that is not installed.");
        }

        ConfigurationRevisionEntity? existingRevision = await context.Set<ConfigurationRevisionEntity>()
            .SingleOrDefaultAsync(
                item => item.AgentId == request.AgentId.Value && item.ConfigurationHash == configurationHash,
                cancellationToken).ConfigureAwait(false);
        AgentConfigurationEntity? configuration = await context.Set<AgentConfigurationEntity>()
            .SingleOrDefaultAsync(item => item.AgentId == request.AgentId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (request.ExpectedCurrentRevisionNumber.HasValue || request.ExpectedCurrentHash is not null)
        {
            ConfigurationRevisionEntity? currentRevision = configuration is null
                ? null
                : await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
                    .SingleAsync(item => item.Id == configuration.CurrentRevisionId, cancellationToken)
                    .ConfigureAwait(false);
            bool revisionMatches = request.ExpectedCurrentRevisionNumber == currentRevision?.RevisionNumber;
            bool hashMatches = request.ExpectedCurrentHash is null
                || string.Equals(request.ExpectedCurrentHash, currentRevision?.ConfigurationHash, StringComparison.Ordinal);
            if (!revisionMatches || !hashMatches)
            {
                throw new ConfigurationConcurrencyException(
                    request.ExpectedCurrentRevisionNumber,
                    currentRevision?.RevisionNumber);
            }
        }

        if (existingRevision is not null && configuration?.CurrentRevisionId == existingRevision.Id)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new SaveConfigurationResult(PersistenceMapper.Map(existingRevision), Created: false, CurrentChanged: false);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        string safeChangedBy = AuditRedactor.RedactText(request.ChangedBy, 128);
        string safeSummary = AuditRedactor.RedactText(request.ChangeSummary, 1_000);
        bool created = existingRevision is null;
        if (existingRevision is null)
        {
            long nextRevision = (await context.Set<ConfigurationRevisionEntity>()
                .Where(item => item.AgentId == request.AgentId.Value)
                .Select(item => (long?)item.RevisionNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1;
            existingRevision = new ConfigurationRevisionEntity
            {
                Id = Guid.NewGuid(),
                AgentId = request.AgentId.Value,
                RevisionNumber = nextRevision,
                SchemaVersion = request.SchemaVersion,
                CanonicalConfigurationJson = canonicalJson,
                ConfigurationHash = configurationHash,
                ChangedBy = safeChangedBy,
                ChangeSummary = safeSummary,
                CreatedAtUtc = nowUtc,
            };
            context.Add(existingRevision);
        }

        if (configuration is null)
        {
            configuration = new AgentConfigurationEntity
            {
                Id = Guid.NewGuid(),
                AgentId = request.AgentId.Value,
                CurrentRevisionId = existingRevision.Id,
                SchemaVersion = existingRevision.SchemaVersion,
                UpdatedAtUtc = nowUtc,
                ConcurrencyToken = 1,
            };
            context.Add(configuration);
        }
        else
        {
            configuration.CurrentRevisionId = existingRevision.Id;
            configuration.SchemaVersion = existingRevision.SchemaVersion;
            configuration.UpdatedAtUtc = nowUtc;
            configuration.ConcurrencyToken++;
        }

        context.Add(CreateAudit(request, existingRevision, created, nowUtc));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SaveConfigurationResult(PersistenceMapper.Map(existingRevision), created, CurrentChanged: true);
    }

    private static AuditEventEntity CreateAudit(
        SaveConfigurationRequest request,
        ConfigurationRevisionEntity revision,
        bool created,
        DateTimeOffset nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            TimestampUtc = nowUtc,
            ActorType = AuditActorType.User.ToString(),
            ActorId = AuditRedactor.RedactText(request.ChangedBy, 128),
            Action = created ? "configuration.revision-created" : "configuration.revision-promoted",
            TargetType = "agent-configuration",
            TargetId = request.AgentId.Value,
            Outcome = AuditOutcome.Succeeded.ToString(),
            CorrelationId = request.CorrelationId,
            DataJson = AuditRedactor.Redact(JsonSerializer.SerializeToElement(new
            {
                revisionId = revision.Id,
                revisionNumber = revision.RevisionNumber,
                configurationHash = revision.ConfigurationHash,
                summary = request.ChangeSummary,
            })),
        };

    private static void ValidateRequestText(SaveConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ChangedBy) || request.ChangedBy.Length > 128)
        {
            throw new ArgumentException("The configuration actor is missing or too long.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ChangeSummary) || request.ChangeSummary.Length > 4_096)
        {
            throw new ArgumentException("The configuration change summary is missing or too long.", nameof(request));
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("A configuration correlation identifier is required.", nameof(request));
        }
    }

    private static bool IsRetryableConcurrencyFailure(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException { InnerException: SqliteException sqlite } => sqlite.SqliteErrorCode is 5 or 6 or 19,
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
        _ => false,
    };
}
