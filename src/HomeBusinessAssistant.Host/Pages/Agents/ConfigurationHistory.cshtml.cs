using System.Text.Json;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Agents;

/// <summary>Displays one immutable revision and a redacted structural diff.</summary>
public sealed class ConfigurationHistoryModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the selected revision.</summary>
    public ConfigurationRevisionRecord? Revision { get; private set; }

    /// <summary>Gets the previous revision when one exists.</summary>
    public ConfigurationRevisionRecord? Previous { get; private set; }

    /// <summary>Gets redacted canonical JSON.</summary>
    public string RedactedJson { get; private set; } = "{}";

    /// <summary>Gets redacted changed top-level field names.</summary>
    public IReadOnlyList<string> ChangedFields { get; private set; } = [];

    /// <summary>Loads one revision owned by the route agent.</summary>
    public async Task<IActionResult> OnGetAsync(string id, Guid revisionId, CancellationToken cancellationToken)
    {
        if (management.Configurations is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        Revision = await management.Configurations.GetRevisionAsync(revisionId, cancellationToken).ConfigureAwait(false);
        if (Revision is null || Revision.AgentId != agentId)
        {
            return NotFound();
        }

        IReadOnlyList<ConfigurationRevisionRecord> history = await management.Configurations.GetHistoryAsync(agentId, cancellationToken).ConfigureAwait(false);
        Previous = history.FirstOrDefault(item => item.RevisionNumber == Revision.RevisionNumber - 1);
        using JsonDocument current = JsonDocument.Parse(Revision.CanonicalConfigurationJson);
        RedactedJson = AuditRedactor.Redact(current.RootElement);
        ChangedFields = GetChangedFields(current.RootElement, Previous?.CanonicalConfigurationJson);
        return Page();
    }

    private static string[] GetChangedFields(JsonElement current, string? previousJson)
    {
        if (previousJson is null)
        {
            return current.EnumerateObject().Select(property => property.Name).Order().ToArray();
        }

        using JsonDocument previous = JsonDocument.Parse(previousJson);
        Dictionary<string, string> oldValues = previous.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        return current.EnumerateObject()
            .Where(property => !oldValues.TryGetValue(property.Name, out string? oldValue)
                || !string.Equals(oldValue, property.Value.GetRawText(), StringComparison.Ordinal))
            .Select(property => property.Name)
            .Concat(oldValues.Keys.Where(name => !current.TryGetProperty(name, out _)))
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();
    }
}
