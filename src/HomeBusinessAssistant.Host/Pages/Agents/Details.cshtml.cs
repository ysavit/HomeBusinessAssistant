using System.Text.Json;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Agents;

/// <summary>Shows one installed agent and invokes durable management use cases.</summary>
[AutoValidateAntiforgeryToken]
public sealed class DetailsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the bounded agent detail.</summary>
    public ManagementAgentDetail? Detail { get; private set; }

    /// <summary>Gets parsed manifest capabilities.</summary>
    public IReadOnlyList<string> Capabilities { get; private set; } = [];

    /// <summary>Gets parsed manifest command names.</summary>
    public IReadOnlyList<string> Commands { get; private set; } = [];

    /// <summary>Gets safe current executable presence when production bootstrap paths are available.</summary>
    public bool? ExecutablePresent { get; private set; }

    /// <summary>Gets the executable hash captured by the latest durable run, if any.</summary>
    public string? LastExecutableHash => Detail is { RecentRuns.Count: > 0 }
        ? Detail.RecentRuns[0].ExecutableHash
        : null;

    /// <summary>Gets whether this agent currently requires onboarding review.</summary>
    public bool IsOnboardingPending { get; private set; }

    /// <summary>Loads one installed agent.</summary>
    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        if (management.Queries is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        Detail = await management.Queries.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (Detail is null)
        {
            return NotFound();
        }

        Capabilities = ParseStringArray(Detail.Agent.Definition.CapabilitiesJson);
        Commands = ParseStringArray(Detail.Agent.Definition.SupportedCommandsJson);
        ExecutablePresent = GetExecutablePresence(Detail.Agent.Definition.ExecutableRelativePath);
        if (management.AgentOnboarding is not null)
        {
            AgentOnboardingDashboardStatus status = await management.AgentOnboarding.GetDashboardStatusAsync(cancellationToken).ConfigureAwait(false);
            IsOnboardingPending = status.PendingAgentIds.Contains(agentId);
        }
        return Page();
    }

    /// <summary>Creates and dispatches one durable manual occurrence.</summary>
    public async Task<IActionResult> OnPostRunAsync(string id, string command, CancellationToken cancellationToken)
    {
        if (management.Commands is null || management.Queries is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        ManagementAgentDetail? detail = await management.Queries.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return NotFound();
        }

        var result = await management.Commands.RunNowAsync(new(
            agentId,
            command,
            "{}",
            detail.Agent.Definition.DefaultConcurrencyPolicy,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            "local-web",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted
            ? $"Runner started for occurrence {result.OccurrenceId}."
            : $"Occurrence {result.OccurrenceId} is {result.Code}.";
        return RedirectToPage("/Runs/Index", new { agentId = id });
    }

    /// <summary>Changes agent enablement without killing active work.</summary>
    public async Task<IActionResult> OnPostSetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        if (management.Commands is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        _ = await management.Commands.SetAgentEnabledAsync(agentId, enabled, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"{id} is now {(enabled ? "enabled" : "disabled")}.";
        return RedirectToPage(new { id });
    }

    /// <summary>Starts or joins agent setup without resetting configuration or schedules.</summary>
    public async Task<IActionResult> OnPostSetupAsync(string id, CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        try
        {
            OnboardingSession session = await management.AgentOnboarding.StartReconfigureAgentAsync(
                agentId,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Onboarding/Agents", new { sessionId = session.Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage(new { id });
        }
    }

    private static string[] ParseStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private bool? GetExecutablePresence(string relativePath)
    {
        if (management.Bootstrap is null)
        {
            return null;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(management.Bootstrap.AgentDirectory));
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return System.IO.File.Exists(candidate)
                && (System.IO.File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
