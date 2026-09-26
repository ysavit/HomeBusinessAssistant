using System.Reflection;
using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using Microsoft.Data.Sqlite;
using Serilog;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>The single durable occurrence-to-child-process execution path.</summary>
public sealed class OccurrenceExecutor(
    IAgentDefinitionRepository agents,
    IAgentConfigurationService configurations,
    IScheduleRepository schedules,
    IOccurrenceRepository occurrences,
    IAgentRunRepository runs,
    ILeaseManager leases,
    IAuditWriter auditWriter,
    IFixedDelayCompletionService fixedDelay,
    IExecutableIntegrityService integrity,
    IRunTemporaryFileManager temporaryFiles,
    IAgentProcessLauncher launcher,
    IAgentProtocolReader protocolReader,
    IRunCancellationMonitor cancellationMonitor,
    IRunFinalizer finalizer,
    IExecutionLifetimeHook lifetimeHook,
    RunLogFactory logFactory,
    TimeProvider timeProvider,
    RunnerSupervisionOptions options,
    ISecretStore? secretStore = null) : IOccurrenceExecutor
{
    private readonly string runnerVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? typeof(OccurrenceExecutor).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    public async ValueTask<OccurrenceExecutionResult> ExecuteAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        ScheduleOccurrenceRecord? existing = await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotRunnable(occurrenceId, "occurrence.not-found", "The occurrence does not exist.");
        }

        string runnerInstanceId = $"runner-{Environment.ProcessId}-{Guid.NewGuid():N}";
        ScheduleOccurrenceRecord? claimed = await TryClaimWithRetryAsync(
            occurrenceId,
            runnerInstanceId,
            nowUtc,
            cancellationToken).ConfigureAwait(false);
        if (claimed is null)
        {
            ScheduleOccurrenceRecord current = await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
                ?? existing;
            return NotRunnable(
                occurrenceId,
                "occurrence.not-runnable",
                $"The occurrence is {current.Status} and was not claimed.");
        }

        AgentLeaseRecord? executionLease = null;
        AgentRunRecord? run = null;
        var runnerSequences = new RunnerEventSequence();
        try
        {
            AgentScheduleRecord? schedule = claimed.ScheduleId.HasValue
                ? await schedules.GetAsync(claimed.ScheduleId.Value, cancellationToken).ConfigureAwait(false)
                : null;
            ValidateScheduleRunnable(claimed, schedule, nowUtc);
            AgentDefinitionRecord agent = await agents.GetAsync(claimed.AgentId, cancellationToken).ConfigureAwait(false)
                ?? throw new RunnerSetupException("runner.agent-not-installed", "The occurrence agent is not installed.");
            bool allowsDisabledInteractiveSetup = claimed.AllowDisabledAgent
                && !claimed.ScheduleId.HasValue
                && claimed.TriggerType == TriggerType.ManualUi
                && string.Equals(claimed.CommandName, "authenticate", StringComparison.Ordinal);
            if (!agent.Enabled && !allowsDisabledInteractiveSetup && schedule?.AllowDisabledAgent != true)
            {
                throw new RunnerSetupException("runner.agent-disabled", "The occurrence agent is disabled.");
            }

            ConcurrencyPolicy concurrency = schedule?.ConcurrencyPolicy ?? agent.DefaultConcurrencyPolicy;
            if (concurrency != ConcurrencyPolicy.AllowParallel)
            {
                executionLease = await TryAcquireExecutionLeaseWithRetryAsync(
                    $"execution:agent:{agent.Id.Value}",
                    runnerInstanceId,
                    nowUtc,
                    options.ExecutionLeaseDuration,
                    cancellationToken).ConfigureAwait(false);
                if (executionLease is null)
                {
                    throw new RunnerSetupException("runner.execution-lease-unavailable", "Another run currently owns the agent execution lease.");
                }
            }

            ConfigurationRevisionRecord revision = await configurations.GetRevisionAsync(
                claimed.ConfigurationRevisionId,
                cancellationToken).ConfigureAwait(false)
                ?? throw new RunnerSetupException("runner.configuration-missing", "The occurrence configuration revision does not exist.");
            if (revision.AgentId != claimed.AgentId)
            {
                throw new RunnerSetupException("runner.configuration-agent-mismatch", "The occurrence configuration belongs to another agent.");
            }

            ValidatedAgentExecutable executable = await integrity.ValidateAsync(
                agent,
                claimed.CommandName,
                cancellationToken).ConfigureAwait(false);
            AgentRunId runId = AgentRunId.New();
            run = await runs.CreateAsync(new(
                runId,
                claimed.Id,
                claimed.AgentId,
                revision.Id,
                revision.ConfigurationHash,
                claimed.TriggerType,
                AgentRunStatus.Starting,
                runnerVersion,
                executable.Manifest.Version.Value,
                executable.Manifest.ManifestVersion.ToString(),
                executable.ExecutableSha256,
                Environment.MachineName,
                ProcessId: null,
                ProcessStartedAtUtc: null,
                StartedAtUtc: nowUtc,
                LastHeartbeatAtUtc: nowUtc,
                CompletedAtUtc: null,
                DurationMilliseconds: null,
                ExitCode: null,
                SummaryText: null,
                SummaryJson: null,
                ErrorType: null,
                ErrorMessage: null,
                CreatedAtUtc: nowUtc,
                UpdatedAtUtc: nowUtc,
                ConcurrencyToken: 1), cancellationToken).ConfigureAwait(false);
            await runs.AppendEventAsync(new(
                Guid.NewGuid(),
                run.Id,
                runnerSequences.Next(),
                nowUtc,
                "Information",
                "runner.starting",
                "Runner initialized the durable run.",
                JsonSerializer.Serialize(new
                {
                    runnerInstanceId,
                    executableSha256 = executable.ExecutableSha256,
                    configurationRevisionId = revision.Id,
                })), cancellationToken).ConfigureAwait(false);

            IReadOnlyDictionary<string, string> resolvedSecrets = await ResolveSecretsAsync(
                revision.CanonicalConfigurationJson,
                secretStore,
                cancellationToken).ConfigureAwait(false);
            string configurationFile = await temporaryFiles.WriteExecutionInputAsync(
                run.Id,
                revision.CanonicalConfigurationJson,
                claimed.ArgumentsJson,
                resolvedSecrets,
                cancellationToken).ConfigureAwait(false);
            string artifactDirectory = temporaryFiles.CreateArtifactDirectory(run.Id);
            string agentDataDirectory = temporaryFiles.GetAgentDataDirectory(agent.Id);
            await using IAsyncDisposable lifetime = await lifetimeHook.AcquireAsync(new(
                run.Id,
                claimed.Id,
                claimed.AgentId,
                claimed.KeepSystemAwake,
                claimed.KeepDisplayOn,
                $"Home Business Assistant run {run.Id}"), cancellationToken).ConfigureAwait(false);
            using RunLog runLog = logFactory.Create(run.Id, claimed.Id, claimed.AgentId);
            ILogger logger = runLog.Logger;
            logger.Information("Runner is launching the validated agent executable");

            RunFinalizationResult finalized;
            await using (AgentProcessHandle? process = await TryLaunchAsync(
                executable,
                run,
                claimed,
                configurationFile,
                agentDataDirectory,
                artifactDirectory,
                logger,
                cancellationToken).ConfigureAwait(false))
            {
                if (process is null)
                {
                    finalized = await finalizer.FinalizeAsync(new(
                        run.Id,
                        FailedSupervision("runner.process-start-failed"),
                        EmptyProtocol(),
                        "runner.process-start-failed",
                        "The agent process could not be started."), CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        run = await runs.MarkRunningAsync(
                            run.Id,
                            process.Process.Id,
                            process.ProcessStartedAtUtc,
                            timeProvider.GetUtcNow(),
                            run.ConcurrencyToken,
                            CancellationToken.None).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("The launched run could not transition to Running.");
                        await runs.AppendEventAsync(new(
                            Guid.NewGuid(),
                            run.Id,
                            runnerSequences.Next(),
                            timeProvider.GetUtcNow(),
                            "Information",
                            "runner.running",
                            "The agent process is running.",
                            JsonSerializer.Serialize(new { processId = process.Process.Id })), CancellationToken.None).ConfigureAwait(false);

                        Task<AgentProtocolIngestionResult> stdoutTask = protocolReader.ReadStandardOutputAsync(
                            process.StandardOutput,
                            run.Id,
                            run.AgentId,
                            artifactDirectory,
                            runnerSequences,
                            logger,
                            CancellationToken.None);
                        Task<StderrIngestionResult> stderrTask = protocolReader.ReadStandardErrorAsync(
                            process.StandardError,
                            run.Id,
                            runnerSequences,
                            logger,
                            CancellationToken.None);
                        ProcessSupervisionResult supervision = await cancellationMonitor.SuperviseAsync(
                            process,
                            run.Id,
                            run.OccurrenceId,
                            schedule is null
                                ? TimeSpan.FromSeconds(executable.Manifest.DefaultTimeoutSeconds)
                                : schedule.Timeout,
                            executionLease,
                            logger,
                            cancellationToken).ConfigureAwait(false);
                        AgentProtocolIngestionResult protocol;
                        string? runnerFailureType = null;
                        string? runnerFailureMessage = null;
                        try
                        {
                            protocol = await stdoutTask.ConfigureAwait(false);
                            _ = await stderrTask.ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            logger.Error("Redirected agent output ingestion failed with {FailureType}", exception.GetType().Name);
                            protocol = EmptyProtocol(fatal: true, "runner.output-ingestion-failed");
                            runnerFailureType = "runner.output-ingestion-failed";
                            runnerFailureMessage = "The Runner could not persist redirected agent output.";
                        }

                        finalized = await finalizer.FinalizeAsync(new(
                            run.Id,
                            supervision,
                            protocol,
                            runnerFailureType,
                            runnerFailureMessage), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        await KillStartedProcessAsync(process, logger).ConfigureAwait(false);
                        throw new InvalidOperationException("Agent process supervision failed after launch.", exception);
                    }
                }
            }

            return ToExecutionResult(finalized.Run);
        }
        catch (RunnerSetupException exception) when (run is null)
        {
            await FailClaimedOccurrenceAsync(claimed, exception.Code, exception.Message, cancellationToken).ConfigureAwait(false);
            return new(
                RunnerExitCode.InvalidExecutionEnvironment,
                exception.Code,
                claimed.Id,
                RunId: null,
                RunStatus: null,
                AgentExitCode: null,
                exception.Message);
        }
        catch (Exception exception) when (run is null && exception is not OperationCanceledException)
        {
            await FailClaimedOccurrenceAsync(
                claimed,
                "runner.setup-failed",
                "The Runner could not initialize the occurrence.",
                CancellationToken.None).ConfigureAwait(false);
            return new(
                RunnerExitCode.InvalidExecutionEnvironment,
                "runner.setup-failed",
                claimed.Id,
                RunId: null,
                RunStatus: null,
                AgentExitCode: null,
                $"Runner setup failed with {exception.GetType().Name}.");
        }
        catch (Exception exception) when (run is not null && exception is not OperationCanceledException)
        {
            RunFinalizationResult terminal = await finalizer.FinalizeAsync(new(
                run.Id,
                FailedSupervision("runner.supervision-failed"),
                EmptyProtocol(fatal: true, "runner.supervision-failed"),
                "runner.supervision-failed",
                $"Runner supervision failed with {exception.GetType().Name}."), CancellationToken.None).ConfigureAwait(false);
            return ToExecutionResult(terminal.Run);
        }
        finally
        {
            if (executionLease is not null)
            {
                _ = await leases.ReleaseAsync(executionLease, CancellationToken.None).ConfigureAwait(false);
            }

            if (run is not null)
            {
                temporaryFiles.CleanupRun(run.Id);
            }
        }
    }

    private static async ValueTask<IReadOnlyDictionary<string, string>> ResolveSecretsAsync(
        string canonicalConfigurationJson,
        ISecretStore? secretStore,
        CancellationToken cancellationToken)
    {
        if (secretStore is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(canonicalConfigurationJson);
        string[] references = EnumerateStrings(document.RootElement)
            .Where(value => value.StartsWith("secret://", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(33)
            .ToArray();
        if (references.Length > 32) throw new RunnerSetupException("runner.secret-reference-limit", "The configuration contains too many secret references.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string referenceValue in references)
        {
            if (!SecretReference.TryParse(referenceValue, out SecretReference reference)) continue;
            string? value = await secretStore.GetAsync(reference, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(value)) values.Add(referenceValue, value);
        }
        return values;
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (text is not null) yield return text;
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
                foreach (string text in EnumerateStrings(property.Value)) yield return text;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
                foreach (string text in EnumerateStrings(item)) yield return text;
        }
    }

    private async ValueTask<ScheduleOccurrenceRecord?> TryClaimWithRetryAsync(
        OccurrenceId occurrenceId,
        string runnerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await occurrences.TryClaimAsync(
                    occurrenceId,
                    runnerInstanceId,
                    nowUtc,
                    options.ClaimDuration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (attempt < 4 && exception.SqliteErrorCode is 5 or 6 or 8)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50 * (attempt + 1)),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<AgentLeaseRecord?> TryAcquireExecutionLeaseWithRetryAsync(
        string leaseName,
        string ownerId,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await leases.TryAcquireAsync(
                    leaseName,
                    ownerId,
                    nowUtc,
                    duration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 4 && IsTransientSqliteWrite(exception))
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50 * (attempt + 1)),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransientSqliteWrite(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 or 8 })
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<AgentProcessHandle?> TryLaunchAsync(
        ValidatedAgentExecutable executable,
        AgentRunRecord run,
        ScheduleOccurrenceRecord occurrence,
        string configurationFile,
        string agentDataDirectory,
        string artifactDirectory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrity.EnsureUnchangedAsync(executable, cancellationToken).ConfigureAwait(false);
            AgentProcessHandle process = await launcher.LaunchAsync(new(
                executable,
                run.Id,
                occurrence.Id,
                occurrence.AgentId,
                occurrence.CommandName,
                configurationFile,
                agentDataDirectory,
                artifactDirectory), cancellationToken).ConfigureAwait(false);
            try
            {
                await integrity.EnsureUnchangedAsync(executable, CancellationToken.None).ConfigureAwait(false);
                return process;
            }
            catch
            {
                await KillStartedProcessAsync(process, logger).ConfigureAwait(false);
                await process.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            logger.Error("Agent process launch failed with {FailureType}", exception.GetType().Name);
            return null;
        }
    }

    private async ValueTask FailClaimedOccurrenceAsync(
        ScheduleOccurrenceRecord occurrence,
        string reasonCode,
        string message,
        CancellationToken cancellationToken)
    {
        ScheduleOccurrenceRecord? terminal = await occurrences.TryTransitionAsync(new(
            occurrence.Id,
            [OccurrenceStatus.Claimed],
            OccurrenceStatus.Failed,
            timeProvider.GetUtcNow(),
            reasonCode,
            message), cancellationToken).ConfigureAwait(false);
        if (terminal is not null)
        {
            _ = await fixedDelay.OnOccurrenceTerminalAsync(
                occurrence.Id,
                terminal.CompletedAtUtc ?? timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }

        _ = await auditWriter.WriteAsync(new(
            AuditActorType.Runner,
            "runner",
            "occurrence.execution-rejected",
            "occurrence",
            occurrence.Id.ToString(),
            AuditOutcome.Failed,
            occurrence.Id.Value,
            RunId: null,
            JsonSerializer.SerializeToElement(new { reasonCode })), cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateScheduleRunnable(
        ScheduleOccurrenceRecord occurrence,
        AgentScheduleRecord? schedule,
        DateTimeOffset nowUtc)
    {
        if (occurrence.ScheduleId.HasValue && schedule is null)
        {
            throw new RunnerSetupException("runner.schedule-missing", "The occurrence schedule does not exist.");
        }

        if (schedule is not null
            && (!schedule.IsEnabled || ScheduleOccurrencePlanner.IsEffectivelyPaused(schedule, nowUtc)))
        {
            throw new RunnerSetupException("runner.schedule-not-runnable", "The occurrence schedule is disabled or paused.");
        }
    }

    private static async Task KillStartedProcessAsync(AgentProcessHandle process, ILogger logger)
    {
        try
        {
            if (!process.Process.HasExited)
            {
                process.Process.Kill(entireProcessTree: true);
            }

            await process.Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.Error("Cleanup of a just-started agent process failed with {FailureType}", exception.GetType().Name);
        }
    }

    private static OccurrenceExecutionResult ToExecutionResult(AgentRunRecord run)
    {
        int runnerExitCode = run.Status switch
        {
            AgentRunStatus.Completed => RunnerExitCode.Success,
            AgentRunStatus.Cancelled => RunnerExitCode.Cancelled,
            AgentRunStatus.TimedOut => RunnerExitCode.TimedOut,
            _ => RunnerExitCode.ExecutionFailed,
        };
        return new(
            runnerExitCode,
            $"run.{run.Status.ToString().ToLowerInvariant()}",
            run.OccurrenceId,
            run.Id,
            run.Status,
            run.ExitCode,
            run.SummaryText ?? "The run has no summary.");
    }

    private static OccurrenceExecutionResult NotRunnable(
        OccurrenceId occurrenceId,
        string code,
        string message) => new(
            RunnerExitCode.NotRunnable,
            code,
            occurrenceId,
            RunId: null,
            RunStatus: null,
            AgentExitCode: null,
            message);

    private static ProcessSupervisionResult FailedSupervision(string reason) => new(
        ExitCode: null,
        TimedOut: false,
        CancellationRequested: false,
        LeaseLost: false,
        ProcessTreeKilled: false,
        FailureReason: reason);

    private static AgentProtocolIngestionResult EmptyProtocol(bool fatal = false, string? reason = null) => new(
        AcceptedEvents: 0,
        ProtocolViolations: fatal ? 1 : 0,
        FatalProtocolIssue: fatal,
        FatalReasonCode: reason,
        SummaryText: null,
        SummaryJson: null,
        ReportedSummaryStatus: null,
        ReportedCompletedExitCode: null,
        CompletedEventSeen: false);

    private sealed class RunnerSetupException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
