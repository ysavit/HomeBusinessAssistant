using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>Audited enable and pause controls for durable schedules.</summary>
public interface IScheduleControlService
{
    /// <summary>Enables or disables a schedule.</summary>
    ValueTask<AgentScheduleRecord> SetEnabledAsync(
        Guid scheduleId,
        bool enabled,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Pauses a schedule indefinitely or until one UTC instant.</summary>
    ValueTask<AgentScheduleRecord> PauseAsync(
        Guid scheduleId,
        DateTimeOffset? untilUtc,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Clears either kind of pause.</summary>
    ValueTask<AgentScheduleRecord> ResumeAsync(
        Guid scheduleId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Optimistically persists schedule controls and appends redacted audit events.</summary>
public sealed class ScheduleControlService(
    IScheduleRepository schedules,
    IAgentScheduleValidator validator,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IScheduleControlService
{
    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> SetEnabledAsync(
        Guid scheduleId,
        bool enabled,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleRecord current = await GetRequiredAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        AgentScheduleRecord changed = current with
        {
            IsEnabled = enabled,
            UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
        };
        if (enabled)
        {
            AgentScheduleValidationResult validation = await validator.ValidateAsync(changed, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new AgentScheduleValidationException(validation.Errors);
            }
        }

        AgentScheduleRecord saved = await schedules.SaveAsync(changed, current.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(saved, enabled ? "schedule.enabled" : "schedule.disabled", actorId, correlationId, new { enabled }, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> PauseAsync(
        Guid scheduleId,
        DateTimeOffset? untilUtc,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset? normalizedUntil = untilUtc?.ToUniversalTime();
        if (normalizedUntil.HasValue && normalizedUntil <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(untilUtc), "A timed pause must end in the future.");
        }

        AgentScheduleRecord current = await GetRequiredAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        AgentScheduleRecord saved = await schedules.SaveAsync(current with
        {
            IsPaused = true,
            PausedUntilUtc = normalizedUntil,
            UpdatedAtUtc = nowUtc,
        }, current.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(saved, "schedule.paused", actorId, correlationId, new
        {
            indefinitely = !normalizedUntil.HasValue,
            pausedUntilUtc = normalizedUntil,
        }, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> ResumeAsync(
        Guid scheduleId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleRecord current = await GetRequiredAsync(scheduleId, cancellationToken).ConfigureAwait(false);
        AgentScheduleRecord saved = await schedules.SaveAsync(current with
        {
            IsPaused = false,
            PausedUntilUtc = null,
            UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
        }, current.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(saved, "schedule.resumed", actorId, correlationId, new { resumed = true }, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    private async ValueTask<AgentScheduleRecord> GetRequiredAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        await schedules.GetAsync(scheduleId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The schedule does not exist.");

    private async ValueTask WriteAuditAsync(
        AgentScheduleRecord schedule,
        string action,
        string actorId,
        Guid correlationId,
        object data,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.User,
            actorId,
            action,
            "schedule",
            schedule.Id.ToString("D"),
            AuditOutcome.Succeeded,
            correlationId,
            RunId: null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);
}

/// <summary>Thrown when a complete schedule fails application validation.</summary>
public sealed class AgentScheduleValidationException : Exception
{
    /// <summary>Initializes an exception with safe path-aware validation errors.</summary>
    public AgentScheduleValidationException(IReadOnlyList<ScheduleValidationError> errors)
        : base("The agent schedule is invalid.") => Errors = errors;

    /// <summary>Gets the safe validation errors.</summary>
    public IReadOnlyList<ScheduleValidationError> Errors { get; }
}
