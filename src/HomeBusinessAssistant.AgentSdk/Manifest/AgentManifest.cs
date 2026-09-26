using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Manifest;

/// <summary>Describes an independently executable agent and its supported contract surface.</summary>
public sealed record AgentManifest(
    AgentProtocolVersion ManifestVersion,
    AgentId Id,
    string DisplayName,
    string Description,
    AgentVersion Version,
    string Executable,
    IReadOnlyList<string> SupportedCommands,
    IReadOnlyList<string> Capabilities,
    int DefaultTimeoutSeconds,
    ConcurrencyPolicy DefaultConcurrencyPolicy,
    bool SupportsScheduling,
    bool SupportsManualRun,
    bool RequiresInteractiveUserSession,
    AgentProtocolVersion ConfigurationSchemaVersion);
