using FounderScout.Domain;

namespace FounderScout.Application;

/// <summary>One durable and Runner-staged Founder Scout report artifact.</summary>
public sealed record FounderScoutReportFile(
    string ReportType,
    string Format,
    string ContentType,
    string DurableRelativePath,
    string RunnerRelativePath,
    string FileHash,
    long FileSize,
    int RowCount);

/// <summary>Writes safe deterministic report formats from one canonical report model.</summary>
public interface IFounderScoutReportStore
{
    /// <summary>Writes the selected report family atomically to durable and per-run storage.</summary>
    ValueTask<IReadOnlyList<FounderScoutReportFile>> WriteAsync(
        Guid runId,
        string reportType,
        FounderScoutReportModel model,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of one serialized report generation operation.</summary>
public sealed record FounderScoutReportResult(
    string ReportType,
    FounderScoutReportModel Model,
    IReadOnlyList<FounderScoutReportFile> Files);

/// <summary>Builds and persists consistent reports through a bounded cross-process lease.</summary>
public sealed class FounderScoutReportService(
    IFounderScoutResultsQuery results,
    IFounderScoutReportStore reportStore,
    IReportExportRepository reports,
    IFounderScoutReportLeaseRepository leases,
    TimeProvider timeProvider)
{
    private static readonly HashSet<string> ReportTypes = new(StringComparer.Ordinal)
    {
        "all",
        "top-candidates",
        "invitation-queue",
    };

    private readonly IFounderScoutResultsQuery results = results ?? throw new ArgumentNullException(nameof(results));
    private readonly IFounderScoutReportStore reportStore = reportStore ?? throw new ArgumentNullException(nameof(reportStore));
    private readonly IReportExportRepository reports = reports ?? throw new ArgumentNullException(nameof(reports));
    private readonly IFounderScoutReportLeaseRepository leases = leases ?? throw new ArgumentNullException(nameof(leases));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Creates the requested report family from one canonical selected candidate set.</summary>
    public async ValueTask<FounderScoutReportResult> CreateAsync(
        Guid runId,
        string reportType,
        int top,
        int retentionDays,
        decimal minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty
            || !ReportTypes.Contains(reportType)
            || top is < 1 or > 1_000
            || retentionDays is < 1 or > 3_650
            || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentException("The Founder Scout report request is invalid.", nameof(runId));
        }

        string ownerId = $"report-{runId:N}";
        FounderScoutReportLease? lease = await leases.TryAcquireAsync(ownerId, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            throw new InvalidOperationException("founderScout.report.busy");
        }

        try
        {
            DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            FounderScoutReportModel model = await results.BuildReportModelAsync(top, nowUtc, minimumConfidence, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<FounderScoutReportFile> files = await reportStore.WriteAsync(runId, reportType, model, cancellationToken).ConfigureAwait(false);
            foreach (FounderScoutReportFile file in files)
            {
                await reports.AppendAsync(new ReportExport(
                    Guid.NewGuid(),
                    file.ReportType,
                    file.Format,
                    model.FilterSortHash,
                    file.DurableRelativePath,
                    file.RowCount,
                    file.FileHash,
                    file.FileSize,
                    nowUtc,
                    nowUtc.AddDays(retentionDays)), cancellationToken).ConfigureAwait(false);
            }

            return new(reportType, model, files);
        }
        finally
        {
            await leases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
