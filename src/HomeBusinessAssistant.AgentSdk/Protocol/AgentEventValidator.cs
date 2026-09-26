using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Validates a deserialized protocol event and its type-specific payload.</summary>
public static class AgentEventValidator
{
    /// <summary>The maximum allowed clock skew into the future.</summary>
    public static TimeSpan MaximumFutureClockSkew { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Validates an event using the supplied clock.</summary>
    public static ContractValidationResult Validate(AgentEvent agentEvent, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var errors = new List<ContractValidationError>();

        ValidateEnvelope(agentEvent, timeProvider, errors);
        switch (agentEvent)
        {
            case StartedAgentEvent started:
                ValidateExpectedType(started.Type, AgentEventTypes.Started, errors);
                ValidateStarted(started.Payload, errors);
                break;
            case HeartbeatAgentEvent heartbeat:
                ValidateExpectedType(heartbeat.Type, AgentEventTypes.Heartbeat, errors);
                ValidateHeartbeat(heartbeat.Payload, errors);
                break;
            case ProgressAgentEvent progress:
                ValidateExpectedType(progress.Type, AgentEventTypes.Progress, errors);
                ValidateProgress(progress.Payload, errors);
                break;
            case MetricAgentEvent metric:
                ValidateExpectedType(metric.Type, AgentEventTypes.Metric, errors);
                ValidateMetric(metric.Payload, errors);
                break;
            case CheckpointAgentEvent checkpoint:
                ValidateExpectedType(checkpoint.Type, AgentEventTypes.Checkpoint, errors);
                ValidateCheckpoint(checkpoint.Payload, errors);
                break;
            case ArtifactAgentEvent artifact:
                ValidateExpectedType(artifact.Type, AgentEventTypes.Artifact, errors);
                ValidateArtifact(artifact.Payload, errors);
                break;
            case WarningAgentEvent warning:
                ValidateExpectedType(warning.Type, AgentEventTypes.Warning, errors);
                ValidateDiagnostic(warning.Payload?.Code, warning.Payload?.Message, "warning", errors);
                break;
            case ErrorAgentEvent error:
                ValidateExpectedType(error.Type, AgentEventTypes.Error, errors);
                ValidateDiagnostic(error.Payload?.Code, error.Payload?.Message, "error", errors);
                break;
            case SummaryAgentEvent summary:
                ValidateExpectedType(summary.Type, AgentEventTypes.Summary, errors);
                ValidateSummary(summary.Payload, errors);
                break;
            case CompletedAgentEvent completed:
                ValidateExpectedType(completed.Type, AgentEventTypes.Completed, errors);
                ValidateCompleted(completed.Payload, errors);
                break;
            case UnknownAgentEvent unknown:
                if (unknown.Payload.ValueKind == JsonValueKind.Undefined)
                {
                    Add(errors, "protocol.missingPayload", "$.payload", "The event payload is required.");
                }

                break;
            default:
                Add(errors, "protocol.unsupportedRuntimeType", "$", "The event runtime type is not supported.");
                break;
        }

        return new ContractValidationResult(errors);
    }

    private static void ValidateEnvelope(
        AgentEvent agentEvent,
        TimeProvider timeProvider,
        List<ContractValidationError> errors)
    {
        if (!agentEvent.ProtocolVersion.IsSupportedBy(AgentProtocolVersion.Current))
        {
            Add(errors, "protocol.unsupportedMajor", "$.protocolVersion", "The protocol major version is not supported.");
        }

        if (!ContractNameRules.IsValid(agentEvent.Type))
        {
            Add(errors, "protocol.invalidType", "$.type", "The event type is invalid.");
        }

        if (agentEvent.RunId.Value == Guid.Empty)
        {
            Add(errors, "protocol.invalidRunId", "$.runId", "The run identifier is invalid.");
        }

        if (agentEvent.Sequence < 1)
        {
            Add(errors, "protocol.invalidSequence", "$.sequence", "The event sequence must be at least one.");
        }

        if (agentEvent.TimestampUtc == default || agentEvent.TimestampUtc.Offset != TimeSpan.Zero)
        {
            Add(errors, "protocol.invalidTimestamp", "$.timestampUtc", "The event timestamp must be a non-default UTC value.");
        }
        else if (agentEvent.TimestampUtc > timeProvider.GetUtcNow() + MaximumFutureClockSkew)
        {
            Add(errors, "protocol.futureTimestamp", "$.timestampUtc", "The event timestamp is too far in the future.");
        }
    }

    private static void ValidateStarted(StartedPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (!ContractNameRules.IsValid(payload.Command))
        {
            Add(errors, "protocol.invalidCommand", "$.payload.command", "The command name is invalid.");
        }

        ValidateStringMap(payload.Metadata, "$.payload.metadata", errors);
    }

    private static void ValidateHeartbeat(HeartbeatPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (payload.Phase is not null && !ContractNameRules.IsValid(payload.Phase))
        {
            Add(errors, "protocol.invalidPhase", "$.payload.phase", "The heartbeat phase is invalid.");
        }

        ValidateOptionalMessage(payload.Message, "$.payload.message", errors);
    }

    private static void ValidateProgress(ProgressPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (payload.Current.HasValue != payload.Total.HasValue)
        {
            Add(errors, "protocol.incompleteProgressRange", "$.payload", "Progress current and total must be supplied together.");
        }
        else if (payload.Current is < 0 || payload.Total is < 0 || payload.Current > payload.Total)
        {
            Add(errors, "protocol.invalidProgressRange", "$.payload", "The progress range is invalid.");
        }

        if (payload.Percentage is { } percentage && (!double.IsFinite(percentage) || percentage is < 0 or > 100))
        {
            Add(errors, "protocol.invalidPercentage", "$.payload.percentage", "The progress percentage must be between zero and one hundred.");
        }

        if (payload.Current is null && payload.Percentage is null)
        {
            Add(errors, "protocol.missingProgressValue", "$.payload", "A progress range or percentage is required.");
        }

        if (payload.Phase is not null && !ContractNameRules.IsValid(payload.Phase))
        {
            Add(errors, "protocol.invalidPhase", "$.payload.phase", "The progress phase is invalid.");
        }

        ValidateOptionalMessage(payload.Message, "$.payload.message", errors);
    }

    private static void ValidateMetric(MetricPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (!ContractNameRules.IsValid(payload.Name))
        {
            Add(errors, "protocol.invalidMetricName", "$.payload.name", "The metric name is invalid.");
        }

        if (payload.NumericValue.HasValue == (payload.TextValue is not null))
        {
            Add(errors, "protocol.invalidMetricValue", "$.payload", "A metric must contain exactly one numericValue or textValue.");
        }

        if (payload.NumericValue is { } numericValue && !double.IsFinite(numericValue))
        {
            Add(errors, "protocol.invalidMetricValue", "$.payload.numericValue", "The numeric metric value must be finite.");
        }

        if (payload.TextValue is { Length: > 1_024 })
        {
            Add(errors, "protocol.metricTextTooLong", "$.payload.textValue", "The text metric value is too long.");
        }

        if (payload.Unit is { Length: > 64 } || payload.Unit?.Any(char.IsControl) == true)
        {
            Add(errors, "protocol.invalidMetricUnit", "$.payload.unit", "The metric unit is invalid.");
        }

        ValidateStringMap(payload.Tags, "$.payload.tags", errors);
    }

    private static void ValidateCheckpoint(CheckpointPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (!ContractNameRules.IsValid(payload.Key))
        {
            Add(errors, "protocol.invalidCheckpointKey", "$.payload.key", "The checkpoint key is invalid.");
        }

        if (payload.State.ValueKind != JsonValueKind.Object)
        {
            Add(errors, "protocol.invalidCheckpointState", "$.payload.state", "Checkpoint state must be a JSON object.");
        }
    }

    private static void ValidateArtifact(ArtifactPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (!ContractNameRules.IsValid(payload.Kind))
        {
            Add(errors, "protocol.invalidArtifactKind", "$.payload.kind", "The artifact kind is invalid.");
        }

        if (!ContractNameRules.IsSafeRelativePath(payload.RelativePath))
        {
            Add(errors, "protocol.invalidArtifactPath", "$.payload.relativePath", "The artifact path must be a safe relative path without traversal segments.");
        }

        if (string.IsNullOrWhiteSpace(payload.ContentType)
            || payload.ContentType.Length > 100
            || !payload.ContentType.Contains('/', StringComparison.Ordinal)
            || payload.ContentType.Any(char.IsControl))
        {
            Add(errors, "protocol.invalidContentType", "$.payload.contentType", "The artifact content type is invalid.");
        }

        ValidateOptionalMessage(payload.Description, "$.payload.description", errors);
        if (payload.DeleteAfterUtc is { } deleteAfter && deleteAfter.Offset != TimeSpan.Zero)
        {
            Add(errors, "protocol.invalidArtifactRetention", "$.payload.deleteAfterUtc", "Artifact retention timestamps must use UTC.");
        }
    }

    private static void ValidateDiagnostic(
        string? code,
        string? message,
        string kind,
        List<ContractValidationError> errors)
    {
        if (code is null && message is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (!ContractNameRules.IsValid(code))
        {
            Add(errors, $"protocol.invalid{kind[..1].ToUpperInvariant()}{kind[1..]}Code", "$.payload.code", $"The {kind} code is invalid.");
        }

        ValidateRequiredMessage(message, "$.payload.message", errors);
    }

    private static void ValidateSummary(SummaryPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
            return;
        }

        if (payload.Status is not (AgentRunStatus.Completed
            or AgentRunStatus.Failed
            or AgentRunStatus.TimedOut
            or AgentRunStatus.Cancelled
            or AgentRunStatus.Abandoned))
        {
            Add(errors, "protocol.invalidSummaryStatus", "$.payload.status", "The summary status must be terminal.");
        }

        ValidateRequiredMessage(payload.Text, "$.payload.text", errors);
    }

    private static void ValidateCompleted(CompletedPayload? payload, List<ContractValidationError> errors)
    {
        if (payload is null)
        {
            AddMissingPayload(errors);
        }
        else if (!AgentExitCode.IsDefined(payload.ExitCode))
        {
            Add(errors, "protocol.invalidExitCode", "$.payload.exitCode", "The completed exit code is not assigned by this protocol version.");
        }
    }

    private static void ValidateExpectedType(
        string actual,
        string expected,
        List<ContractValidationError> errors)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Add(errors, "protocol.typeMismatch", "$.type", "The event discriminator does not match its payload type.");
        }
    }

    private static void ValidateStringMap(
        IReadOnlyDictionary<string, string>? values,
        string path,
        List<ContractValidationError> errors)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count > 32)
        {
            Add(errors, "protocol.tooManyMapEntries", path, "The map contains too many entries.");
        }

        foreach (var (key, value) in values)
        {
            if (!ContractNameRules.IsValid(key) || value is null || value.Length > 256 || value.Any(char.IsControl))
            {
                Add(errors, "protocol.invalidMapEntry", path, "The map contains an invalid key or value.");
                break;
            }
        }
    }

    private static void ValidateRequiredMessage(
        string? message,
        string path,
        List<ContractValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 4_096 || message.Any(character => character is '\r' or '\n'))
        {
            Add(errors, "protocol.invalidMessage", path, "The message must be a single non-empty line no longer than 4096 characters.");
        }
    }

    private static void ValidateOptionalMessage(
        string? message,
        string path,
        List<ContractValidationError> errors)
    {
        if (message is not null)
        {
            ValidateRequiredMessage(message, path, errors);
        }
    }

    private static void AddMissingPayload(List<ContractValidationError> errors) =>
        Add(errors, "protocol.missingPayload", "$.payload", "The event payload is required.");

    private static void Add(List<ContractValidationError> errors, string code, string path, string message) =>
        errors.Add(new ContractValidationError(code, path, message));
}
