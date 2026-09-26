using System.Text.Json;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>The common immutable envelope for one JSONL protocol record.</summary>
public abstract record AgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc);

/// <summary>An event indicating that command execution began.</summary>
public sealed record StartedAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    StartedPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event indicating that a running agent remains responsive.</summary>
public sealed record HeartbeatAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    HeartbeatPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting bounded operation progress.</summary>
public sealed record ProgressAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    ProgressPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting a named metric.</summary>
public sealed record MetricAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    MetricPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting resumable structured state.</summary>
public sealed record CheckpointAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    CheckpointPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event registering an artifact beneath the run artifact directory.</summary>
public sealed record ArtifactAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    ArtifactPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting a non-fatal diagnostic.</summary>
public sealed record WarningAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    WarningPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting a failed operation or sub-operation.</summary>
public sealed record ErrorAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    ErrorPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>An event reporting the terminal deterministic run summary.</summary>
public sealed record SummaryAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    SummaryPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>The final event reporting the process exit code.</summary>
public sealed record CompletedAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    CompletedPayload Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);

/// <summary>A forward-compatible event whose discriminator is not known to this SDK version.</summary>
public sealed record UnknownAgentEvent(
    AgentProtocolVersion ProtocolVersion,
    string Type,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    JsonElement Payload)
    : AgentEvent(ProtocolVersion, Type, RunId, Sequence, TimestampUtc);
