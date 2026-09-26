namespace HomeBusinessAssistant.Domain.Audit;

/// <summary>Stable outcomes for append-only audit events.</summary>
public enum AuditOutcome
{
    /// <summary>The audited action completed successfully.</summary>
    Succeeded = 0,
    /// <summary>The audited action was rejected or failed.</summary>
    Failed = 1,
    /// <summary>The event records information without a success/failure result.</summary>
    Informational = 2,
}
