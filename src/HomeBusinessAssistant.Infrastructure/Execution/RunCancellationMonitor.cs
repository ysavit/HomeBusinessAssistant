using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Runner-owned heartbeat, lease, timeout, cancellation, and process-tree enforcement.</summary>
public sealed class RunCancellationMonitor(
    IAgentRunRepository runs,
    IOccurrenceRepository occurrences,
    ILeaseManager leases,
    TimeProvider timeProvider,
    RunnerSupervisionOptions options) : IRunCancellationMonitor
{
    /// <inheritdoc />
    public async Task<ProcessSupervisionResult> SuperviseAsync(
        AgentProcessHandle process,
        AgentRunId runId,
        OccurrenceId occurrenceId,
        TimeSpan timeout,
        AgentLeaseRecord? executionLease,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(logger);
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        DateTimeOffset timeoutAtUtc = timeProvider.GetUtcNow().ToUniversalTime().Add(timeout);
        DateTimeOffset nextHeartbeatAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        Task exitTask = process.Process.WaitForExitAsync(CancellationToken.None);
        AgentLeaseRecord? currentLease = executionLease;
        while (!exitTask.IsCompleted)
        {
            DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            if (cancellationToken.IsCancellationRequested)
            {
                return await EnforceStopAsync(
                    process,
                    exitTask,
                    timedOut: false,
                    cancellationRequested: true,
                    leaseLost: false,
                    "runner-cancellation",
                    logger).ConfigureAwait(false);
            }

            ScheduleOccurrenceRecord? occurrence = await occurrences.GetAsync(occurrenceId, CancellationToken.None).ConfigureAwait(false);
            if (occurrence?.Status == OccurrenceStatus.CancellationRequested)
            {
                return await EnforceStopAsync(
                    process,
                    exitTask,
                    timedOut: false,
                    cancellationRequested: true,
                    leaseLost: false,
                    "persisted-cancellation",
                    logger).ConfigureAwait(false);
            }

            if (nowUtc >= timeoutAtUtc)
            {
                return await EnforceStopAsync(
                    process,
                    exitTask,
                    timedOut: true,
                    cancellationRequested: false,
                    leaseLost: false,
                    "timeout",
                    logger).ConfigureAwait(false);
            }

            if (nowUtc >= nextHeartbeatAtUtc)
            {
                await UpdateHeartbeatWithRetryAsync(runId, nowUtc, logger).ConfigureAwait(false);
                if (currentLease is not null)
                {
                    currentLease = await RenewLeaseWithRetryAsync(currentLease, nowUtc, logger).ConfigureAwait(false);
                    if (currentLease is null)
                    {
                        return await EnforceStopAsync(
                            process,
                            exitTask,
                            timedOut: false,
                            cancellationRequested: false,
                            leaseLost: true,
                            "execution-lease-lost",
                            logger).ConfigureAwait(false);
                    }
                }

                nextHeartbeatAtUtc = nowUtc.Add(options.HeartbeatInterval);
            }

            TimeSpan untilPoll = new[]
            {
                options.CancellationPollInterval,
                timeoutAtUtc - nowUtc,
                nextHeartbeatAtUtc - nowUtc,
            }.Where(duration => duration > TimeSpan.Zero).Min();
            Task delay = Task.Delay(untilPoll, timeProvider, CancellationToken.None);
            _ = await Task.WhenAny(exitTask, delay).ConfigureAwait(false);
        }

        await exitTask.ConfigureAwait(false);
        return new(
            process.Process.ExitCode,
            TimedOut: false,
            CancellationRequested: false,
            LeaseLost: false,
            ProcessTreeKilled: false,
            FailureReason: null);
    }

    private async Task<ProcessSupervisionResult> EnforceStopAsync(
        AgentProcessHandle process,
        Task exitTask,
        bool timedOut,
        bool cancellationRequested,
        bool leaseLost,
        string reason,
        ILogger logger)
    {
        logger.Warning("Stopping agent process because {StopReason}", reason);
        if (options.TerminationGracePeriod > TimeSpan.Zero)
        {
            Task grace = Task.Delay(options.TerminationGracePeriod, timeProvider, CancellationToken.None);
            _ = await Task.WhenAny(exitTask, grace).ConfigureAwait(false);
        }

        var killed = false;
        string? failure = null;
        if (!exitTask.IsCompleted)
        {
            try
            {
                process.Process.Kill(entireProcessTree: true);
                killed = true;
                logger.Warning("Agent process tree termination was requested");
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                failure = $"process-tree-kill:{exception.GetType().Name}";
                logger.Error("Agent process tree termination failed with {FailureType}", exception.GetType().Name);
            }
        }

        Task forcedWait = Task.Delay(options.ForcedExitWait, timeProvider, CancellationToken.None);
        _ = await Task.WhenAny(exitTask, forcedWait).ConfigureAwait(false);
        int? exitCode = null;
        if (exitTask.IsCompleted)
        {
            await exitTask.ConfigureAwait(false);
            exitCode = process.Process.ExitCode;
        }
        else
        {
            failure ??= "process-did-not-exit";
        }

        return new(exitCode, timedOut, cancellationRequested, leaseLost, killed, failure);
    }

    private async ValueTask UpdateHeartbeatWithRetryAsync(
        AgentRunId runId,
        DateTimeOffset nowUtc,
        ILogger logger)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                AgentRunRecord? updated = await runs.UpdateHeartbeatAsync(runId, nowUtc, CancellationToken.None).ConfigureAwait(false);
                if (updated is not null)
                {
                    return;
                }

                logger.Warning("Runner heartbeat did not update an active run");
                return;
            }
            catch (Exception exception) when (IsTransientPersistenceFailure(exception))
            {
                logger.Warning("Runner heartbeat persistence attempt {Attempt} failed with {FailureType}", attempt, exception.GetType().Name);
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), timeProvider, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask<AgentLeaseRecord?> RenewLeaseWithRetryAsync(
        AgentLeaseRecord lease,
        DateTimeOffset nowUtc,
        ILogger logger)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await leases.TryRenewAsync(
                    lease,
                    nowUtc,
                    options.ExecutionLeaseDuration,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransientPersistenceFailure(exception))
            {
                logger.Warning("Execution lease renewal attempt {Attempt} failed with {FailureType}", attempt, exception.GetType().Name);
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), timeProvider, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        return null;
    }

    private static bool IsTransientPersistenceFailure(Exception exception) => exception is
        SqliteException
        or DbUpdateException
        or TimeoutException
        or IOException;
}
