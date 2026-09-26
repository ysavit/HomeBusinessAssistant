using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Wake;

/// <summary>Creates and reports a harmless, schedule-less wake diagnostic occurrence.</summary>
public sealed class WakeTestService(
    IAgentDefinitionRepository agents,
    IAgentConfigurationService configurations,
    IOccurrenceRepository occurrences,
    IAgentRunRepository runs,
    IWakeTaskReconciler reconciler,
    IWakeTaskSchedulerBridge bridge,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IWakeTestService
{
    private static readonly AgentId WakeRemoteAgentId = AgentId.Parse("wake-remote");
    private const string WakeTestCommand = "wake-test";

    /// <inheritdoc />
    public async ValueTask<WakeTestPreparationResult> PrepareAsync(
        WakeTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MinutesInFuture is < 1 or > 60
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.ActorId.Length > 128
            || request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("The wake-test request is invalid.", nameof(request));
        }

        AgentDefinitionRecord agent = await agents.GetAsync(WakeRemoteAgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wake & Remote is not installed.");
        if (!agent.Enabled || !AgentCommandPolicy.SupportsCommand(agent, WakeTestCommand))
        {
            throw new InvalidOperationException("The harmless wake-test command is not available.");
        }

        AgentConfigurationRecord? current = await configurations.GetCurrentAsync(
            WakeRemoteAgentId,
            cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            _ = await configurations.SaveAsync(new(
                WakeRemoteAgentId,
                "1.0",
                JsonSerializer.SerializeToElement(new { schemaVersion = "1.0" }),
                "wake-test",
                "Created the non-sensitive default configuration for a wake diagnostic.",
                request.CorrelationId), cancellationToken).ConfigureAwait(false);
            current = await configurations.GetCurrentAsync(WakeRemoteAgentId, cancellationToken).ConfigureAwait(false);
        }

        AgentConfigurationRecord configuration = current
            ?? throw new InvalidOperationException("Wake & Remote has no current configuration revision.");
        DateTimeOffset expected = timeProvider.GetUtcNow().ToUniversalTime().AddMinutes(request.MinutesInFuture);
        ScheduleOccurrenceRecord occurrence = await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            ScheduleId: null,
            WakeRemoteAgentId,
            WakeTestCommand,
            "{}",
            configuration.CurrentRevisionId,
            expected,
            TriggerType.WakeSchedule,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Ready,
            RequiresWake: true,
            KeepSystemAwake: true,
            KeepDisplayOn: false), cancellationToken).ConfigureAwait(false);
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.User,
            request.ActorId,
            "wake-test.prepared",
            "occurrence",
            occurrence.Id.ToString(),
            AuditOutcome.Succeeded,
            request.CorrelationId,
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                expectedWakeAtUtc = expected,
                minutesInFuture = request.MinutesInFuture,
                configurationRevisionId = configuration.CurrentRevisionId,
            })), cancellationToken).ConfigureAwait(false);
        WakeTaskReconciliationResult reconciliation = await reconciler.ReconcileAsync(
            $"wake-test-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        return new(occurrence.Id, expected, reconciliation);
    }

    /// <inheritdoc />
    public async ValueTask<WakeTestResult> GetResultAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default)
    {
        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The wake-test occurrence does not exist.");
        if (occurrence.AgentId != WakeRemoteAgentId || occurrence.CommandName != WakeTestCommand)
        {
            throw new InvalidOperationException("The occurrence is not a wake test.");
        }

        AgentRunRecord? run = await runs.GetForOccurrenceAsync(occurrenceId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? actual = run?.StartedAtUtc.ToUniversalTime();
        TimeSpan? delay = actual - occurrence.DueAtUtc.ToUniversalTime();
        WakeTaskState taskState = await bridge.GetStateAsync(cancellationToken).ConfigureAwait(false);
        string resultCode = occurrence.Status switch
        {
            OccurrenceStatus.Completed => "wake-test.completed",
            OccurrenceStatus.Failed or OccurrenceStatus.TimedOut or OccurrenceStatus.Cancelled or OccurrenceStatus.Abandoned => "wake-test.failed",
            _ when actual.HasValue => "wake-test.started",
            _ => "wake-test.pending",
        };
        return new(
            occurrence.Id,
            occurrence.DueAtUtc,
            actual,
            delay,
            occurrence.Status,
            taskState,
            resultCode);
    }
}
