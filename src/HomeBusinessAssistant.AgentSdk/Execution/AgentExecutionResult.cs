using System.Text.Json;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>The bounded result returned by an operation run through an execution session.</summary>
public sealed record AgentExecutionResult(
    int ExitCode,
    AgentRunStatus Status,
    string Summary,
    JsonElement? Data = null);
