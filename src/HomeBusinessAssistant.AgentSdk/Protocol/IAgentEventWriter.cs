namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Writes versioned agent events to the reserved standard-output stream.</summary>
public interface IAgentEventWriter
{
    /// <summary>Writes the lifecycle start event.</summary>
    ValueTask WriteStartedAsync(StartedPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes a liveness heartbeat.</summary>
    ValueTask WriteHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes a bounded progress update.</summary>
    ValueTask WriteProgressAsync(ProgressPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes a named metric.</summary>
    ValueTask WriteMetricAsync(MetricPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes resumable structured checkpoint data.</summary>
    ValueTask WriteCheckpointAsync(CheckpointPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes artifact metadata.</summary>
    ValueTask WriteArtifactAsync(ArtifactPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes a non-fatal warning.</summary>
    ValueTask WriteWarningAsync(WarningPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes an error diagnostic.</summary>
    ValueTask WriteErrorAsync(ErrorPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes and flushes the terminal run summary.</summary>
    ValueTask WriteSummaryAsync(SummaryPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Writes and flushes the final process exit code.</summary>
    ValueTask WriteCompletedAsync(CompletedPayload payload, CancellationToken cancellationToken = default);
}
