namespace HomeBusinessAssistant.Domain.Audit;

/// <summary>Stable categories for actors that append platform audit events.</summary>
public enum AuditActorType
{
    /// <summary>A local interactive user.</summary>
    User = 0,
    /// <summary>The platform itself.</summary>
    System = 1,
    /// <summary>The central runner process.</summary>
    Runner = 2,
    /// <summary>An independently executable agent.</summary>
    Agent = 3,
    /// <summary>Stale-run and interrupted-lifecycle recovery.</summary>
    Recovery = 4,
}
