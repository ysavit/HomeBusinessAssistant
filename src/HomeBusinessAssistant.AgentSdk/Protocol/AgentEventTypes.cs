namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Stable protocol 1.0 event discriminators.</summary>
public static class AgentEventTypes
{
    /// <summary>The lifecycle start event.</summary>
    public const string Started = "started";
    /// <summary>The liveness heartbeat event.</summary>
    public const string Heartbeat = "heartbeat";
    /// <summary>The progress-reporting event.</summary>
    public const string Progress = "progress";
    /// <summary>The structured metric event.</summary>
    public const string Metric = "metric";
    /// <summary>The resumable checkpoint event.</summary>
    public const string Checkpoint = "checkpoint";
    /// <summary>The artifact metadata event.</summary>
    public const string Artifact = "artifact";
    /// <summary>The non-fatal warning event.</summary>
    public const string Warning = "warning";
    /// <summary>The error diagnostic event.</summary>
    public const string Error = "error";
    /// <summary>The terminal human and structured summary event.</summary>
    public const string Summary = "summary";
    /// <summary>The final process exit-code event.</summary>
    public const string Completed = "completed";
}
