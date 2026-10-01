using System.Text;
using System.Text.Json;
using FounderScout.Agent;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Execution;
using HomeBusinessAssistant.Windows.Secrets;

namespace FounderScout.SimpleCli;

/// <summary>A small interactive entry point over the existing Founder Scout workflow and database.</summary>
public static class SimpleScoutCommand
{
    private const int DefaultMaximum = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs one bounded console command.</summary>
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!TryParse(arguments, out Options? parsedOptions))
        {
            await WriteUsageAsync(error).ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }
        Options options = parsedOptions!;

        string dataRoot = options.DataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HomeBusinessAssistant", "data");
        string scoutRoot = Path.Combine(dataRoot, "agents", FounderScoutDefaults.AgentId.Value);
        string databasePath = Path.Combine(scoutRoot, "founders.db");
        if (options.Command == "db-path")
        {
            await output.WriteLineAsync(databasePath).ConfigureAwait(false);
            return AgentExitCode.Success;
        }

        if (options.Command == "list")
        {
            if (!File.Exists(databasePath))
            {
                await output.WriteLineAsync($"No Founder Scout database yet. It will be created at {databasePath} after the first scan.").ConfigureAwait(false);
                return AgentExitCode.Success;
            }

            FounderScoutDatabase stored = await FounderScoutDatabaseInitializer.InitializeAsync(
                new(scoutRoot), timeProvider, cancellationToken).ConfigureAwait(false);
            var results = new FounderScoutResultsService(stored.ContextFactory, stored.DataDirectory, timeProvider);
            FounderScoutCandidateResultPage page = await results.QueryCandidatesAsync(
                CandidateQuery(options.Maximum), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"{page.TotalCount} saved candidates in {databasePath}").ConfigureAwait(false);
            foreach (FounderScoutCandidateListItem item in page.Items)
            {
                string scores = item.FounderQualityScore is null
                    ? "AI pending"
                    : $"quality {item.FounderQualityScore:0.0}, fit {item.OurFitScore:0.0}";
                await output.WriteLineAsync($"{item.CandidateId}  {item.Status,-18}  {scores,-26}  {item.DisplayName}").ConfigureAwait(false);
            }
            return AgentExitCode.Success;
        }

        string? apiKey = null;
        if (options.Command is "run" or "analyze")
        {
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var secretStore = new WindowsCurrentUserSecretStore(dataRoot, new ReadOnlyAuditWriter());
                apiKey = await secretStore.GetAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await error.WriteLineAsync("AI key missing. Set OPENAI_API_KEY for this console session or save the protected key in Founder Scout Settings. Use 'scan' to capture without AI.").ConfigureAwait(false);
                return AgentExitCode.InvalidConfiguration;
            }
        }

        FounderScoutConfiguration defaults = FounderScoutDefaults.CreateConfiguration();
        FounderScoutConfiguration configuration = defaults with
        {
            Discovery = defaults.Discovery with
            {
                MaxNewProfilesPerRun = options.Maximum,
                MaxViewedProfilesPerRun = Math.Max(options.Maximum * 2, options.Maximum),
            },
            Analysis = defaults.Analysis with { BatchSize = options.Maximum },
            Ai = defaults.Ai with { Deployment = options.Model },
        };

        var files = new RunTemporaryFileManager(dataRoot);
        string agentData = files.GetAgentDataDirectory(FounderScoutDefaults.AgentId);
        FounderScoutDatabase database = await FounderScoutDatabaseInitializer.InitializeAsync(
            new(agentData), timeProvider, cancellationToken).ConfigureAwait(false);
        var resultsService = new FounderScoutResultsService(database.ContextFactory, database.DataDirectory, timeProvider);
        await output.WriteLineAsync($"Database: {database.DatabasePath}").ConfigureAwait(false);

        if (options.Command is "run" or "scan")
        {
            DateTimeOffset searchStartedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            await output.WriteLineAsync($"Searching up to {options.Maximum} new profiles; each capture is saved before analysis.").ConfigureAwait(false);
            int searchExit = await ExecuteAgentAsync("start", configuration, new
            {
                accountId = FounderScoutSimpleMode.BrowserAccountId,
                discoveryDelaySeconds = options.DelaySeconds,
            }, null, files, agentData, output, error, timeProvider, cancellationToken).ConfigureAwait(false);
            FounderScoutDashboard afterSearch = await resultsService.GetDashboardAsync(
                timeProvider.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"Saved candidates: {afterSearch.Candidates}. Pending screening: {afterSearch.PendingScreening}.").ConfigureAwait(false);
            if (searchExit != AgentExitCode.Success || options.Command == "scan") return searchExit;

            FounderScoutCandidateResultPage touched = await resultsService.QueryCandidatesAsync(
                CandidateQuery(options.Maximum, searchStartedAtUtc), cancellationToken).ConfigureAwait(false);
            int analyzed = 0;
            foreach (FounderScoutCandidateListItem candidate in touched.Items.Where(
                item => item.LastSeenAtUtc >= searchStartedAtUtc).Take(options.Maximum))
            {
                if (!await resultsService.QueueCandidateForAnalysisAsync(
                    candidate.CandidateId, "simple-scout", cancellationToken).ConfigureAwait(false))
                {
                    await output.WriteLineAsync($"Skipped {candidate.CandidateId}: no matching completed screening yet.").ConfigureAwait(false);
                    continue;
                }
                await output.WriteLineAsync($"Analyzing saved candidate {candidate.CandidateId} ({++analyzed}/{options.Maximum}).").ConfigureAwait(false);
                int targetedExit = await ExecuteAgentAsync("analyze", configuration,
                    new { phase = "deep", max = 1, candidateId = candidate.CandidateId }, apiKey,
                    files, agentData, output, error, timeProvider, cancellationToken).ConfigureAwait(false);
                if (targetedExit != AgentExitCode.Success)
                {
                    await output.WriteLineAsync("Saved profiles remain available. Retry AI later with 'analyze'.").ConfigureAwait(false);
                    return targetedExit;
                }
            }
            if (analyzed == 0)
                await output.WriteLineAsync("This scan did not find a newly touched, screened candidate to analyze.").ConfigureAwait(false);
            FounderScoutDashboard completedRun = await resultsService.GetDashboardAsync(
                timeProvider.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"AI evaluations saved: {completedRun.Analyzed}. Pending AI: {completedRun.PendingAnalysis}.").ConfigureAwait(false);
            return AgentExitCode.Success;
        }

        FounderScoutAnalysisQueueResult queued = await resultsService.QueueUnanalyzedForAnalysisAsync(
            options.Maximum, "simple-scout", cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"AI queue: {queued.Queued} newly queued, {queued.AlreadyPending} already pending.").ConfigureAwait(false);
        if (queued.Queued == 0 && queued.AlreadyPending == 0)
        {
            await output.WriteLineAsync("No screened candidates require AI analysis.").ConfigureAwait(false);
            return AgentExitCode.Success;
        }

        int analysisExit = await ExecuteAgentAsync("analyze", configuration,
            new { phase = "deep", max = options.Maximum }, apiKey,
            files, agentData, output, error, timeProvider, cancellationToken).ConfigureAwait(false);
        FounderScoutDashboard afterAnalysis = await resultsService.GetDashboardAsync(
            timeProvider.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"AI evaluations saved: {afterAnalysis.Analyzed}. Pending AI: {afterAnalysis.PendingAnalysis}.").ConfigureAwait(false);
        return analysisExit;
    }

    private static async Task<int> ExecuteAgentAsync(
        string command,
        FounderScoutConfiguration configuration,
        object occurrenceArguments,
        string? apiKey,
        RunTemporaryFileManager files,
        string agentData,
        TextWriter output,
        TextWriter error,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AgentRunId runId = AgentRunId.New();
        string inputPath = await files.WriteExecutionInputAsync(runId,
            JsonSerializer.Serialize(configuration, JsonOptions),
            JsonSerializer.Serialize(occurrenceArguments, JsonOptions),
            apiKey is null ? new Dictionary<string, string>() : new Dictionary<string, string>
            {
                [FounderScoutDefaults.ApiKeyReference.Value] = apiKey,
            }, cancellationToken).ConfigureAwait(false);
        try
        {
            string artifactDirectory = files.CreateArtifactDirectory(runId);
            string[] agentArguments =
            [
                command,
                "--run-id", runId.ToString(),
                "--occurrence-id", OccurrenceId.New().ToString(),
                "--agent-id", FounderScoutDefaults.AgentId.Value,
                "--config-file", inputPath,
                "--data-directory", agentData,
                "--artifact-directory", artifactDirectory,
                "--protocol-version", "1.0",
            ];
            using var progress = new ProgressTextWriter(output);
            return await FounderScoutCommand.ExecuteAsync(
                agentArguments, progress, error, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            files.CleanupRun(runId);
        }
    }

    private static bool TryParse(IReadOnlyList<string> arguments, out Options? options)
    {
        options = null;
        if (arguments.Count == 0 || arguments[0] is not ("run" or "scan" or "analyze" or "list" or "db-path")) return false;
        string command = arguments[0];
        int maximum = DefaultMaximum;
        int delay = 5;
        string model = FounderScoutDefaults.CreateConfiguration().Ai.Deployment;
        string? dataRoot = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 1; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || !seen.Add(arguments[index])) return false;
            string value = arguments[index + 1];
            switch (arguments[index])
            {
                case "--max" when command != "db-path" && int.TryParse(value, out int parsedMaximum) && parsedMaximum is >= 1 and <= 20:
                    maximum = parsedMaximum;
                    break;
                case "--delay" when command is "run" or "scan" && int.TryParse(value, out int parsedDelay) && parsedDelay is >= 1 and <= 60:
                    delay = parsedDelay;
                    break;
                case "--model" when command is "run" or "analyze" && value.Length is >= 1 and <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'):
                    model = value;
                    break;
                case "--data-root" when Path.IsPathFullyQualified(value):
                    dataRoot = Path.GetFullPath(value);
                    break;
                default:
                    return false;
            }
        }
        options = new(command, maximum, delay, model, dataRoot);
        return true;
    }

    private static Task WriteUsageAsync(TextWriter error) => error.WriteLineAsync(
        "Usage: SimpleScout run|scan|analyze|list|db-path [--max 1..20] [--delay 1..60] [--model id] [--data-root absolute-path]\n" +
        "run searches, saves, then analyzes; scan saves without AI; analyze retries saved candidates; list shows saved candidates.");

    private static FounderScoutCandidateQuery CandidateQuery(int maximum, DateTimeOffset? changedFromUtc = null) => new(
        Search: null, Recommendations: null, Statuses: null, MinimumScore: null,
        MaximumScore: null, MinimumConfidence: null, TechnicalStatus: null,
        CommitmentStatus: null, IdeaStatus: null, HasTractionEvidence: null,
        RiskKey: null, ChangedFromUtc: changedFromUtc, ChangedToUtc: null, ActiveSinceUtc: null,
        QueueKind: null, AccountId: null, SegmentId: null, NeedsManualReview: null,
        Sort: FounderScoutCandidateSort.LastCapturedDescending, Offset: 0,
        PageSize: maximum);

    private sealed record Options(string Command, int Maximum, int DelaySeconds, string Model, string? DataRoot);

    private sealed class ReadOnlyAuditWriter : IAuditWriter
    {
        public ValueTask<AuditEventRecord> WriteAsync(WriteAuditEventRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The console only reads the existing protected key; it does not write secret audit records.");
    }

    private sealed class ProgressTextWriter(TextWriter output) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            AgentEventReadResult parsed = AgentEventSerializer.Deserialize(buffer.ToString());
            string? message = parsed.Event switch
            {
                ProgressAgentEvent progress when !string.IsNullOrWhiteSpace(progress.Payload.Message) =>
                    $"[{progress.Payload.Phase ?? "working"}] {progress.Payload.Message}",
                WarningAgentEvent warning => $"Warning {warning.Payload.Code}: {warning.Payload.Message}",
                ErrorAgentEvent failure => $"Error {failure.Payload.Code}: {failure.Payload.Message}",
                SummaryAgentEvent summary => summary.Payload.Text,
                _ => null,
            };
            if (message is not null) await output.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
}
