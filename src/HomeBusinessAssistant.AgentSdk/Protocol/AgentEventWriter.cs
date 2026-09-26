using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Writes compact JSONL events with synchronized monotonic sequencing.</summary>
public sealed class AgentEventWriter : IAgentEventWriter, IDisposable
{
    private readonly TextWriter _output;
    private readonly TimeProvider _timeProvider;
    private readonly AgentRunId _runId;
    private readonly AgentProtocolVersion _protocolVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _sequence;
    private bool _completed;
    private bool _disposed;

    /// <summary>Creates a writer for one run and one output stream.</summary>
    public AgentEventWriter(
        TextWriter output,
        TimeProvider timeProvider,
        AgentRunId runId,
        AgentProtocolVersion? protocolVersion = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (runId.Value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty run identifier is required.", nameof(runId));
        }

        _output = output;
        _timeProvider = timeProvider;
        _runId = runId;
        _protocolVersion = protocolVersion ?? AgentProtocolVersion.Current;
    }

    /// <summary>Gets the number assigned to the last successfully written event.</summary>
    public long CurrentSequence => Interlocked.Read(ref _sequence);

    /// <inheritdoc />
    public ValueTask WriteStartedAsync(StartedPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new StartedAgentEvent(
                _protocolVersion,
                AgentEventTypes.Started,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new HeartbeatAgentEvent(
                _protocolVersion,
                AgentEventTypes.Heartbeat,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteProgressAsync(ProgressPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new ProgressAgentEvent(
                _protocolVersion,
                AgentEventTypes.Progress,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteMetricAsync(MetricPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new MetricAgentEvent(
                _protocolVersion,
                AgentEventTypes.Metric,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteCheckpointAsync(CheckpointPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new CheckpointAgentEvent(
                _protocolVersion,
                AgentEventTypes.Checkpoint,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteArtifactAsync(ArtifactPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new ArtifactAgentEvent(
                _protocolVersion,
                AgentEventTypes.Artifact,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteWarningAsync(WarningPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new WarningAgentEvent(
                _protocolVersion,
                AgentEventTypes.Warning,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteErrorAsync(ErrorPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new ErrorAgentEvent(
                _protocolVersion,
                AgentEventTypes.Error,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: false,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteSummaryAsync(SummaryPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new SummaryAgentEvent(
                _protocolVersion,
                AgentEventTypes.Summary,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: true,
            completesStream: false,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask WriteCompletedAsync(CompletedPayload payload, CancellationToken cancellationToken = default) =>
        WriteAsync(
            (sequence, timestamp) => new CompletedAgentEvent(
                _protocolVersion,
                AgentEventTypes.Completed,
                _runId,
                sequence,
                timestamp,
                payload),
            flush: true,
            completesStream: true,
            cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async ValueTask WriteAsync(
        Func<long, DateTimeOffset, AgentEvent> eventFactory,
        bool flush,
        bool completesStream,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(eventFactory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed)
            {
                throw new AgentEventWriteException(
                [
                    new ContractValidationError(
                        "writer.streamCompleted",
                        "$",
                        "No event can be written after the completed event."),
                ]);
            }

            var sequence = _sequence + 1;
            var agentEvent = eventFactory(sequence, _timeProvider.GetUtcNow().ToUniversalTime());
            var validation = AgentEventValidator.Validate(agentEvent, _timeProvider);
            if (!validation.IsValid)
            {
                throw new AgentEventWriteException(validation.Errors);
            }

            var json = AgentEventSerializer.Serialize(agentEvent);
            await _output.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (flush)
            {
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _sequence, sequence);
            _completed = completesStream;
        }
        finally
        {
            _gate.Release();
        }
    }
}
