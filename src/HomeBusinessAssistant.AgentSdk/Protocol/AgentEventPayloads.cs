using System.Text.Json;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Describes the command and bounded metadata at run start.</summary>
public sealed record StartedPayload(
    string Command,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Describes the optional phase and liveness message.</summary>
public sealed record HeartbeatPayload(
    string? Phase = null,
    string? Message = null);

/// <summary>Describes a range or percentage progress update.</summary>
public sealed record ProgressPayload(
    long? Current = null,
    long? Total = null,
    double? Percentage = null,
    string? Phase = null,
    string? Message = null);

/// <summary>Describes one named numeric or textual metric.</summary>
public sealed record MetricPayload(
    string Name,
    double? NumericValue = null,
    string? TextValue = null,
    string? Unit = null,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>Describes one named structured checkpoint.</summary>
public sealed record CheckpointPayload(
    string Key,
    JsonElement State);

/// <summary>Describes an artifact stored beneath the run artifact directory.</summary>
public sealed record ArtifactPayload(
    string Kind,
    string RelativePath,
    string ContentType,
    string? Description = null,
    DateTimeOffset? DeleteAfterUtc = null);

/// <summary>Describes a non-fatal warning.</summary>
public sealed record WarningPayload(
    string Code,
    string Message,
    JsonElement? Data = null);

/// <summary>Describes an error and whether it is transient.</summary>
public sealed record ErrorPayload(
    string Code,
    string Message,
    bool Transient,
    JsonElement? Data = null);

/// <summary>Describes the terminal run status and deterministic summary.</summary>
public sealed record SummaryPayload(
    AgentRunStatus Status,
    string Text,
    JsonElement? Data = null);

/// <summary>Describes the final stable process exit code.</summary>
public sealed record CompletedPayload(int ExitCode);
