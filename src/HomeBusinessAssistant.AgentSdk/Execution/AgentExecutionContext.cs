using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>The validated invocation contract supplied by the central runner.</summary>
public sealed record AgentExecutionContext(
    AgentRunId RunId,
    OccurrenceId OccurrenceId,
    AgentId AgentId,
    string CommandName,
    string ConfigurationFilePath,
    string DataDirectory,
    string ArtifactDirectory,
    AgentProtocolVersion ProtocolVersion);
