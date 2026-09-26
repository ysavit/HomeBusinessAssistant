using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Desktop;

/// <summary>Durable occurrence dispatch that never invokes an agent executable directly.</summary>
public sealed class OccurrenceRunnerDispatcher(
    IOccurrenceRepository occurrences,
    IRunnerProcessLauncher runner,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IOccurrenceRunnerDispatcher
{
    /// <inheritdoc />
    public async ValueTask<OccurrenceDispatchResult> DispatchAsync(
        OccurrenceId occurrenceId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId, correlationId);
        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The occurrence does not exist.");
        if (occurrence.Status == OccurrenceStatus.Planned)
        {
            return new(occurrenceId, false, true, false, null, "occurrence.queued");
        }

        if (occurrence.Status != OccurrenceStatus.Ready)
        {
            return new(occurrenceId, false, false, false, null, "occurrence.not-ready");
        }

        RunnerProcessLaunchResult launch;
        try
        {
            launch = await runner.LaunchAsync(occurrenceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            launch = new(false, null, "runner.launch-exception");
        }

        if (!launch.Started)
        {
            _ = await occurrences.TryTransitionAsync(new(
                occurrenceId,
                [OccurrenceStatus.Ready],
                OccurrenceStatus.Failed,
                timeProvider.GetUtcNow().ToUniversalTime(),
                launch.Code,
                "The central Runner process could not be started."), cancellationToken).ConfigureAwait(false);
        }

        await WriteAuditAsync(
            occurrence,
            actorId,
            correlationId,
            launch.Started ? "runner.launch-started" : "runner.launch-failed",
            launch.Started ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            new { launch.Code, launch.ProcessId },
            cancellationToken).ConfigureAwait(false);
        return new(
            occurrenceId,
            false,
            false,
            launch.Started,
            launch.ProcessId,
            launch.Code);
    }

    private async ValueTask WriteAuditAsync(
        ScheduleOccurrenceRecord occurrence,
        string actorId,
        Guid correlationId,
        string action,
        AuditOutcome outcome,
        object data,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            actorId == "host-scheduler" ? AuditActorType.System : AuditActorType.User,
            actorId,
            action,
            "occurrence",
            occurrence.Id.ToString(),
            outcome,
            correlationId,
            RunId: null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);

    private static void ValidateActor(string actorId, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 || correlationId == Guid.Empty)
        {
            throw new ArgumentException("A bounded dispatch actor and correlation identifier are required.");
        }
    }
}

/// <summary>Creates manual work through the scheduler contract before dispatching Runner.</summary>
public sealed class ManualAgentRunLauncher(
    IManualRunService manualRuns,
    IOccurrenceRunnerDispatcher dispatcher) : IManualAgentRunLauncher
{
    /// <inheritdoc />
    public async ValueTask<OccurrenceDispatchResult> CreateAndLaunchAsync(
        ManualAgentLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TriggerType is not (TriggerType.ManualUi or TriggerType.TrayMenu))
        {
            throw new ArgumentException("The desktop launch source is invalid.", nameof(request));
        }

        OccurrenceId occurrenceId = await manualRuns.CreateAsync(new(
            request.AgentId,
            request.CommandName,
            request.ArgumentsJson,
            request.TriggerType,
            request.ConcurrencyPolicy,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            request.ActorId,
            request.CorrelationId), cancellationToken).ConfigureAwait(false);
        OccurrenceDispatchResult dispatched = await dispatcher.DispatchAsync(
            occurrenceId,
            request.ActorId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);
        return dispatched with { Created = true };
    }
}
