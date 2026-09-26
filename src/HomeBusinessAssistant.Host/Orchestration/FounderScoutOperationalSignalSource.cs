using FounderScout.Application;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Operations;

namespace HomeBusinessAssistant.Host.Orchestration;

/// <summary>Projects safe Founder Scout-owned state into the central operational detector.</summary>
public sealed class FounderScoutOperationalSignalSource(IFounderScoutResultsQuery query, TimeProvider timeProvider) : IOperationalSignalSource
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(CancellationToken cancellationToken = default)
    {
        FounderScoutDashboard dashboard = await query.GetDashboardAsync(timeProvider.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
        var signals = new List<OperationalSignal>();
        foreach (BrowserAccount account in dashboard.Accounts.Where(item => item.LastErrorReasonCode is not null || item.SessionStatus != BrowserSessionStatus.Healthy))
        {
            string code = account.LastErrorReasonCode ?? account.SessionStatus.ToString();
            (string category, AttentionSeverity severity) = code.ToLowerInvariant() switch
            {
                string value when value.Contains("challenge", StringComparison.Ordinal) || value.Contains("captcha", StringComparison.Ordinal) => ("challenge", AttentionSeverity.Critical),
                string value when value.Contains("thrott", StringComparison.Ordinal) => ("throttling", AttentionSeverity.Warning),
                string value when value.Contains("parser", StringComparison.Ordinal) => ("parser-health", AttentionSeverity.Error),
                string value when value.Contains("access", StringComparison.Ordinal) => ("access-denied", AttentionSeverity.Error),
                _ => ("authentication", AttentionSeverity.Error),
            };
            signals.Add(new(category, severity, $"Founder Scout account {account.DisplayName} needs attention",
                $"The account is {account.SessionStatus}; reason code {code}.", "founder-scout", null, null,
                $"founder-scout:account:{account.Id}:{category}", "/FounderScout/Operations", new { account.Id, account.SessionStatus, reasonCode = code }));
        }

        if (dashboard.DraftsNeedReview > 0)
        {
            signals.Add(new("draft-review", AttentionSeverity.Warning, "Founder Scout drafts need review",
                $"{dashboard.DraftsNeedReview} invitation draft(s) require manual review.", "founder-scout", null, null,
                "founder-scout:drafts-needs-review", "/FounderScout/Candidates?needsManualReview=true", new { dashboard.DraftsNeedReview }));
        }

        int strong = dashboard.Recommendations.TryGetValue("StrongConnect", out int count) ? count : 0;
        if (strong > 0)
        {
            signals.Add(new("founder-opportunity", AttentionSeverity.Info, "Founder Scout found strong-connect candidates",
                $"{strong} candidate(s) currently meet the Strong Connect recommendation.", "founder-scout", null, null,
                "founder-scout:strong-connect", "/FounderScout/Candidates?recommendations=StrongConnect", new { strong }));
        }

        if (dashboard.PrimaryQueue + dashboard.ReserveQueue >= 20)
        {
            signals.Add(new("queue-threshold", AttentionSeverity.Info, "Founder Scout invitation queue is ready for review",
                $"{dashboard.PrimaryQueue + dashboard.ReserveQueue} candidates are in the manual invitation queue.", "founder-scout", null, null,
                "founder-scout:queue-threshold:20", "/FounderScout/InvitationQueue", new { dashboard.PrimaryQueue, dashboard.ReserveQueue }));
        }

        return signals;
    }
}
