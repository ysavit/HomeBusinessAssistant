using System.Text;
using System.Text.Json;
using FounderScout.Agent;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Execution;

namespace FounderScout.SimpleCli;

/// <summary>A small interactive entry point over the existing Founder Scout workflow and database.</summary>
public static class SimpleScoutCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs one bounded console command.</summary>
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        TimeProvider timeProvider,
        string? settingsPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (arguments.Count != 1 || arguments[0] is not ("run" or "list"))
        {
            await WriteUsageAsync(error).ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }

        (SimpleScoutSettings? settings, string? settingsError) = await SimpleScoutSettings.LoadAsync(
            settingsPath, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            await error.WriteLineAsync(settingsError).ConfigureAwait(false);
            return AgentExitCode.InvalidConfiguration;
        }

        SimpleScoutSettings configurationSettings = settings!;
        string databasePath = configurationSettings.DatabasePath;
        string agentData = Path.GetDirectoryName(databasePath)!;
        if (arguments[0] == "list")
        {
            if (!File.Exists(databasePath))
            {
                await output.WriteLineAsync($"No Founder Scout database yet. The first run will create {databasePath}").ConfigureAwait(false);
                return AgentExitCode.Success;
            }

            FounderScoutDbContextFactory readOnlyFactory = FounderScoutDbContextFactory.CreateReadOnly(
                databasePath, TimeSpan.FromSeconds(5));
            var results = new FounderScoutResultsService(readOnlyFactory, agentData, timeProvider);
            FounderScoutCandidateResultPage page = await results.QueryCandidatesAsync(
                CandidateQuery(configurationSettings.ListLimit), cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"{page.TotalCount} saved candidates in {databasePath}; showing {page.Items.Count} newest.").ConfigureAwait(false);
            foreach (FounderScoutCandidateListItem item in page.Items)
            {
                string scores = item.FounderQualityScore is null
                    ? "AI pending"
                    : $"quality {item.FounderQualityScore:0.0}, fit {item.OurFitScore:0.0}";
                await output.WriteLineAsync($"{item.CandidateId}  {item.Status,-18}  {scores,-26}  {item.DisplayName}").ConfigureAwait(false);
            }
            return AgentExitCode.Success;
        }

        if (string.IsNullOrWhiteSpace(configurationSettings.ApiKey))
        {
            await error.WriteLineAsync("Set SimpleScout:ApiKey in the local appsettings.json before running Scout.").ConfigureAwait(false);
            return AgentExitCode.InvalidConfiguration;
        }

        FounderScoutConfiguration defaults = FounderScoutDefaults.CreateConfiguration();
        FounderScoutConfiguration configuration = defaults with
        {
            Discovery = defaults.Discovery with
            {
                MaxNewProfilesPerRun = configurationSettings.MaxCandidatesPerRun,
                MaxViewedProfilesPerRun = configurationSettings.MaxCandidatesPerRun * 2,
            },
            Analysis = defaults.Analysis with { BatchSize = configurationSettings.MaxCandidatesPerRun },
            Ai = defaults.Ai with { Deployment = configurationSettings.Model },
            Persona = defaults.Persona with { AdditionalContext = configurationSettings.FounderContext },
        };

        var files = new RunTemporaryFileManager(Path.Combine(agentData, "console-runtime"));
        FounderScoutDatabase database = await FounderScoutDatabaseInitializer.InitializeAsync(
            new(agentData, Path.GetFileName(databasePath)), timeProvider, cancellationToken).ConfigureAwait(false);
        var resultsService = new FounderScoutResultsService(database.ContextFactory, database.DataDirectory, timeProvider);
        await output.WriteLineAsync($"Database: {database.DatabasePath}").ConfigureAwait(false);

        DateTimeOffset searchStartedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await output.WriteLineAsync($"Searching up to {configurationSettings.MaxCandidatesPerRun} new profiles; each capture is saved before analysis.").ConfigureAwait(false);
        int searchExit = await ExecuteAgentAsync("start", configuration, new
        {
            accountId = FounderScoutSimpleMode.BrowserAccountId,
            discoveryDelaySeconds = configurationSettings.DelaySeconds,
        }, null, files, agentData, output, error, timeProvider, cancellationToken).ConfigureAwait(false);
        FounderScoutDashboard afterSearch = await resultsService.GetDashboardAsync(
            timeProvider.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Saved candidates: {afterSearch.Candidates}. Pending screening: {afterSearch.PendingScreening}.").ConfigureAwait(false);
        if (searchExit != AgentExitCode.Success) return searchExit;

        FounderScoutCandidateResultPage touched = await resultsService.QueryCandidatesAsync(
            CandidateQuery(configurationSettings.MaxCandidatesPerRun, searchStartedAtUtc), cancellationToken).ConfigureAwait(false);
        int analyzed = 0;
        foreach (FounderScoutCandidateListItem candidate in touched.Items.Where(
            item => item.LastSeenAtUtc >= searchStartedAtUtc).Take(configurationSettings.MaxCandidatesPerRun))
        {
            if (!await resultsService.QueueCandidateForAnalysisAsync(
                candidate.CandidateId, "simple-scout", cancellationToken).ConfigureAwait(false))
            {
                await output.WriteLineAsync($"Skipped {candidate.CandidateId}: no completed screening yet.").ConfigureAwait(false);
                continue;
            }
            await output.WriteLineAsync($"Analyzing saved candidate {candidate.CandidateId} ({++analyzed}/{configurationSettings.MaxCandidatesPerRun}).").ConfigureAwait(false);
            int targetedExit = await ExecuteAgentAsync("analyze", configuration,
                new { phase = "deep", max = 1, candidateId = candidate.CandidateId }, configurationSettings.ApiKey,
                files, agentData, output, error, timeProvider, cancellationToken).ConfigureAwait(false);
            if (targetedExit != AgentExitCode.Success)
            {
                await output.WriteLineAsync("Saved profiles remain available in the database. Correct the AI configuration and run again.").ConfigureAwait(false);
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

    private static Task WriteUsageAsync(TextWriter error) => error.WriteLineAsync(
        "Usage: SimpleScout run|list\nConfigure database, API key, model, and limits in appsettings.json.");

    private static FounderScoutCandidateQuery CandidateQuery(int maximum, DateTimeOffset? changedFromUtc = null) => new(
        Search: null, Recommendations: null, Statuses: null, MinimumScore: null,
        MaximumScore: null, MinimumConfidence: null, TechnicalStatus: null,
        CommitmentStatus: null, IdeaStatus: null, HasTractionEvidence: null,
        RiskKey: null, ChangedFromUtc: changedFromUtc, ChangedToUtc: null, ActiveSinceUtc: null,
        QueueKind: null, AccountId: null, SegmentId: null, NeedsManualReview: null,
        Sort: FounderScoutCandidateSort.LastCapturedDescending, Offset: 0,
        PageSize: maximum);

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
