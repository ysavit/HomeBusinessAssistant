using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Host.Health;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Settings;

/// <summary>Shows read-only bootstrap/system health and creates a non-sensitive diagnostic archive.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management, HostHealthState healthState) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Gets validated bootstrap settings.</summary>
    public HostBootstrapSettings? Bootstrap => management.Bootstrap;

    /// <summary>Gets database health and pragma status.</summary>
    public ManagementDatabaseStatus? Database { get; private set; }

    /// <summary>Gets Host loop health.</summary>
    public IReadOnlyList<HostLoopHealth> Loops => healthState.Loops;

    /// <summary>Gets the effective local operational policy.</summary>
    public OperationalPolicy? Policy => management.Operations?.Policy;

    /// <summary>Gets recent notification delivery history.</summary>
    public IReadOnlyList<LocalNotification> Notifications { get; private set; } = [];

    /// <summary>Gets a conservative retention preview.</summary>
    public RetentionResult? RetentionPreview { get; private set; }

    /// <summary>Gets recent completed database backup sets.</summary>
    public IReadOnlyList<BackupSetInfo> Backups { get; private set; } = [];

    /// <summary>Gets current durable onboarding lifecycle status.</summary>
    public OnboardingEntryDecision? Onboarding { get; private set; }

    /// <summary>Gets non-blocking pending-agent status.</summary>
    public AgentOnboardingDashboardStatus? AgentOnboarding { get; private set; }

    /// <summary>Gets the Host assembly version.</summary>
    public string HostVersion => management.Build?.ProductVersion ?? "development";

    /// <summary>Gets current process uptime.</summary>
    public TimeSpan Uptime => HostProcessInfo.GetUptime(management.TimeProvider);

    /// <summary>Gets whether Runner exists at the configured path.</summary>
    public bool RunnerPresent => Bootstrap is not null && System.IO.File.Exists(Bootstrap.RunnerExecutablePath);

    /// <summary>Loads database status.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Queries is not null)
        {
            Database = await management.Queries.GetDatabaseStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        if (management.Operations is not null)
        {
            Notifications = await management.Operations.GetNotificationsAsync(20, cancellationToken).ConfigureAwait(false);
            RetentionPreview = await management.Operations.ApplyRetentionAsync(true, "local-web", cancellationToken).ConfigureAwait(false);
        }
        if (management.Backups is not null)
        {
            Backups = await management.Backups.ListAsync(10, cancellationToken).ConfigureAwait(false);
        }
        if (management.Onboarding is not null)
        {
            Onboarding = await management.Onboarding.GetEntryDecisionAsync("local-web", cancellationToken).ConfigureAwait(false);
        }
        if (management.AgentOnboarding is not null)
        {
            AgentOnboarding = await management.AgentOnboarding.GetDashboardStatusAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Creates a bounded ZIP containing only non-sensitive status metadata.</summary>
    public async Task<IActionResult> OnGetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (management.Operations is not null)
        {
            DiagnosticArchive archive = await management.Operations.CreateDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            return File(archive.Content, "application/zip", archive.FileName);
        }

        if (management.Queries is null || Bootstrap is null)
        {
            return NotFound();
        }

        ManagementDatabaseStatus database = await management.Queries.GetDatabaseStatusAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ManagementAgentItem> agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        ManagementPage<HomeBusinessAssistant.Application.Persistence.AuditEventRecord> audit = await management.Queries.GetAuditAsync(
            new(null, null, null, null, null, null, null, null, 1, 100),
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset nowUtc = management.TimeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonAsync(archive, "health.json", new
            {
                capturedAtUtc = nowUtc,
                hostVersion = HostVersion,
                uptimeSeconds = (long)Uptime.TotalSeconds,
                machine = Environment.MachineName,
                os = Environment.OSVersion.VersionString,
                url = Bootstrap.Url,
                runnerPresent = RunnerPresent,
                database,
                loops = Loops.Select(item => new { item.Name, item.IsRunning, item.LastAttemptAtUtc, item.LastSuccessAtUtc, item.LastErrorCode }),
            }, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(archive, "agents.json", agents.Select(item => new
            {
                id = item.Definition.Id.Value,
                item.Definition.DisplayName,
                item.Definition.InstalledVersion,
                item.Definition.ManifestVersion,
                item.Definition.Enabled,
                item.ConfigurationRevision,
                item.NextOccurrenceAtUtc,
                item.ActiveRunStatus,
                item.LastRunStatus,
                item.AttentionReason,
            }), cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(archive, "recent-audit.json", audit.Items.Select(item => new
            {
                item.Id,
                item.TimestampUtc,
                item.ActorType,
                item.ActorId,
                item.Action,
                item.TargetType,
                item.TargetId,
                item.Outcome,
                item.CorrelationId,
                runId = item.RunId?.ToString(),
            }), cancellationToken).ConfigureAwait(false);
            await WriteTextAsync(archive, "EXCLUSIONS.txt", "Excluded by design: secret values and DPAPI files; browser profiles, cookies, authentication state, and account paths; raw founder profile data and photographs; configuration JSON; AI prompts and responses; artifact contents; database files; and raw log messages.\n", cancellationToken).ConfigureAwait(false);
        }

        output.Position = 0;
        string fileName = $"home-business-assistant-diagnostics-{nowUtc:yyyyMMdd-HHmmss}.zip";
        return File(output.ToArray(), "application/zip", fileName);
    }

    /// <summary>Applies only the files and metadata shown by the conservative preview.</summary>
    public async Task<IActionResult> OnPostRetentionAsync(CancellationToken cancellationToken)
    {
        if (management.Operations is null) return NotFound();
        RetentionResult result = await management.Operations.ApplyRetentionAsync(false, "local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Retention deleted {result.Deleted}, skipped {result.Skipped}, failed {result.Failed}, and reclaimed {result.BytesReclaimed:N0} bytes.";
        return RedirectToPage();
    }

    /// <summary>Generates a new local-day summary revision on explicit request.</summary>
    public async Task<IActionResult> OnPostDailySummaryAsync(CancellationToken cancellationToken)
    {
        if (management.Operations is null) return NotFound();
        DateTimeOffset localNow = (management.TimeProvider ?? TimeProvider.System).GetLocalNow();
        DailySummary result = await management.Operations.GenerateDailySummaryAsync(DateOnly.FromDateTime(localNow.Date), TimeZoneInfo.Local.Id, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Generated daily summary revision {result.GenerationNumber} for {result.LocalDate}.";
        return RedirectToPage();
    }

    /// <summary>Creates one verified online backup set of both application databases.</summary>
    public async Task<IActionResult> OnPostBackupAsync(CancellationToken cancellationToken)
    {
        if (management.Backups is null) return NotFound();
        BackupCreateResult result = await management.Backups.CreateAsync("local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Backup {result.BackupSet.BackupSetId} completed: {result.BackupSet.SizeBytes:N0} bytes; {result.RemovedByRetention} old set(s) removed.";
        return RedirectToPage();
    }

    /// <summary>Starts or joins the explicit add/configure agents workflow.</summary>
    public async Task<IActionResult> OnPostAgentOnboardingAsync(CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            OnboardingSession session = await management.AgentOnboarding.StartAddAgentsAsync(
                "local-web",
                cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Onboarding/Agents", new { sessionId = session.Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage();
        }
    }

    private static async Task WriteJsonAsync(ZipArchive archive, string name, object value, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(ZipArchive archive, string name, string value, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using Stream stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        await writer.WriteAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
