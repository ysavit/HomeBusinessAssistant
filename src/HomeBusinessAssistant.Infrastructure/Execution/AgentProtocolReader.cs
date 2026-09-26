using System.Buffers;
using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using Serilog;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Strict bounded protocol 1.x ingestion and redacted stderr capture.</summary>
public sealed class AgentProtocolReader(
    IAgentRunRepository runs,
    IArtifactStore artifacts,
    TimeProvider timeProvider,
    RunnerSupervisionOptions options) : IAgentProtocolReader
{
    private const int MaximumDiagnosticTextLength = 4_096;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public async Task<AgentProtocolIngestionResult> ReadStandardOutputAsync(
        Stream output,
        AgentRunId runId,
        AgentId agentId,
        string artifactDirectory,
        RunnerEventSequence runnerSequences,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(runnerSequences);
        ArgumentNullException.ThrowIfNull(logger);
        var state = new ProtocolState();
        await BoundedUtf8LineReader.ReadAsync(
            output,
            AgentEventSerializer.MaximumEventSizeBytes,
            async line =>
            {
                if (line.WasOversized)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.event-too-large",
                        "An oversized stdout record was discarded.",
                        rawLine: null,
                        fatalImmediately: false,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (line.InvalidUtf8)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.invalid-utf8",
                        "A stdout record was not valid UTF-8.",
                        rawLine: null,
                        fatalImmediately: false,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                string text = line.Text ?? string.Empty;
                AgentEventReadResult read = AgentEventSerializer.Deserialize(text);
                if (!read.IsSuccess)
                {
                    string code = read.Errors.Count > 0 ? read.Errors[0].Code : "protocol.invalid-record";
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        code,
                        "A malformed stdout record was discarded.",
                        text,
                        fatalImmediately: false,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                AgentEvent agentEvent = read.Event!;
                var validation = AgentEventValidator.Validate(agentEvent, timeProvider);
                if (!validation.IsValid)
                {
                    string code = validation.Errors[0].Code;
                    bool fatalValidation = validation.Errors.Any(error =>
                        error.Code is "protocol.invalidRunId" or "protocol.invalidArtifactPath");
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        code,
                        "A schema-invalid stdout record was discarded.",
                        text,
                        fatalValidation,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (agentEvent.RunId != runId)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.run-id-mismatch",
                        "An event for a different run was discarded.",
                        text,
                        fatalImmediately: true,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (state.CompletedSeen)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.event-after-completed",
                        "An event after the completed record was discarded.",
                        text,
                        fatalImmediately: true,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (agentEvent.Sequence <= state.LastSequence)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.sequence-not-monotonic",
                        "A non-monotonic event sequence was discarded.",
                        text,
                        fatalImmediately: false,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (state.AcceptedEvents == 0 && agentEvent is not StartedAgentEvent)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        "protocol.started-not-first",
                        "The first accepted event was not started.",
                        text,
                        fatalImmediately: false,
                        cancellationToken).ConfigureAwait(false);
                }

                if (agentEvent is UnknownAgentEvent)
                {
                    await AppendRunnerDiagnosticAsync(
                        runId,
                        runnerSequences.Next(),
                        "protocol.unknown-event",
                        "protocol.unknown-event",
                        "A forward-compatible unknown event was preserved as an untrusted diagnostic.",
                        text,
                        cancellationToken).ConfigureAwait(false);
                    logger.Information("Unknown agent protocol event type {EventType} was preserved as a diagnostic", agentEvent.Type);
                    state.LastSequence = agentEvent.Sequence;
                    state.AcceptedEvents++;
                    return;
                }

                string? lifecycleViolation = GetLifecycleViolation(state, agentEvent);
                if (lifecycleViolation is not null)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        lifecycleViolation,
                        "A duplicate or invalid terminal lifecycle record was discarded.",
                        text,
                        fatalImmediately: true,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                try
                {
                    await PersistKnownEventAsync(
                        agentEvent,
                        agentId,
                        artifactDirectory,
                        state,
                        logger,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (AgentArtifactValidationException exception)
                {
                    await RecordViolationAsync(
                        state,
                        runId,
                        runnerSequences,
                        logger,
                        exception.Code,
                        exception.Message,
                        text,
                        fatalImmediately: true,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                state.LastSequence = agentEvent.Sequence;
                state.AcceptedEvents++;
            },
            cancellationToken).ConfigureAwait(false);

        if (!state.StartedSeen)
        {
            await RecordViolationAsync(
                state,
                runId,
                runnerSequences,
                logger,
                "protocol.started-missing",
                "The stdout stream ended without a started event.",
                rawLine: null,
                fatalImmediately: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (!state.SummarySeen)
        {
            await RecordViolationAsync(
                state,
                runId,
                runnerSequences,
                logger,
                "protocol.summary-missing",
                "The stdout stream ended without a summary event.",
                rawLine: null,
                fatalImmediately: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (!state.CompletedSeen)
        {
            await RecordViolationAsync(
                state,
                runId,
                runnerSequences,
                logger,
                "protocol.completed-missing",
                "The stdout stream ended without a completed event.",
                rawLine: null,
                fatalImmediately: true,
                cancellationToken).ConfigureAwait(false);
        }

        return new(
            state.AcceptedEvents,
            state.Violations,
            state.Fatal,
            state.FatalReasonCode,
            state.SummaryText,
            state.SummaryJson,
            state.ReportedSummaryStatus,
            state.ReportedCompletedExitCode,
            state.CompletedSeen);
    }

    /// <inheritdoc />
    public async Task<StderrIngestionResult> ReadStandardErrorAsync(
        Stream errorStream,
        AgentRunId runId,
        RunnerEventSequence runnerSequences,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errorStream);
        var observed = 0;
        var persisted = 0;
        var suppressed = 0;
        await BoundedUtf8LineReader.ReadAsync(
            errorStream,
            maximumLineBytes: 65_536,
            async line =>
            {
                observed++;
                string diagnostic = line switch
                {
                    { WasOversized: true } => "An oversized stderr line was discarded.",
                    { InvalidUtf8: true } => "An invalid UTF-8 stderr line was discarded.",
                    _ => AuditRedactor.RedactText(line.Text ?? string.Empty, MaximumDiagnosticTextLength),
                };
                logger.Information("Agent stderr: {AgentDiagnostic}", diagnostic);
                if (persisted < options.MaximumStderrEvents)
                {
                    await runs.AppendEventAsync(new(
                        Guid.NewGuid(),
                        runId,
                        runnerSequences.Next(),
                        timeProvider.GetUtcNow(),
                        "Information",
                        "runner.stderr",
                        diagnostic,
                        "{}"), cancellationToken).ConfigureAwait(false);
                    persisted++;
                }
                else
                {
                    suppressed++;
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (suppressed > 0)
        {
            await runs.AppendEventAsync(new(
                Guid.NewGuid(),
                runId,
                runnerSequences.Next(),
                timeProvider.GetUtcNow(),
                "Warning",
                "runner.stderr-suppressed",
                $"{suppressed} additional stderr lines were suppressed from SQLite.",
                JsonSerializer.Serialize(new { suppressed })), cancellationToken).ConfigureAwait(false);
        }

        return new(observed, persisted, suppressed);
    }

    private async ValueTask PersistKnownEventAsync(
        AgentEvent agentEvent,
        AgentId agentId,
        string artifactDirectory,
        ProtocolState state,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        switch (agentEvent)
        {
            case StartedAgentEvent started:
                state.StartedSeen = true;
                await AppendAgentEventAsync(started, "Information", started.Payload.Command, started.Payload, cancellationToken).ConfigureAwait(false);
                break;
            case HeartbeatAgentEvent heartbeat:
                await AppendAgentEventAsync(
                    heartbeat,
                    "Debug",
                    heartbeat.Payload.Message ?? heartbeat.Payload.Phase ?? "Agent heartbeat.",
                    heartbeat.Payload,
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    _ = await runs.UpdateHeartbeatAsync(agentEvent.RunId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    logger.Warning("Agent heartbeat persistence failed with {FailureType}", exception.GetType().Name);
                }

                break;
            case ProgressAgentEvent progress:
                await AppendAgentEventAsync(
                    progress,
                    "Information",
                    progress.Payload.Message ?? progress.Payload.Phase ?? "Agent progress.",
                    progress.Payload,
                    cancellationToken).ConfigureAwait(false);
                break;
            case MetricAgentEvent metric:
                await runs.AppendMetricAsync(new(
                    Guid.NewGuid(),
                    metric.RunId,
                    metric.Payload.Name,
                    metric.Payload.NumericValue,
                    metric.Payload.TextValue,
                    metric.Payload.Unit,
                    JsonSerializer.Serialize(metric.Payload.Tags ?? new Dictionary<string, string>()),
                    metric.TimestampUtc), cancellationToken).ConfigureAwait(false);
                break;
            case CheckpointAgentEvent checkpoint:
                await AppendAgentEventAsync(checkpoint, "Information", checkpoint.Payload.Key, checkpoint.Payload, cancellationToken).ConfigureAwait(false);
                break;
            case ArtifactAgentEvent artifact:
                RunArtifactRecord stored = await StoreArtifactAsync(
                    artifact,
                    agentId,
                    artifactDirectory,
                    cancellationToken).ConfigureAwait(false);
                await AppendAgentEventAsync(
                    artifact,
                    "Information",
                    artifact.Payload.Description ?? artifact.Payload.RelativePath,
                    new
                    {
                        artifact.Payload.Kind,
                        artifact.Payload.RelativePath,
                        artifact.Payload.ContentType,
                        artifact.Payload.Description,
                        artifactId = stored.Id,
                        stored.SizeBytes,
                        stored.Sha256,
                        storedRelativePath = stored.RelativePath,
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
            case WarningAgentEvent warning:
                await AppendAgentEventAsync(warning, "Warning", warning.Payload.Message, warning.Payload, cancellationToken).ConfigureAwait(false);
                break;
            case ErrorAgentEvent error:
                await AppendAgentEventAsync(error, "Error", error.Payload.Message, error.Payload, cancellationToken).ConfigureAwait(false);
                break;
            case SummaryAgentEvent summary:
                state.SummarySeen = true;
                state.SummaryText = AuditRedactor.RedactText(summary.Payload.Text, 8_192);
                state.SummaryJson = summary.Payload.Data.HasValue
                    ? AuditRedactor.Redact(summary.Payload.Data.Value)
                    : null;
                state.ReportedSummaryStatus = summary.Payload.Status;
                await AppendAgentEventAsync(summary, "Information", summary.Payload.Text, summary.Payload, cancellationToken).ConfigureAwait(false);
                break;
            case CompletedAgentEvent completed:
                state.CompletedSeen = true;
                state.ReportedCompletedExitCode = completed.Payload.ExitCode;
                await AppendAgentEventAsync(
                    completed,
                    "Information",
                    $"Agent reported exit code {completed.Payload.ExitCode}.",
                    completed.Payload,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("A known protocol event was not handled.");
        }
    }

    private async ValueTask<RunArtifactRecord> StoreArtifactAsync(
        ArtifactAgentEvent agentEvent,
        AgentId agentId,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(artifactDirectory);
        string[] segments = agentEvent.Payload.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        string sourcePath;
        try
        {
            sourcePath = StoragePathPolicy.CombineContained(root, segments);
        }
        catch (InvalidOperationException exception)
        {
            throw new AgentArtifactValidationException("protocol.artifact-traversal", "The agent artifact path escaped its assigned directory.", exception);
        }

        if (!File.Exists(sourcePath))
        {
            throw new AgentArtifactValidationException("protocol.artifact-missing", "The agent-reported artifact file does not exist.");
        }

        string sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new AgentArtifactValidationException("protocol.artifact-path-invalid", "The agent artifact has no containing directory.");
        try
        {
            StoragePathPolicy.RejectExistingReparsePoints(root, sourceDirectory);
        }
        catch (InvalidOperationException exception)
        {
            throw new AgentArtifactValidationException("protocol.artifact-reparse", "The agent artifact path traverses a reparse point.", exception);
        }

        if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new AgentArtifactValidationException("protocol.artifact-reparse", "The agent artifact file cannot be a reparse point.");
        }

        var before = new FileInfo(sourcePath);
        if (before.Length > options.MaximumAgentArtifactBytes)
        {
            throw new AgentArtifactValidationException("protocol.artifact-too-large", "The agent artifact exceeds the configured maximum size.");
        }

        using var stableCopy = new MemoryStream(capacity: checked((int)Math.Min(before.Length, int.MaxValue)));
        try
        {
            await using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65_536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(input, stableCopy, options.MaximumAgentArtifactBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new AgentArtifactValidationException(
                "protocol.artifact-changed",
                "The agent artifact was locked or changed while the Runner inspected it.",
                exception);
        }

        var after = new FileInfo(sourcePath);
        if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            throw new AgentArtifactValidationException("protocol.artifact-changed", "The agent artifact changed while the Runner inspected it.");
        }

        stableCopy.Position = 0;
        return await artifacts.WriteAsync(new(
            agentEvent.RunId,
            agentId,
            agentEvent.Payload.Kind,
            Path.GetFileName(sourcePath),
            agentEvent.Payload.ContentType,
            stableCopy,
            DeleteAfterUtc: agentEvent.Payload.DeleteAfterUtc), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AppendAgentEventAsync(
        AgentEvent agentEvent,
        string level,
        string message,
        object data,
        CancellationToken cancellationToken)
    {
        string dataJson = AuditRedactor.Redact(JsonSerializer.SerializeToElement(data));
        await runs.AppendEventAsync(new(
            Guid.NewGuid(),
            agentEvent.RunId,
            agentEvent.Sequence,
            agentEvent.TimestampUtc,
            level,
            agentEvent.Type,
            AuditRedactor.RedactText(message, MaximumDiagnosticTextLength),
            dataJson), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RecordViolationAsync(
        ProtocolState state,
        AgentRunId runId,
        RunnerEventSequence runnerSequences,
        ILogger logger,
        string code,
        string message,
        string? rawLine,
        bool fatalImmediately,
        CancellationToken cancellationToken)
    {
        state.Violations++;
        if (fatalImmediately || state.Violations >= options.MaximumProtocolViolations)
        {
            state.Fatal = true;
            state.FatalReasonCode ??= fatalImmediately ? code : "protocol.violation-limit";
        }

        await AppendRunnerDiagnosticAsync(
            runId,
            runnerSequences.Next(),
            "runner.protocol-violation",
            code,
            message,
            rawLine,
            cancellationToken).ConfigureAwait(false);
        logger.Warning("Agent protocol violation {ViolationCode}: {ViolationMessage}", code, message);
    }

    private ValueTask AppendRunnerDiagnosticAsync(
        AgentRunId runId,
        long sequence,
        string eventType,
        string code,
        string message,
        string? rawLine,
        CancellationToken cancellationToken)
    {
        string? safeLine = rawLine is null ? null : AuditRedactor.RedactText(rawLine, MaximumDiagnosticTextLength);
        string dataJson = AuditRedactor.Redact(JsonSerializer.SerializeToElement(new { code, line = safeLine }));
        return runs.AppendEventAsync(new(
            Guid.NewGuid(),
            runId,
            sequence,
            timeProvider.GetUtcNow(),
            "Warning",
            eventType,
            AuditRedactor.RedactText(message, MaximumDiagnosticTextLength),
            dataJson), cancellationToken);
    }

    private static string? GetLifecycleViolation(ProtocolState state, AgentEvent agentEvent)
    {
        if (agentEvent is StartedAgentEvent && state.StartedSeen)
        {
            return "protocol.duplicate-started";
        }

        if (agentEvent is SummaryAgentEvent && state.SummarySeen)
        {
            return "protocol.duplicate-summary";
        }

        if (agentEvent is CompletedAgentEvent && state.CompletedSeen)
        {
            return "protocol.duplicate-completed";
        }

        return null;
    }

    private static async ValueTask CopyBoundedAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65_536);
        long total = 0;
        try
        {
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                total += read;
                if (total > maximumBytes)
                {
                    throw new AgentArtifactValidationException("protocol.artifact-too-large", "The agent artifact exceeds the configured maximum size.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class ProtocolState
    {
        public int AcceptedEvents { get; set; }
        public int Violations { get; set; }
        public bool Fatal { get; set; }
        public string? FatalReasonCode { get; set; }
        public long LastSequence { get; set; }
        public bool StartedSeen { get; set; }
        public bool SummarySeen { get; set; }
        public bool CompletedSeen { get; set; }
        public string? SummaryText { get; set; }
        public string? SummaryJson { get; set; }
        public AgentRunStatus? ReportedSummaryStatus { get; set; }
        public int? ReportedCompletedExitCode { get; set; }
    }
}

internal sealed class AgentArtifactValidationException : Exception
{
    public AgentArtifactValidationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

internal sealed record BoundedUtf8Line(string? Text, bool WasOversized, bool InvalidUtf8);

internal static class BoundedUtf8LineReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task ReadAsync(
        Stream stream,
        int maximumLineBytes,
        Func<BoundedUtf8Line, ValueTask> onLine,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLineBytes, 1);

        byte[] input = ArrayPool<byte>.Shared.Rent(8_192);
        var line = new ArrayBufferWriter<byte>(Math.Min(maximumLineBytes, 8_192));
        var oversized = false;
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(input.AsMemory(0, input.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (line.WrittenCount > 0 || oversized)
                    {
                        await onLine(CreateLine(line, oversized)).ConfigureAwait(false);
                    }

                    return;
                }

                for (var index = 0; index < read; index++)
                {
                    byte value = input[index];
                    if (value == (byte)'\n')
                    {
                        await onLine(CreateLine(line, oversized)).ConfigureAwait(false);
                        line.Clear();
                        oversized = false;
                    }
                    else if (!oversized)
                    {
                        if (line.WrittenCount >= maximumLineBytes)
                        {
                            line.Clear();
                            oversized = true;
                        }
                        else
                        {
                            line.GetSpan(1)[0] = value;
                            line.Advance(1);
                        }
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
        }
    }

    private static BoundedUtf8Line CreateLine(ArrayBufferWriter<byte> line, bool oversized)
    {
        if (oversized)
        {
            return new(null, WasOversized: true, InvalidUtf8: false);
        }

        ReadOnlySpan<byte> bytes = line.WrittenSpan;
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        try
        {
            return new(StrictUtf8.GetString(bytes), WasOversized: false, InvalidUtf8: false);
        }
        catch (DecoderFallbackException)
        {
            return new(null, WasOversized: false, InvalidUtf8: true);
        }
    }
}
