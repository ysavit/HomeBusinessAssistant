using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Infrastructure.Files;

/// <summary>Writes deterministic, encoded Founder Scout reports to durable and Runner staging roots.</summary>
public sealed class FounderScoutReportStore : IFounderScoutReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string dataDirectory;
    private readonly string reportsDirectory;
    private readonly string runnerArtifactDirectory;

    /// <summary>Creates one report store for a Runner invocation.</summary>
    public FounderScoutReportStore(string dataDirectory, string reportsDirectory, string runnerArtifactDirectory)
    {
        this.dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        this.reportsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportsDirectory));
        this.runnerArtifactDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runnerArtifactDirectory));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FounderScoutReportFile>> WriteAsync(
        Guid runId,
        string reportType,
        FounderScoutReportModel model,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty || reportType is not ("all" or "top-candidates" or "invitation-queue"))
        {
            throw new ArgumentException("The report output request is invalid.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(model);
        Directory.CreateDirectory(reportsDirectory);
        Directory.CreateDirectory(runnerArtifactDirectory);
        FounderScoutPathPolicy.RejectReparsePoint(reportsDirectory);
        FounderScoutPathPolicy.RejectReparsePoint(runnerArtifactDirectory);
        string stem = $"{model.GeneratedAtUtc:yyyyMMdd-HHmmss}-{runId:N}";
        var files = new List<FounderScoutReportFile>(6);

        if (reportType is "all" or "top-candidates")
        {
            files.Add(await WritePairAsync(stem, "top-candidates.html", "top-candidates", "html", "text/html", Encoding.UTF8.GetBytes(BuildHtml(model)), model.Candidates.Count, cancellationToken).ConfigureAwait(false));
            files.Add(await WritePairAsync(stem, "top-candidates.md", "top-candidates", "markdown", "text/markdown", Encoding.UTF8.GetBytes(BuildTopCandidatesMarkdown(model)), model.Candidates.Count, cancellationToken).ConfigureAwait(false));
            files.Add(await WritePairAsync(stem, "candidates.csv", "top-candidates", "csv", "text/csv", Encoding.UTF8.GetBytes(BuildCsv(model)), model.Candidates.Count, cancellationToken).ConfigureAwait(false));
            files.Add(await WritePairAsync(stem, "candidates.json", "top-candidates", "json", "application/json", BuildJson(model), model.Candidates.Count, cancellationToken).ConfigureAwait(false));
        }

        if (reportType == "all")
        {
            files.Add(await WritePairAsync(stem, "discovery-summary.md", "discovery-summary", "markdown", "text/markdown", Encoding.UTF8.GetBytes(BuildDiscoveryMarkdown(model)), model.Candidates.Count, cancellationToken).ConfigureAwait(false));
        }

        if (reportType is "all" or "invitation-queue")
        {
            int queueRows = (model.InvitationQueue?.Primary.Count ?? 0) + (model.InvitationQueue?.Reserve.Count ?? 0);
            files.Add(await WritePairAsync(stem, "manual-invitation-queue.md", "invitation-queue", "markdown", "text/markdown", Encoding.UTF8.GetBytes(BuildQueueMarkdown(model)), queueRows, cancellationToken).ConfigureAwait(false));
        }

        return files;
    }

    private async ValueTask<FounderScoutReportFile> WritePairAsync(
        string stem,
        string fileName,
        string reportType,
        string format,
        string contentType,
        byte[] content,
        int rowCount,
        CancellationToken cancellationToken)
    {
        string durableFileName = $"{Path.GetFileNameWithoutExtension(fileName)}-{stem}{Path.GetExtension(fileName)}";
        string durablePath = FounderScoutPathPolicy.CombineContained(reportsDirectory, durableFileName);
        string runnerPath = FounderScoutPathPolicy.CombineContained(runnerArtifactDirectory, fileName);
        await WriteAtomicAsync(durablePath, content, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(runnerPath, content, cancellationToken).ConfigureAwait(false);
        return new(
            reportType,
            format,
            contentType,
            FounderScoutPathPolicy.GetRelativeContainedPath(dataDirectory, durablePath),
            fileName,
            Convert.ToHexStringLower(SHA256.HashData(content)),
            content.LongLength,
            rowCount);
    }

    private static async ValueTask WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The report path has no parent directory.");
        string temporary = FounderScoutPathPolicy.CombineContained(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string BuildHtml(FounderScoutReportModel model)
    {
        var builder = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Founder Scout top candidates</title><style>body{font:16px system-ui;line-height:1.5;color:#17231f;background:#f5f3ed;margin:0}main{max-width:1080px;margin:auto;padding:32px}article{background:white;border:1px solid #d8ddd9;border-radius:14px;padding:24px;margin:18px 0}h1,h2{font-family:Georgia,serif}dt{font-weight:700}dd{margin:0 0 10px}.score{display:flex;gap:18px;flex-wrap:wrap}.score span{background:#eaf1ed;padding:6px 10px;border-radius:999px}pre{white-space:pre-wrap;background:#f7f8f6;padding:12px;border-radius:8px}a{color:#155a44}</style></head><body><main>");
        _ = builder.Append("<h1>Founder Scout top candidates</h1><p>Generated UTC: <time>").Append(Html(model.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append("</time><br>Generated local: <time>").Append(Html(model.GeneratedAtLocal.ToString("O", CultureInfo.InvariantCulture))).Append("</time><br>Scorecards: ").Append(Html(model.ScorecardVersions)).Append("</p>");
        foreach (FounderScoutReportCandidate row in model.Candidates)
        {
            _ = builder.Append("<article><h2>#").Append(row.Candidate.Rank).Append(' ').Append(Html(row.Candidate.DisplayName)).Append("</h2><div class=\"score\"><span>Quality ").Append(Decimal(row.Candidate.FounderQualityScore)).Append("</span><span>Fit ").Append(Decimal(row.Candidate.OurFitScore)).Append("</span><span>Confidence ").Append(Decimal(row.Candidate.Confidence)).Append("</span><span>Priority ").Append(Decimal(row.Candidate.InvitationPriority)).Append("</span></div><p>").Append(Html(row.Summary)).Append("</p>");
            AppendHtmlList(builder, "Why connect", row.PositiveSignals);
            AppendHtmlList(builder, "Concerns", row.Risks.Select(risk => $"{risk.Key}: {risk.Reason ?? risk.EvidenceJson}"));
            AppendHtmlList(builder, "Questions", row.PriorityQuestions);
            if (!string.IsNullOrWhiteSpace(row.ShortDraft)) _ = builder.Append("<h3>Short introduction draft</h3><pre>").Append(Html(row.ShortDraft)).Append("</pre>");
            if (!string.IsNullOrWhiteSpace(row.DetailedDraft)) _ = builder.Append("<h3>Detailed introduction draft</h3><pre>").Append(Html(row.DetailedDraft)).Append("</pre>");
            if (Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out Uri? source) && source.Scheme is "https" or "http") _ = builder.Append("<p><a rel=\"noopener noreferrer\" href=\"").Append(Html(source.AbsoluteUri)).Append("\">Open source profile</a></p>");
            _ = builder.Append("</article>");
        }

        _ = builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private static void AppendHtmlList(StringBuilder builder, string title, IEnumerable<string> values)
    {
        string[] items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (items.Length == 0) return;
        _ = builder.Append("<h3>").Append(Html(title)).Append("</h3><ul>");
        foreach (string value in items) _ = builder.Append("<li>").Append(Html(value)).Append("</li>");
        _ = builder.Append("</ul>");
    }

    private static string BuildTopCandidatesMarkdown(FounderScoutReportModel model)
    {
        var builder = new StringBuilder("# Founder Scout top candidates\n\n");
        _ = builder.Append("- Schema: `").Append(Markdown(model.SchemaVersion)).Append("`\n- Generated UTC: `").Append(model.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("`\n- Generated local: `").Append(model.GeneratedAtLocal.ToString("O", CultureInfo.InvariantCulture)).Append("`\n- Filter/order hash: `").Append(model.FilterSortHash).Append("`\n- Scorecards: ").Append(Markdown(model.ScorecardVersions)).Append("\n- Reviewed count: ").Append(model.ReviewedCount).Append("\n\n");
        foreach (FounderScoutReportCandidate row in model.Candidates)
        {
            _ = builder.Append("## ").Append(row.Candidate.Rank).Append(". ").Append(Markdown(row.Candidate.DisplayName)).Append("\n\n").Append(Markdown(row.Summary)).Append("\n\n");
            _ = builder.Append("- Status: ").Append(Markdown(row.Candidate.Status.ToString())).Append("\n- Recommendation: ").Append(Markdown(row.Candidate.Recommendation ?? "Unscored")).Append("\n- Founder quality: ").Append(Decimal(row.Candidate.FounderQualityScore)).Append("\n- Local fit: ").Append(Decimal(row.Candidate.OurFitScore)).Append("\n- Confidence: ").Append(Decimal(row.Candidate.Confidence)).Append("\n- Invitation priority: ").Append(Decimal(row.Candidate.InvitationPriority)).Append("\n\n");
            AppendMarkdownList(builder, "Why connect", row.PositiveSignals);
            AppendMarkdownList(builder, "Concerns", row.Risks.Select(risk => $"{risk.Key}: {risk.Reason ?? risk.EvidenceJson}"));
            AppendMarkdownList(builder, "Questions", row.PriorityQuestions);
            if (!string.IsNullOrWhiteSpace(row.ShortDraft)) _ = builder.Append("### Short introduction draft\n\n> ").Append(Markdown(row.ShortDraft).Replace("\n", "\n> ", StringComparison.Ordinal)).Append("\n\n");
        }

        AppendMarkdownDistribution(builder, "Common positive reasons", model.PositiveReasonDistribution);
        AppendMarkdownDistribution(builder, "Common risk reasons", model.RiskReasonDistribution);
        _ = builder.Append("## Attention and manual review\n\n");
        foreach (FounderScoutCandidateListItem item in model.Attention) _ = builder.Append("- ").Append(Markdown(item.DisplayName)).Append(" — ").Append(Markdown(item.Status.ToString())).Append('\n');
        return builder.ToString();
    }

    private static void AppendMarkdownList(StringBuilder builder, string title, IEnumerable<string> values)
    {
        string[] items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (items.Length == 0) return;
        _ = builder.Append("### ").Append(title).Append("\n\n");
        foreach (string value in items) _ = builder.Append("- ").Append(Markdown(value)).Append('\n');
        _ = builder.AppendLine();
    }

    private static void AppendMarkdownDistribution(StringBuilder builder, string title, IReadOnlyDictionary<string, int> values)
    {
        _ = builder.Append("## ").Append(title).Append("\n\n");
        foreach (KeyValuePair<string, int> item in values.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)) _ = builder.Append("- ").Append(Markdown(item.Key)).Append(": ").Append(item.Value).Append('\n');
        _ = builder.AppendLine();
    }

    private static string BuildCsv(FounderScoutReportModel model)
    {
        var builder = new StringBuilder("rank,candidate_id,name,status,recommendation,founder_quality,local_fit,confidence,activity,risk_penalty,invitation_priority,queue,queue_position,summary\r\n");
        foreach (FounderScoutReportCandidate row in model.Candidates)
        {
            string[] fields =
            [
                row.Candidate.Rank.ToString(CultureInfo.InvariantCulture), row.Candidate.CandidateId.ToString("D"), row.Candidate.DisplayName,
                row.Candidate.Status.ToString(), row.Candidate.Recommendation ?? string.Empty, Decimal(row.Candidate.FounderQualityScore), Decimal(row.Candidate.OurFitScore),
                Decimal(row.Candidate.Confidence), Decimal(row.Candidate.ActivityScore), Decimal(row.Candidate.RiskPenalty), Decimal(row.Candidate.InvitationPriority),
                row.QueueKind?.ToString() ?? string.Empty, row.QueuePosition?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, OneLine(row.Summary),
            ];
            _ = builder.Append(string.Join(',', fields.Select(Csv))).Append("\r\n");
        }

        return builder.ToString();
    }

    private static byte[] BuildJson(FounderScoutReportModel model) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = "1.0",
        reportType = "top-candidates",
        model.GeneratedAtUtc,
        model.GeneratedAtLocal,
        model.FilterSortHash,
        model.ScorecardVersions,
        model.ReviewedCount,
        candidates = model.Candidates.Select(row => new
        {
            rank = row.Candidate.Rank,
            candidateId = row.Candidate.CandidateId,
            row.Candidate.DisplayName,
            status = row.Candidate.Status.ToString(),
            row.Candidate.Recommendation,
            row.Candidate.FounderQualityScore,
            row.Candidate.OurFitScore,
            row.Candidate.Confidence,
            row.Candidate.ActivityScore,
            row.Candidate.RiskPenalty,
            row.Candidate.InvitationPriority,
            row.Summary,
            row.PositiveSignals,
            risks = row.Risks.Select(risk => new { risk.Key, risk.Penalty, risk.EvidenceJson, risk.Reason }),
            row.MissingEvidence,
            row.PriorityQuestions,
            row.ShortDraft,
            row.DetailedDraft,
            row.SourceUrl,
            queueKind = row.QueueKind?.ToString(),
            row.QueuePosition,
            row.EvaluationVersion,
        }),
        attention = model.Attention.Select(item => new { item.CandidateId, item.DisplayName, status = item.Status.ToString(), item.NeedsManualReview }),
        model.PositiveReasonDistribution,
        model.RiskReasonDistribution,
    }, JsonOptions);

    private static string BuildDiscoveryMarkdown(FounderScoutReportModel model)
    {
        var builder = new StringBuilder("# Founder Scout discovery summary\n\n");
        _ = builder.Append("- Generated UTC: `").Append(model.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("`\n- Selected/analyzed candidates: ").Append(model.Candidates.Count).Append("\n- Attention required: ").Append(model.Attention.Count).Append("\n\n## Browser accounts\n\n");
        foreach (BrowserAccount account in model.Accounts) _ = builder.Append("- ").Append(Markdown(account.DisplayName)).Append(" — ").Append(account.Enabled ? "enabled" : "paused").Append("; auth ").Append(Markdown(account.SessionStatus.ToString())).Append("; last discovery ").Append(account.LastSuccessfulRunAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "never").Append('\n');
        _ = builder.Append("\n## Discovery segments\n\n");
        foreach (DiscoverySegment segment in model.Segments) _ = builder.Append("- ").Append(Markdown(segment.Name)).Append(" — ").Append(segment.Enabled ? "enabled" : "paused").Append("; viewed ").Append(segment.ViewedCount).Append("; new ").Append(segment.NewCount).Append("; duplicates ").Append(segment.DuplicateCount).Append(';').Append(" errors ").Append(segment.ErrorCount).Append('\n');
        AppendMarkdownDistribution(builder, "Common positive reasons", model.PositiveReasonDistribution);
        AppendMarkdownDistribution(builder, "Common risk reasons", model.RiskReasonDistribution);
        _ = builder.Append("## Top candidates\n\n");
        foreach (FounderScoutReportCandidate row in model.Candidates.Take(10)) _ = builder.Append("- ").Append(row.Candidate.Rank).Append(". ").Append(Markdown(row.Candidate.DisplayName)).Append(" — ").Append(Decimal(row.Candidate.InvitationPriority)).Append('\n');
        return builder.ToString();
    }

    private static string BuildQueueMarkdown(FounderScoutReportModel model)
    {
        var builder = new StringBuilder("# Manual invitation queue\n\n> This is a local planning checklist. Founder Scout never sends invitations or messages.\n\n");
        if (model.InvitationQueue is null)
        {
            _ = builder.AppendLine("No invitation window is available.");
            return builder.ToString();
        }

        _ = builder.Append("- Window: `").Append(model.InvitationQueue.Window.Id.ToString("D")).Append("`\n- Start UTC: `").Append(model.InvitationQueue.Window.StartAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("`\n- End UTC: `").Append(model.InvitationQueue.Window.EndAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("`\n\n");
        AppendQueueLane(builder, "Primary", model.InvitationQueue.Primary);
        AppendQueueLane(builder, "Reserve", model.InvitationQueue.Reserve);
        return builder.ToString();
    }

    private static void AppendQueueLane(StringBuilder builder, string title, IReadOnlyList<FounderScoutInvitationQueueItem> rows)
    {
        _ = builder.Append("## ").Append(title).Append(" queue\n\n");
        foreach (FounderScoutInvitationQueueItem row in rows)
        {
            InvitationDraft? draft = row.GeneratedDraft;
            string? shortDraft = row.ActiveRevision?.ShortDraft ?? draft?.ShortDraft;
            _ = builder.Append("### ").Append(row.Entry.Position).Append(". ").Append(Markdown(row.Candidate.DisplayName)).Append("\n\n- [ ] Review evidence and latest profile\n- [ ] Review and copy the introduction draft\n- [ ] Send manually outside Founder Scout\n- [ ] Record the manual outcome\n- Draft status: ").Append(Markdown(draft?.Status.ToString() ?? "Missing")).Append('\n');
            if (Uri.TryCreate(row.SourceUrl, UriKind.Absolute, out Uri? source) && source.Scheme is "https" or "http") _ = builder.Append("- Source: <").Append(source.AbsoluteUri).Append(">\n");
            if (!string.IsNullOrWhiteSpace(shortDraft)) _ = builder.Append("\n> ").Append(Markdown(shortDraft).Replace("\n", "\n> ", StringComparison.Ordinal)).Append('\n');
            _ = builder.AppendLine();
        }
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Markdown(string? value) => (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string OneLine(string value) => string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private static string Decimal(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Csv(string value)
    {
        string safe = OneLine(value);
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = $"'{safe}";
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
