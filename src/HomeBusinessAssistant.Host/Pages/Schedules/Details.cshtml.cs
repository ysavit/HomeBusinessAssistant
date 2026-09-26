using HomeBusinessAssistant.Application.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Schedules;

/// <summary>Shows one schedule, its occurrences, and run history.</summary>
[AutoValidateAntiforgeryToken]
public sealed class DetailsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the schedule detail.</summary>
    public ManagementScheduleDetail? Detail { get; private set; }

    /// <summary>Loads one schedule.</summary>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Queries is null)
        {
            return NotFound();
        }

        Detail = await management.Queries.GetScheduleAsync(id, cancellationToken).ConfigureAwait(false);
        return Detail is null ? NotFound() : Page();
    }

    /// <summary>Runs the schedule command now with an explicit pause-bypass choice.</summary>
    public async Task<IActionResult> OnPostRunAsync(Guid id, bool bypassPause, CancellationToken cancellationToken)
    {
        if (management.Queries is null || management.Commands is null)
        {
            return NotFound();
        }

        ManagementScheduleDetail? detail = await management.Queries.GetScheduleAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return NotFound();
        }

        var schedule = detail.Schedule.Schedule;
        var result = await management.Commands.RunNowAsync(new(
            schedule.AgentId,
            schedule.CommandName,
            schedule.ArgumentsJson,
            schedule.ConcurrencyPolicy,
            schedule.Id,
            bypassPause,
            "local-web",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted ? "Runner started for the manual occurrence." : $"Occurrence is {result.Code}.";
        return RedirectToPage("/Runs/Index", new { agentId = schedule.AgentId.Value });
    }
}
