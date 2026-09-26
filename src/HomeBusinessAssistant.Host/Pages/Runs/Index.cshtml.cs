using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Runs;

/// <summary>Provides server-side filtered and paged run history.</summary>
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the run page.</summary>
    public ManagementPage<ManagementRunItem> Runs { get; private set; } = new([], 1, 25, 0);

    /// <summary>Gets installed agents for filtering.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Gets the selected agent filter.</summary>
    public string? AgentId { get; private set; }

    /// <summary>Gets the selected status filter.</summary>
    public AgentRunStatus? Status { get; private set; }

    /// <summary>Gets the selected trigger filter.</summary>
    public TriggerType? Trigger { get; private set; }

    /// <summary>Gets whether failures/attention only are selected.</summary>
    public bool AttentionOnly { get; private set; }

    /// <summary>Gets whether active only is selected.</summary>
    public bool ActiveOnly { get; private set; }

    /// <summary>Loads a filtered page.</summary>
    public async Task OnGetAsync(
        string? agentId,
        AgentRunStatus? status,
        TriggerType? trigger,
        DateTimeOffset? fromUtc,
        DateTimeOffset? throughUtc,
        bool attentionOnly,
        bool activeOnly,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        AgentId = agentId;
        Status = status;
        Trigger = trigger;
        AttentionOnly = attentionOnly;
        ActiveOnly = activeOnly;
        if (management.Queries is null)
        {
            return;
        }

        Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        Runs = await management.Queries.GetRunsAsync(new(
            agentId,
            status,
            trigger,
            fromUtc,
            throughUtc,
            attentionOnly,
            activeOnly,
            pageNumber,
            25), cancellationToken).ConfigureAwait(false);
    }
}
