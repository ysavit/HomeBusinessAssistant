using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>Appends bounded and redacted platform audit events.</summary>
public sealed class AuditWriter(
    IDbContextFactory<AssistantDbContext> contextFactory,
    TimeProvider timeProvider) : IAuditWriter
{
    /// <inheritdoc />
    public async ValueTask<AuditEventRecord> WriteAsync(
        WriteAuditEventRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateText(request.ActorId, 128, nameof(request.ActorId));
        ValidateText(request.Action, 128, nameof(request.Action));
        ValidateText(request.TargetType, 64, nameof(request.TargetType));
        ValidateText(request.TargetId, 128, nameof(request.TargetId));
        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("An audit correlation identifier is required.", nameof(request));
        }

        var entity = new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            TimestampUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            ActorType = request.ActorType.ToString(),
            ActorId = AuditRedactor.RedactText(request.ActorId, 128),
            Action = request.Action,
            TargetType = request.TargetType,
            TargetId = AuditRedactor.RedactText(request.TargetId, 128),
            Outcome = request.Outcome.ToString(),
            CorrelationId = request.CorrelationId,
            RunId = request.RunId?.Value,
            DataJson = AuditRedactor.Redact(request.Data),
        };

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PersistenceMapper.Map(entity);
    }

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException("An audit identifier is missing or too long.", parameterName);
        }
    }
}
