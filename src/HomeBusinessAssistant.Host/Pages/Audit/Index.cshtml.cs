using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Audit;

/// <summary>Provides immutable server-side filtered audit history.</summary>
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the audit page.</summary>
    public ManagementPage<AuditEventRecord> Events { get; private set; } = new([], 1, 25, 0);

    /// <summary>Gets the current filters.</summary>
    public ManagementAuditQuery Query { get; private set; } = new(null, null, null, null, null, null, null, null);

    /// <summary>Gets installed agents for filtering.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Loads a bounded audit page.</summary>
    public async Task OnGetAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? throughUtc,
        string? actor,
        string? action,
        string? targetType,
        string? outcome,
        string? agentId,
        Guid? runId,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        Query = new(fromUtc, throughUtc, actor, action, targetType, outcome, agentId, runId.HasValue ? AgentRunId.FromGuid(runId.Value) : null, pageNumber, 25);
        if (management.Queries is null)
        {
            return;
        }

        Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        Events = await management.Queries.GetAuditAsync(Query, cancellationToken).ConfigureAwait(false);
    }
}
