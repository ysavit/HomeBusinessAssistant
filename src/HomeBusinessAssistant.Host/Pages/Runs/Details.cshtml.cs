using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Runs;

/// <summary>Displays and controls one durable run.</summary>
[AutoValidateAntiforgeryToken]
public sealed class DetailsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the bounded run detail.</summary>
    public ManagementRunDetail? Detail { get; private set; }

    /// <summary>Loads one run.</summary>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Queries is null)
        {
            return NotFound();
        }

        Detail = await management.Queries.GetRunAsync(AgentRunId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        return Detail is null ? NotFound() : Page();
    }

    /// <summary>Returns a bounded active-status polling payload.</summary>
    public async Task<IActionResult> OnGetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Queries is null)
        {
            return NotFound();
        }

        ManagementRunDetail? detail = await management.Queries.GetRunAsync(AgentRunId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return NotFound();
        }

        bool active = detail.Run.Run.Status is AgentRunStatus.Starting or AgentRunStatus.Running;
        return new JsonResult(new { status = detail.Run.Run.Status.ToString(), active, heartbeatAtUtc = detail.Run.Run.LastHeartbeatAtUtc });
    }

    /// <summary>Requests cancellation of eligible work.</summary>
    public async Task<IActionResult> OnPostCancelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        bool changed = await management.Commands.CancelRunAsync(AgentRunId.FromGuid(id), "local-web", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = changed ? "Cancellation was requested. Runner will terminate the child tree after its grace period if needed." : "The run was no longer eligible for cancellation.";
        return RedirectToPage(new { id });
    }

    /// <summary>Creates and dispatches an explicit retry occurrence.</summary>
    public async Task<IActionResult> OnPostRetryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        var result = await management.Commands.RetryRunAsync(AgentRunId.FromGuid(id), "local-web", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted ? $"Retry occurrence {result.OccurrenceId} was started." : $"Retry occurrence {result.OccurrenceId} is {result.Code}.";
        return RedirectToPage("/Runs/Index");
    }

    /// <summary>Downloads an artifact only after persisted run ownership and root checks.</summary>
    public async Task<IActionResult> OnGetArtifactAsync(Guid id, Guid artifactId, CancellationToken cancellationToken)
    {
        if (management.Queries is null || management.Artifacts is null)
        {
            return NotFound();
        }

        ManagementRunDetail? detail = await management.Queries.GetRunAsync(AgentRunId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        var metadata = detail?.Artifacts.SingleOrDefault(item => item.Id == artifactId && item.RunId.Value == id);
        if (metadata is null)
        {
            return NotFound();
        }

        Stream content = await management.Artifacts.OpenReadAsync(artifactId, cancellationToken).ConfigureAwait(false);
        return File(content, metadata.ContentType, Path.GetFileName(metadata.FileName));
    }
}
