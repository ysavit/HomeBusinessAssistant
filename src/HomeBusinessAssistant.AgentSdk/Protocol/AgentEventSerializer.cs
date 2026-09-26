using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Contracts;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Serializes and safely reads one compact JSONL agent event.</summary>
public static class AgentEventSerializer
{
    /// <summary>The maximum UTF-8 size of one event line.</summary>
    public const int MaximumEventSizeBytes = 262_144;

    /// <summary>Serializes one event without a trailing newline.</summary>
    public static string Serialize(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        if (!IsSupportedRuntimeType(agentEvent))
        {
            throw new AgentProtocolSerializationException(new ContractValidationError(
                "protocol.unsupportedRuntimeType",
                "$",
                "The event runtime type is not supported."));
        }

        var json = JsonSerializer.Serialize(agentEvent, agentEvent.GetType(), AgentJson.Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumEventSizeBytes)
        {
            throw new AgentProtocolSerializationException(new ContractValidationError(
                "protocol.eventTooLarge",
                "$",
                $"The serialized event exceeds {MaximumEventSizeBytes} UTF-8 bytes."));
        }

        return json;
    }

    /// <summary>Reads one event line while preserving unknown event payloads.</summary>
    public static AgentEventReadResult Deserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Failure("protocol.emptyLine", "$", "The event line is empty.");
        }

        if (line.Contains('\r', StringComparison.Ordinal) || line.Contains('\n', StringComparison.Ordinal))
        {
            return Failure("protocol.multilineEvent", "$", "An event must occupy exactly one line.");
        }

        if (Encoding.UTF8.GetByteCount(line) > MaximumEventSizeBytes)
        {
            return Failure(
                "protocol.eventTooLarge",
                "$",
                $"The event exceeds {MaximumEventSizeBytes} UTF-8 bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure("protocol.invalidEnvelope", "$", "The event envelope must be a JSON object.");
            }

            if (!document.RootElement.TryGetProperty("type", out var typeProperty)
                || typeProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(typeProperty.GetString()))
            {
                return Failure("protocol.missingType", "$.type", "The event type is required.");
            }

            var eventType = typeProperty.GetString()!;
            AgentEvent? agentEvent = eventType switch
            {
                AgentEventTypes.Started => JsonSerializer.Deserialize<StartedAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Heartbeat => JsonSerializer.Deserialize<HeartbeatAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Progress => JsonSerializer.Deserialize<ProgressAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Metric => JsonSerializer.Deserialize<MetricAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Checkpoint => JsonSerializer.Deserialize<CheckpointAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Artifact => JsonSerializer.Deserialize<ArtifactAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Warning => JsonSerializer.Deserialize<WarningAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Error => JsonSerializer.Deserialize<ErrorAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Summary => JsonSerializer.Deserialize<SummaryAgentEvent>(line, AgentJson.Options),
                AgentEventTypes.Completed => JsonSerializer.Deserialize<CompletedAgentEvent>(line, AgentJson.Options),
                _ => JsonSerializer.Deserialize<UnknownAgentEvent>(line, AgentJson.Options),
            };

            return agentEvent is null
                ? Failure("protocol.invalidEnvelope", "$", "The event envelope is invalid.")
                : new AgentEventReadResult(agentEvent, Array.Empty<ContractValidationError>());
        }
        catch (JsonException exception)
        {
            return Failure(
                "protocol.invalidJson",
                exception.Path ?? "$",
                "The event contains invalid JSON or a value of the wrong type.");
        }
        catch (NotSupportedException)
        {
            return Failure("protocol.unsupportedValue", "$", "The event contains an unsupported value.");
        }
    }

    private static bool IsSupportedRuntimeType(AgentEvent agentEvent) => agentEvent is
        StartedAgentEvent
        or HeartbeatAgentEvent
        or ProgressAgentEvent
        or MetricAgentEvent
        or CheckpointAgentEvent
        or ArtifactAgentEvent
        or WarningAgentEvent
        or ErrorAgentEvent
        or SummaryAgentEvent
        or CompletedAgentEvent
        or UnknownAgentEvent;

    private static AgentEventReadResult Failure(string code, string path, string message) =>
        new(null, [new ContractValidationError(code, path, message)]);
}
