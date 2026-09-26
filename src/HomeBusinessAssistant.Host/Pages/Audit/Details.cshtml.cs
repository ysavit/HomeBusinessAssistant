using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Audit;

/// <summary>Displays one immutable audit event with redacted structured data.</summary>
public sealed class DetailsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the immutable audit event.</summary>
    public AuditEventRecord? Event { get; private set; }

    /// <summary>Gets redacted structured JSON.</summary>
    public string RedactedData { get; private set; } = "{}";

    /// <summary>Loads one audit event.</summary>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Queries is null)
        {
            return NotFound();
        }

        Event = await management.Queries.GetAuditEventAsync(id, cancellationToken).ConfigureAwait(false);
        if (Event is null)
        {
            return NotFound();
        }

        using JsonDocument document = JsonDocument.Parse(Event.DataJson);
        RedactedData = AuditRedactor.Redact(document.RootElement);
        return Page();
    }
}
