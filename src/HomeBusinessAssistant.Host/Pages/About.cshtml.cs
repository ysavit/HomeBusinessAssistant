using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Management;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages;

/// <summary>Displays consistent release, runtime, agent, and migration identity.</summary>
public sealed class AboutModel(HostManagementComposition management) : PageModel
{
    /// <summary>Published build identity.</summary>
    public ProductBuildInfo Build { get; private set; } = new("development", "unavailable", "development", "unknown", "unknown");

    /// <summary>Current central database status.</summary>
    public ManagementDatabaseStatus? Database { get; private set; }

    /// <summary>Installed agent and manifest versions.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Current Founder Scout database migration.</summary>
    public string FounderDatabaseMigration { get; private set; } = "unavailable";

    /// <summary>Loads only bounded operational metadata.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Build = management.Build ?? ProductBuildInfo.Load(typeof(AboutModel).Assembly);
        FounderDatabaseMigration = management.FounderDatabaseMigration ?? "unavailable";
        if (management.Queries is not null)
        {
            Database = await management.Queries.GetDatabaseStatusAsync(cancellationToken).ConfigureAwait(false);
            Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
