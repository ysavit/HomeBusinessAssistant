using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Host.Health;
using HomeBusinessAssistant.Infrastructure.Execution;
namespace HomeBusinessAssistant.Host.Orchestration;

/// <summary>One non-overlapping background-loop iteration.</summary>
public interface IHostLoopIteration
{
    /// <summary>Executes one bounded unit of work.</summary>
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sequential resilient periodic loop with injected time and observable health.</summary>
public sealed class HostPeriodicBackgroundService(
    string name,
    TimeSpan interval,
    IHostLoopIteration iteration,
    HostHealthState healthState,
    TimeProvider timeProvider,
    ILogger<HostPeriodicBackgroundService> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, string, Exception?> LogKnownFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(7001, "HostLoopFailure"),
            "Host loop {LoopName} failed with {FailureCode}.");

    private static readonly Action<ILogger, string, string, Exception?> LogUnexpectedFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(7002, "HostLoopUnexpectedFailure"),
            "Host loop {LoopName} failed with exception type {ExceptionType}.");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(name)
            || interval < TimeSpan.FromMilliseconds(10)
            || interval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("The Host loop configuration is invalid.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            healthState.RecordLoopStarted(name);
            try
            {
                await iteration.ExecuteAsync(stoppingToken).ConfigureAwait(false);
                healthState.RecordLoopSucceeded(name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HostLoopIterationException exception)
            {
                healthState.RecordLoopFailed(name, exception.Code);
                LogKnownFailure(logger, name, exception.Code, null);
            }
            catch (Exception exception)
            {
                const string code = "host.loop-failed";
                healthState.RecordLoopFailed(name, code);
                LogUnexpectedFailure(logger, name, exception.GetType().Name, null);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

/// <summary>A safe machine-readable loop failure.</summary>
public sealed class HostLoopIterationException(string code) : Exception("A Host loop iteration failed.")
{
    /// <summary>Gets the bounded failure code.</summary>
    public string Code { get; } = code;
}

/// <summary>Plans durable work and dispatches due non-wake occurrences to Runner.</summary>
public sealed class ScheduleReconciliationIteration(
    IScheduleReconciler reconciler,
    IOccurrenceRepository occurrences,
    IOccurrenceRunnerDispatcher dispatcher,
    IGlobalScheduleControlService globalPause,
    IWakeTaskReconciler wakeTaskReconciler,
    TimeProvider timeProvider) : IHostLoopIteration
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ScheduleReconciliationSummary summary = await reconciler.ReconcileAsync(
            $"host-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        bool globallyPaused = (await globalPause.GetStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused;
        if (summary.Errors.Count == 0 && !globallyPaused)
        {
            IReadOnlyList<ScheduleOccurrenceRecord> due = await occurrences.GetDueAsync(
                timeProvider.GetUtcNow().ToUniversalTime(),
                maximumResults: 20,
                cancellationToken).ConfigureAwait(false);
            foreach (ScheduleOccurrenceRecord occurrence in due.Where(item => !item.RequiresWake))
            {
                _ = await dispatcher.DispatchAsync(
                    occurrence.Id,
                    "host-scheduler",
                    occurrence.Id.Value,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        WakeTaskReconciliationResult wake = await wakeTaskReconciler.ReconcileAsync(
            $"host-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        if (summary.Errors.Count > 0)
        {
            throw new HostLoopIterationException("schedule.partial-failure");
        }

        if (wake.Error is not null)
        {
            throw new HostLoopIterationException(wake.Error.Code);
        }
    }
}

/// <summary>Reconciles the one managed Windows wake task.</summary>
public sealed class WakeTaskReconciliationIteration(IWakeTaskReconciler reconciler) : IHostLoopIteration
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        WakeTaskReconciliationResult result = await reconciler.ReconcileAsync(
            $"host-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            throw new HostLoopIterationException(result.Error.Code);
        }
    }
}

/// <summary>Runs conservative stale work recovery.</summary>
public sealed class StaleRunRecoveryIteration(IStaleRunRecoveryService recovery) : IHostLoopIteration
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        StaleRunRecoveryResult result = await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
        if (result.Errors > 0)
        {
            throw new HostLoopIterationException("recovery.partial-failure");
        }
    }
}

/// <summary>A bounded attention notification produced by Host orchestration.</summary>
public sealed record HostNotification(
    string Code,
    string Title,
    string Message,
    bool IsHighPriority,
    Guid? NotificationId = null,
    string? LocalPath = null);

/// <summary>In-process fan-out from background work to the interactive tray shell.</summary>
public sealed class HostNotificationHub
{
    /// <summary>Raised for a bounded local notification.</summary>
    public event EventHandler<HostNotification>? Published;

    /// <summary>Publishes an allow-listed local notification without letting a subscriber fail the producer.</summary>
    public void Publish(HostNotification notification) => _ = TryPublish(notification);

    /// <summary>Publishes and reports whether at least one interactive subscriber accepted the call.</summary>
    public bool TryPublish(HostNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Code.Length > 128 || notification.Title.Length > 128 || notification.Message.Length > 512)
        {
            throw new ArgumentException("The Host notification is outside supported bounds.", nameof(notification));
        }

        bool accepted = false;
        foreach (EventHandler<HostNotification> subscriber in Published?.GetInvocationList().Cast<EventHandler<HostNotification>>() ?? [])
        {
            try
            {
                subscriber(this, notification);
                accepted = true;
            }
            catch (Exception)
            {
                // One tray subscriber cannot break background orchestration.
            }
        }

        return accepted;
    }
}

/// <summary>Derives Stage 07 local notifications from newly observed platform run failures.</summary>
public sealed class NotificationPollingIteration : IHostLoopIteration
{
    private readonly IHostPlatformStatusReader? statusReader;
    private readonly IOperationalService? operations;
    private readonly HostNotificationHub notifications;
    private readonly TimeProvider timeProvider;
    private readonly HashSet<Guid> seenRunIds = [];

    /// <summary>Creates the legacy read-model adapter used by narrow tests.</summary>
    public NotificationPollingIteration(IHostPlatformStatusReader statusReader, HostNotificationHub notifications)
    {
        this.statusReader = statusReader;
        this.notifications = notifications;
        timeProvider = TimeProvider.System;
    }

    /// <summary>Creates the durable Stage 14 notification adapter.</summary>
    public NotificationPollingIteration(IOperationalService operations, HostNotificationHub notifications, TimeProvider? timeProvider = null)
    {
        this.operations = operations;
        this.notifications = notifications;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (operations is not null)
        {
            _ = await operations.DetectAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset localNow = timeProvider.GetLocalNow();
            if (localNow.TimeOfDay >= operations.Policy.DailySummaryTimeLocal.ToTimeSpan())
            {
                DateOnly date = DateOnly.FromDateTime(localNow.Date);
                IReadOnlyList<DailySummary> recent = await operations.GetDailySummariesAsync(5, cancellationToken).ConfigureAwait(false);
                if (!recent.Any(item => item.LocalDate == date && item.TimeZoneId == TimeZoneInfo.Local.Id))
                {
                    _ = await operations.GenerateDailySummaryAsync(date, TimeZoneInfo.Local.Id, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (LocalNotification item in await operations.GetDeliverableNotificationsAsync(1, cancellationToken).ConfigureAwait(false))
            {
                bool accepted = notifications.TryPublish(new(
                    "durable-attention", item.Title, item.Message,
                    item.Severity is AttentionSeverity.Error or AttentionSeverity.Critical,
                    item.Id, item.LocalPath));
                if (accepted)
                {
                    await operations.MarkNotificationDeliveredAsync(item.Id, cancellationToken).ConfigureAwait(false);
                }
            }

            return;
        }

        System.Diagnostics.Debug.Assert(statusReader is not null);
        HostPlatformStatus status = await statusReader.ReadAsync(20, cancellationToken).ConfigureAwait(false);
        foreach (HostRunStatus failure in status.RecentFailures.OrderBy(item => item.CompletedAtUtc ?? item.StartedAtUtc))
        {
            if (!seenRunIds.Add(failure.RunId.Value))
            {
                continue;
            }

            notifications.Publish(new(
                "run.attention-required",
                "Agent run needs attention",
                $"{failure.AgentDisplayName} ended with status {failure.Status}.",
                IsHighPriority: true));
        }

        if (seenRunIds.Count > 200)
        {
            Guid[] retain = status.RecentFailures.Select(item => item.RunId.Value).ToArray();
            seenRunIds.Clear();
            seenRunIds.UnionWith(retain);
        }
    }
}
