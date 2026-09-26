using System.Text.Json;
using System.Text.Json.Serialization;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Ai;
using FounderScout.Infrastructure.Browser;
using FounderScout.Infrastructure.Files;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace FounderScout.Agent;

/// <summary>Provides the independently executable Founder Scout command boundary.</summary>
public static class FounderScoutCommand
{
    private static readonly string[] ImplementedPhases = ["browser-discovery", "fixture-import", "profile-processing", "deep-evaluation", "invitation-drafts", "results-ui", "reports"];
    private static readonly int[] DeferredStages = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Executes one Founder Scout command while reserving stdout for protocol JSONL.</summary>
    public static async ValueTask<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (arguments.Count == 1 && string.Equals(arguments[0], "protocol-demo", StringComparison.Ordinal))
        {
            return await AgentProtocolDemo.RunAsync(
                "founder-scout",
                standardOutput,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }

        if (arguments.Count == 1 && arguments[0] is "version" or "--version")
        {
            ProductBuildInfo build = ProductBuildInfo.Load(typeof(FounderScoutCommand).Assembly);
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(build, JsonOptions)).ConfigureAwait(false);
            return AgentExitCode.Success;
        }

        if (!TryExtractCommandOptions(arguments, out string[] standardArguments, out FounderScoutDirectOptions directOptions))
        {
            await WriteUsageAsync(standardError).ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }

        AgentExecutionContextParseResult parsed = AgentExecutionContextParser.Parse(standardArguments);
        if (parsed.Context is not AgentExecutionContext context
            || context.AgentId != FounderScoutDefaults.AgentId
            || context.CommandName is not ("run" or "discover" or "analyze" or "authenticate" or "import" or "report" or "diagnose" or "record-fixture"))
        {
            await WriteUsageAsync(standardError).ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }

        using var writer = new AgentEventWriter(
            standardOutput,
            timeProvider,
            context.RunId,
            context.ProtocolVersion);
        var session = new AgentExecutionSession(writer);
        return await session.ExecuteAsync(
            context.CommandName,
            (events, token) => ExecuteOperationAsync(context, directOptions, events, timeProvider, token),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent"] = "founder-scout",
                ["command"] = context.CommandName,
                ["domainSchema"] = "1.0",
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<AgentExecutionResult> ExecuteOperationAsync(
        AgentExecutionContext context,
        FounderScoutDirectOptions directOptions,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        AgentExecutionInput input;
        try
        {
            input = await AgentExecutionInput.LoadAsync(context.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.executionInput.invalid",
                "The private execution input is missing, invalid, or unsupported.",
                Transient: false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidConfiguration, "Founder Scout rejected the execution input.");
        }

        FounderScoutConfiguration? configuration;
        try
        {
            configuration = input.Configuration.Deserialize<FounderScoutConfiguration>(JsonOptions);
        }
        catch (JsonException)
        {
            configuration = null;
        }

        IReadOnlyList<HomeBusinessAssistant.Application.Configuration.ConfigurationValidationError> errors =
            await new FounderScoutConfigurationValidator().ValidateAsync(
                context.AgentId,
                configuration?.SchemaVersion ?? string.Empty,
                input.Configuration,
                cancellationToken).ConfigureAwait(false);
        if (configuration is null || errors.Count > 0)
        {
            await events.WriteErrorAsync(new(
                errors.Count == 0 ? "founderScout.configuration.invalidShape" : errors[0].Code,
                "Founder Scout configuration validation failed.",
                Transient: false,
                JsonSerializer.SerializeToElement(new
                {
                    errors = errors.Take(20).Select(error => new { error.Code, error.Path }).ToArray(),
                }, JsonOptions)), cancellationToken).ConfigureAwait(false);
            return new(
                AgentExitCode.InvalidConfiguration,
                AgentRunStatus.Failed,
                "Founder Scout configuration validation failed.",
                JsonSerializer.SerializeToElement(new { result = "InvalidConfiguration" }, JsonOptions));
        }

        FounderScoutDatabase database;
        try
        {
            database = await FounderScoutDatabaseInitializer.InitializeAsync(
                new FounderScoutDatabaseSettings(context.DataDirectory),
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.database.initializationFailed",
                "The separate Founder Scout database could not be initialized.",
                Transient: exception is IOException), cancellationToken).ConfigureAwait(false);
            return Failure(
                exception is IOException ? AgentExitCode.TransientFailure : AgentExitCode.PermanentFailure,
                "Founder Scout database initialization failed.");
        }

        var repository = new FounderScoutRepository(database.ContextFactory, timeProvider);
        return context.CommandName switch
        {
            "import" => await ExecuteImportAsync(context, input, directOptions.InputPath, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            "report" => await ExecuteReportAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            "diagnose" => await ExecuteDiagnoseAsync(context, input, directOptions, configuration, database, repository, events, cancellationToken).ConfigureAwait(false),
            "run" => await ExecuteRunAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            "discover" => await ExecuteDiscoverAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            "analyze" => await ExecuteAnalyzeAsync(context, input, directOptions, configuration, database, repository, events, cancellationToken).ConfigureAwait(false),
            "authenticate" => await ExecuteAuthenticateAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            "record-fixture" => await ExecuteRecordFixtureAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The Founder Scout command dispatch is invalid."),
        };
    }

    private static async ValueTask<AgentExecutionResult> ExecuteImportAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        string? directInputPath,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? inputPath = directInputPath ?? ReadInputPath(input.OccurrenceArguments);
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            await events.WriteErrorAsync(new(
                "founderScout.import.inputRequired",
                "Fixture import requires an absolute input path beneath the Founder Scout imports directory.",
                Transient: false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout fixture import requires an input path.");
        }

        var service = new FounderScoutImportService(
            new FounderScoutFixtureFileReader(database.ImportsDirectory, timeProvider),
            new FounderScoutRawArtifactStore(database.DataDirectory, database.SnapshotsDirectory),
            repository,
            repository,
            timeProvider);
        FounderScoutImportResult result;
        try
        {
            await events.WriteProgressAsync(new(
                Current: 0,
                Total: 1,
                Phase: "fixture-import",
                Message: "Validating and importing the bounded local fixture."), cancellationToken).ConfigureAwait(false);
            result = await service.ImportAsync(new(
                inputPath,
                context.RunId.Value,
                context.RunId.Value.ToString("D"),
                configuration.Retention.RawProfileDays), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.import.fixtureInvalid",
                "The local fixture failed bounded format, path, or content validation.",
                Transient: false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout rejected the fixture import.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.import.ioFailure",
                "Founder Scout could not read or commit the local fixture.",
                Transient: exception is IOException), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.TransientFailure, "Founder Scout fixture import encountered an I/O failure.");
        }

        await WriteImportMetricsAsync(events, result, cancellationToken).ConfigureAwait(false);
        await events.WriteProgressAsync(new(
            Current: 1,
            Total: 1,
            Percentage: 100,
            Phase: "fixture-import",
            Message: "Fixture import completed after durable snapshot commits."), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            $"Founder Scout imported {result.SnapshotsCreated} new snapshots and observed {result.DuplicateSnapshots} duplicates.",
            JsonSerializer.SerializeToElement(new
            {
                result = "Completed",
                result.CapturesRead,
                result.CandidatesCreated,
                result.CandidatesMatched,
                result.SnapshotsCreated,
                result.DuplicateSnapshots,
                result.RawArtifactsCreated,
            }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteReportAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var reportStore = new FounderScoutReportStore(
            database.DataDirectory,
            database.ReportsDirectory,
            context.ArtifactDirectory);
        var results = new FounderScoutResultsService(database.ContextFactory, database.DataDirectory, timeProvider);
        var service = new FounderScoutReportService(results, reportStore, repository, results, timeProvider);
        string reportType = (directOptions.ReportType ?? ReadString(input.OccurrenceArguments, "type") ?? "all").ToLowerInvariant();
        int top = directOptions.Top ?? ReadInt(input.OccurrenceArguments, "top") ?? 30;
        FounderScoutReportResult result = await service.CreateAsync(
            context.RunId.Value,
            reportType,
            top,
            configuration.Retention.ReportDays,
            configuration.Ranking.MinimumConfidence,
            cancellationToken).ConfigureAwait(false);
        foreach (FounderScoutReportFile file in result.Files)
        {
            await events.WriteArtifactAsync(new(
                $"founder-scout-{file.ReportType}",
                file.RunnerRelativePath,
                file.ContentType,
                "Encoded Founder Scout decision-support report generated from one canonical ordered candidate set."), cancellationToken).ConfigureAwait(false);
        }

        await events.WriteMetricAsync(new("reports.files", NumericValue: result.Files.Count, Unit: "files"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("reports.candidates", NumericValue: result.Model.Candidates.Count, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            $"Founder Scout wrote {result.Files.Count} safe report artifacts from {result.Model.Candidates.Count} ordered candidates.",
            JsonSerializer.SerializeToElement(new
            {
                result = "Completed",
                result.ReportType,
                schemaVersion = result.Model.SchemaVersion,
                filterSortHash = result.Model.FilterSortHash,
                candidates = result.Model.Candidates.Count,
                artifacts = result.Files.Select(file => new { file.ReportType, file.Format, path = file.RunnerRelativePath, file.FileHash, file.FileSize }).ToArray(),
            }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteAuthenticateAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? accountId = directOptions.AccountId ?? ReadString(input.OccurrenceArguments, "accountId", "account");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            await events.WriteErrorAsync(new("founderScout.browser.accountRequired", "The browser command requires --account or an account occurrence argument.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout browser authentication requires an account ID.");
        }

        StartupSchoolSourceOptions sourceOptions = configuration.StartupSchool ?? StartupSchoolSourceOptions.Default;
        BrowserAccount account;
        try
        {
            account = await EnsureBrowserAccountAsync(accountId, sourceOptions, database, repository, timeProvider, allowCreate: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await events.WriteErrorAsync(new("founderScout.browser.accountInvalid", "The browser account or its dedicated profile path is invalid.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout rejected the browser account.");
        }

        var runtime = new PlaywrightBrowserRuntimeFactory();
        BrowserRuntimeStatus runtimeStatus = await runtime.DiagnoseAsync(sourceOptions.BrowserChannel, cancellationToken).ConfigureAwait(false);
        await WriteRuntimeMetricsAsync(events, runtimeStatus, cancellationToken).ConfigureAwait(false);
        if (!runtimeStatus.IsAvailable)
        {
            await events.WriteErrorAsync(new(runtimeStatus.ReasonCode, runtimeStatus.Message, false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.PermanentFailure, "The explicitly installed browser runtime is unavailable.");
        }

        string profileDirectory = BrowserProfilePath.Resolve(database.DataDirectory, account.Id);
        BrowserSessionOpenResult opened = await new PlaywrightBrowserSessionManager(database.DataDirectory).OpenAsync(new(
            account.Id,
            profileDirectory,
            Headless: false,
            sourceOptions.BrowserChannel,
            sourceOptions.NavigationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            await TransitionAccountStopAsync(repository, account, opened.StopSignal, timeProvider, cancellationToken).ConfigureAwait(false);
            return StopResult(opened.StopSignal, "Founder Scout could not open the dedicated authentication profile.");
        }

        await using IBrowserSession browser = opened.Session!;
        var detector = new PlaywrightBrowserChallengeDetector();
        var source = new StartupSchoolSourceAdapter(detector);
        await events.WriteProgressAsync(new(0, 1, Phase: "manual-authentication", Message: "Complete login, MFA, or an ordinary site challenge in the headed browser. No password is accepted by Founder Scout."), cancellationToken).ConfigureAwait(false);
        BrowserStopSignal signal = await source.WaitForAuthenticationAsync(browser, sourceOptions, cancellationToken).ConfigureAwait(false);
        if (signal.Kind != BrowserStopKind.None)
        {
            await TransitionAccountStopAsync(repository, account, signal, timeProvider, cancellationToken).ConfigureAwait(false);
            return StopResult(signal, "Founder Scout did not observe a healthy authenticated session.");
        }

        if (!account.Enabled || account.SessionStatus == BrowserSessionStatus.Disabled)
        {
            account = await ((IBrowserAccountRepository)repository).UpsertAsync(account with
            {
                Enabled = true,
                SessionStatus = BrowserSessionStatus.Unknown,
                LastErrorReasonCode = null,
                LastErrorMessage = null,
                UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            }, cancellationToken).ConfigureAwait(false);
        }

        await ((IBrowserAccountRepository)repository).TransitionHealthAsync(
            account.Id,
            BrowserSessionStatus.Healthy,
            "browser.authentication.healthy",
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        await events.WriteProgressAsync(new(1, 1, 100, "manual-authentication", "The dedicated browser profile is authenticated."), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            "Founder Scout recorded a healthy manually authenticated browser session.",
            JsonSerializer.SerializeToElement(new { result = "Healthy", accountId = account.Id, runtime = runtimeStatus.RuntimeName }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteDiscoverAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? accountId = directOptions.AccountId ?? ReadString(input.OccurrenceArguments, "accountId", "account");
        string? requestedSegment = directOptions.SegmentId ?? ReadString(input.OccurrenceArguments, "segmentId", "segment");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            await events.WriteErrorAsync(new("founderScout.browser.accountRequired", "Discovery requires one explicit browser account.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout discovery requires an account ID.");
        }

        BrowserAccount? account = await ((IBrowserAccountRepository)repository).GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            await events.WriteErrorAsync(new("founderScout.browser.accountMissing", "Authenticate the dedicated account before discovery.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.AuthenticationRequired, "Founder Scout browser account is not configured.");
        }

        if (account.SessionStatus != BrowserSessionStatus.Healthy)
        {
            await events.WriteWarningAsync(new("founderScout.browser.accountNotHealthy", "The selected account requires manual remediation before discovery."), cancellationToken).ConfigureAwait(false);
            return StopResult(
                new(BrowserStopKind.ReauthenticationRequired, "browser.authentication.required", "The selected account is not in a healthy authenticated state."),
                "Founder Scout requires manual browser authentication.");
        }

        string segmentId = requestedSegment ?? (account.AssignedSegmentIds.Count == 0 ? null : account.AssignedSegmentIds[0]) ?? "default";
        DiscoverySegment? segment = await ((IDiscoverySegmentRepository)repository).GetAsync(segmentId, cancellationToken).ConfigureAwait(false);
        if (segment is null || segment.AssignedAccountId is not null && segment.AssignedAccountId != account.Id)
        {
            await events.WriteErrorAsync(new("founderScout.discovery.segmentInvalid", "The requested discovery segment is missing or assigned to another account.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout rejected the discovery segment.");
        }

        StartupSchoolSourceOptions sourceOptions = configuration.StartupSchool ?? StartupSchoolSourceOptions.Default;
        BrowserRuntimeStatus runtimeStatus = await new PlaywrightBrowserRuntimeFactory().DiagnoseAsync(sourceOptions.BrowserChannel, cancellationToken).ConfigureAwait(false);
        await WriteRuntimeMetricsAsync(events, runtimeStatus, cancellationToken).ConfigureAwait(false);
        if (!runtimeStatus.IsAvailable)
        {
            await events.WriteErrorAsync(new(runtimeStatus.ReasonCode, runtimeStatus.Message, false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.PermanentFailure, "The explicitly installed browser runtime is unavailable.");
        }

        var detector = new PlaywrightBrowserChallengeDetector();
        var diagnosticCapture = new PlaywrightBrowserDiagnosticCapture();
        var service = new FounderScoutDiscoveryService(
            new PlaywrightBrowserSessionManager(database.DataDirectory),
            new StartupSchoolSourceAdapter(detector),
            new PlaywrightProfileCaptureExtractor(detector),
            detector,
            diagnosticCapture,
            new FounderScoutCaptureCommitService(
                new FounderScoutRawArtifactStore(database.DataDirectory, database.SnapshotsDirectory),
                repository,
                repository),
            repository,
            repository,
            repository,
            repository,
            timeProvider);
        await events.WriteProgressAsync(new(0, configuration.Discovery.MaxNewProfilesPerRun, Phase: "browser-discovery", Message: "Starting one sequential bounded discovery segment."), cancellationToken).ConfigureAwait(false);
        FounderScoutDiscoveryResult result;
        try
        {
            result = await service.DiscoverAsync(new(
                account,
                segment,
                configuration.Discovery,
                sourceOptions,
                BrowserProfilePath.Resolve(database.DataDirectory, account.Id),
                context.ArtifactDirectory,
                context.RunId.Value,
                context.RunId.Value.ToString("D"),
                configuration.Retention.RawProfileDays,
                configuration.Retention.ErrorArtifactDays), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.discovery.unexpectedFailure",
                "Founder Scout discovery failed at a bounded internal boundary.",
                Transient: exception is IOException,
                JsonSerializer.SerializeToElement(new { failureKind = exception.GetType().Name }, JsonOptions)), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.UnhandledFailure, "Founder Scout discovery failed unexpectedly.");
        }
        try
        {
            await WriteDiscoveryEventsAsync(events, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await events.WriteErrorAsync(new(
                "founderScout.discovery.protocolFailure",
                "Founder Scout could not publish bounded discovery events.",
                false,
                JsonSerializer.SerializeToElement(new { failureKind = exception.GetType().Name }, JsonOptions)), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.UnhandledFailure, "Founder Scout could not publish its discovery result.");
        }
        if (result.Completion == FounderScoutDiscoveryCompletion.Stopped)
        {
            return StopResult(result.StopSignal, "Founder Scout discovery stopped safely and did not switch accounts.");
        }

        await events.WriteCheckpointAsync(new(
            $"{account.Id}.{segment.Id}",
            JsonSerializer.SerializeToElement(new
            {
                result.Completion,
                result.CompletionReasonCode,
                result.ViewedProfiles,
                result.NewSnapshots,
            }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            $"Founder Scout discovery {result.Completion.ToString().ToLowerInvariant()} after viewing {result.ViewedProfiles} profiles and committing {result.NewSnapshots} snapshots.",
            JsonSerializer.SerializeToElement(new
            {
                result = result.Completion.ToString(),
                result.CompletionReasonCode,
                result.ViewedProfiles,
                result.NewCandidates,
                result.NewSnapshots,
                result.KnownUnchangedProfiles,
                accountId = account.Id,
                segmentId = segment.Id,
                accountFailover = false,
            }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteRecordFixtureAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? accountId = directOptions.AccountId ?? ReadString(input.OccurrenceArguments, "accountId", "account");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Failure(AgentExitCode.InvalidArguments, "Fixture recording requires an explicit account ID.");
        }

        BrowserAccount? existingAccount = await ((IBrowserAccountRepository)repository).GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (existingAccount is null)
        {
            return Failure(AgentExitCode.InvalidArguments, "The browser account does not exist. Run the explicit authenticate command first.");
        }

        BrowserAccount account = await EnsureBrowserAccountAsync(
            accountId,
            configuration.StartupSchool ?? StartupSchoolSourceOptions.Default,
            database,
            repository,
            timeProvider,
            allowCreate: false,
            cancellationToken).ConfigureAwait(false);
        StartupSchoolSourceOptions options = configuration.StartupSchool ?? StartupSchoolSourceOptions.Default;
        BrowserSessionOpenResult opened = await new PlaywrightBrowserSessionManager(database.DataDirectory).OpenAsync(new(
            account.Id,
            BrowserProfilePath.Resolve(database.DataDirectory, account.Id),
            Headless: false,
            options.BrowserChannel,
            options.NavigationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            return StopResult(opened.StopSignal, "Founder Scout could not open the fixture-recording browser.");
        }

        await using IBrowserSession browser = opened.Session!;
        var detector = new PlaywrightBrowserChallengeDetector();
        BrowserStopSignal stop = await new StartupSchoolSourceAdapter(detector).OpenEntryAsync(browser, options, cancellationToken).ConfigureAwait(false);
        if (stop.Kind != BrowserStopKind.None)
        {
            return StopResult(stop, "Fixture recording requires a healthy authenticated page.");
        }

        IReadOnlyList<BrowserDiagnosticArtifact> files = await new PlaywrightBrowserDiagnosticCapture().CaptureAsync(
            browser,
            context.ArtifactDirectory,
            BrowserStopSignal.None,
            configuration.Retention.ErrorArtifactDays,
            cancellationToken).ConfigureAwait(false);
        foreach (BrowserDiagnosticArtifact file in files)
        {
            await events.WriteArtifactAsync(new("browser-sanitized-fixture", file.RelativePath, file.ContentType, "Sanitized developer fixture evidence; forms, images, query values, and session state are excluded."), cancellationToken).ConfigureAwait(false);
        }

        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            "Founder Scout recorded bounded sanitized browser fixture diagnostics for developer review.",
            JsonSerializer.SerializeToElement(new { result = "Recorded", artifacts = files.Select(file => file.RelativePath).ToArray() }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteDiagnoseAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        CancellationToken cancellationToken)
    {
        _ = context;
        FounderScoutDomainCounts counts = await repository.GetCountsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BrowserAccount> accounts = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            await events.WriteWarningAsync(new(
                "founderScout.diagnostics.noBrowserAccounts",
                "No browser accounts are configured; fixture import and reports remain available."), cancellationToken).ConfigureAwait(false);
        }

        await WriteCountMetricsAsync(events, counts, cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "database.journal_mode",
            TextValue: database.Pragmas.JournalMode), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "database.foreign_keys",
            TextValue: database.Pragmas.ForeignKeysEnabled ? "true" : "false"), cancellationToken).ConfigureAwait(false);
        StartupSchoolSourceOptions sourceOptions = configuration.StartupSchool ?? StartupSchoolSourceOptions.Default;
        BrowserRuntimeStatus runtime = await new PlaywrightBrowserRuntimeFactory().DiagnoseAsync(sourceOptions.BrowserChannel, cancellationToken).ConfigureAwait(false);
        await WriteRuntimeMetricsAsync(events, runtime, cancellationToken).ConfigureAwait(false);
        string? accountId = directOptions.AccountId ?? ReadString(input.OccurrenceArguments, "accountId", "account");
        string accountDiagnostic = "not-requested";
        BrowserAccount? diagnosedAccount = null;
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            BrowserAccount? account = await ((IBrowserAccountRepository)repository).GetAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                await events.WriteErrorAsync(new("founderScout.browser.accountMissing", "The requested diagnostic account does not exist.", false), cancellationToken).ConfigureAwait(false);
                return Failure(AgentExitCode.InvalidArguments, "Founder Scout could not diagnose the requested account.");
            }

            string expectedRelative = $"browser/{account.Id}";
            accountDiagnostic = string.Equals(account.BrowserProfileRelativePath.Replace('\\', '/'), expectedRelative, StringComparison.Ordinal)
                ? account.SessionStatus.ToString()
                : "non-canonical-profile";
            if (accountDiagnostic == "non-canonical-profile")
            {
                return Failure(AgentExitCode.InvalidConfiguration, "The browser account profile path is not canonical.");
            }

            diagnosedAccount = account;
        }

        if (runtime.IsAvailable && diagnosedAccount is not null)
        {
            BrowserSessionOpenResult opened = await new PlaywrightBrowserSessionManager(database.DataDirectory).OpenAsync(new(
                diagnosedAccount.Id,
                BrowserProfilePath.Resolve(database.DataDirectory, diagnosedAccount.Id),
                sourceOptions.HeadlessDiscovery,
                sourceOptions.BrowserChannel,
                sourceOptions.NavigationTimeoutSeconds), cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                await TransitionAccountStopAsync(repository, diagnosedAccount, opened.StopSignal, TimeProvider.System, cancellationToken).ConfigureAwait(false);
                return StopResult(opened.StopSignal, "Founder Scout could not acquire and validate the requested browser profile.");
            }

            await using IBrowserSession browser = opened.Session!;
            var detector = new PlaywrightBrowserChallengeDetector();
            BrowserStopSignal stop = await new StartupSchoolSourceAdapter(detector).OpenEntryAsync(browser, sourceOptions, cancellationToken).ConfigureAwait(false);
            if (stop.Kind != BrowserStopKind.None)
            {
                await TransitionAccountStopAsync(repository, diagnosedAccount, stop, TimeProvider.System, cancellationToken).ConfigureAwait(false);
                return StopResult(stop, "Founder Scout diagnosed an unhealthy authenticated browser session.");
            }

            if (diagnosedAccount.SessionStatus != BrowserSessionStatus.Disabled)
            {
                _ = await ((IBrowserAccountRepository)repository).TransitionHealthAsync(
                    diagnosedAccount.Id,
                    BrowserSessionStatus.Healthy,
                    "browser.diagnose.healthy",
                    null,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            accountDiagnostic = "authenticated-healthy";
        }

        if (!runtime.IsAvailable)
        {
            await events.WriteWarningAsync(new(runtime.ReasonCode, runtime.Message), cancellationToken).ConfigureAwait(false);
        }

        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            runtime.IsAvailable
                ? "Founder Scout configuration, storage, browser runtime, and account records are valid."
                : "Founder Scout storage is valid; the explicit browser runtime installation is still required.",
            JsonSerializer.SerializeToElement(new
            {
                result = "Healthy",
                configurationSchema = configuration.SchemaVersion,
                domainSchema = "1.0",
                database = new
                {
                    journalMode = database.Pragmas.JournalMode,
                    database.Pragmas.ForeignKeysEnabled,
                    database.Pragmas.BusyTimeoutMilliseconds,
                    database.Pragmas.SynchronousMode,
                },
                filesystemAuthority = "RunnerAssignedAgentDataDirectory",
                browser = new { runtime.IsAvailable, runtime.RuntimeName, runtime.PlaywrightVersion, runtime.BrowserVersion, runtime.ReasonCode },
                accountDiagnostic,
                browserAccounts = accounts.Count,
                enabledBrowserAccounts = accounts.Count(account => account.Enabled),
                counts.Candidates,
                counts.Snapshots,
            }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteRunAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string? inputPath = directOptions.InputPath ?? ReadInputPath(input.OccurrenceArguments);
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            AgentExecutionResult imported = await ExecuteImportAsync(
                context,
                input,
                inputPath,
                configuration,
                database,
                repository,
                events,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            if (imported.ExitCode != AgentExitCode.Success)
            {
                return imported;
            }

            FounderScoutDirectOptions runOptions = directOptions with
            {
                Phase = directOptions.Phase ?? (CanRunDeepAnalysis(configuration) ? "all" : "screen"),
            };
            AgentExecutionResult processed = await ExecuteAnalyzeAsync(
                context,
                input,
                runOptions,
                configuration,
                database,
                repository,
                events,
                cancellationToken).ConfigureAwait(false);
            if (processed.ExitCode != AgentExitCode.Success)
            {
                return processed;
            }
            return await ExecuteReportAsync(context, input, directOptions, configuration, database, repository, events, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(directOptions.AccountId ?? ReadString(input.OccurrenceArguments, "accountId", "account")))
        {
            AgentExecutionResult discovered = await ExecuteDiscoverAsync(
                context,
                input,
                directOptions,
                configuration,
                database,
                repository,
                events,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
            if (discovered.ExitCode != AgentExitCode.Success)
            {
                return discovered;
            }
            FounderScoutDirectOptions runOptions = directOptions with
            {
                Phase = directOptions.Phase ?? (CanRunDeepAnalysis(configuration) ? "all" : "screen"),
            };
            return await ExecuteAnalyzeAsync(context, input, runOptions, configuration, database, repository, events, cancellationToken).ConfigureAwait(false);
        }

        FounderScoutDirectOptions analysisOptions = directOptions with
        {
            Phase = directOptions.Phase ?? (CanRunDeepAnalysis(configuration) ? "all" : "screen"),
        };
        return await ExecuteAnalyzeAsync(context, input, analysisOptions, configuration, database, repository, events, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<AgentExecutionResult> ExecuteAnalyzeAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutDirectOptions directOptions,
        FounderScoutConfiguration configuration,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        CancellationToken cancellationToken)
    {
        string phase = (directOptions.Phase ?? ReadString(input.OccurrenceArguments, "phase") ?? "screen").ToLowerInvariant();
        int maximum = directOptions.Maximum ?? ReadInt(input.OccurrenceArguments, "max", "maximum") ?? configuration.Analysis.BatchSize;
        if (maximum is < 1 or > 500 || phase is not ("screen" or "deep" or "all"))
        {
            await events.WriteErrorAsync(new("founderScout.analyze.phaseInvalid", "Founder Scout supports --phase screen, deep, or all with --max between 1 and 500.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidArguments, "Founder Scout rejected the processing phase or batch limit.");
        }

        if (string.Equals(phase, "all", StringComparison.Ordinal))
        {
            AgentExecutionResult screened = await ExecuteAnalyzeAsync(
                context,
                input,
                directOptions with { Phase = "screen" },
                configuration,
                database,
                repository,
                events,
                cancellationToken).ConfigureAwait(false);
            return screened.ExitCode == AgentExitCode.Success
                ? await ExecuteAnalyzeAsync(
                    context,
                    input,
                    directOptions with { Phase = "deep" },
                    configuration,
                    database,
                    repository,
                    events,
                    cancellationToken).ConfigureAwait(false)
                : screened;
        }

        if (string.Equals(phase, "deep", StringComparison.Ordinal))
        {
            return await ExecuteDeepAnalysisAsync(
                context,
                input,
                configuration,
                repository,
                events,
                maximum,
                cancellationToken).ConfigureAwait(false);
        }

        FounderScoutProcessingSettings settings = configuration.Processing ?? FounderScoutProcessingSettings.Default;
        var store = new FounderScoutRawArtifactStore(database.DataDirectory, database.SnapshotsDirectory);
        var service = new FounderScoutProcessingService(
            repository,
            store,
            new DeterministicFounderProfileParser(),
            new DeterministicProfileRedactor(),
            new CandidateIdentityResolver(),
            new DeterministicFounderScreeningEngine());
        FounderScoutProcessingBatchResult result = await service.ProcessAsync(
            new(maximum, $"screen-{Environment.ProcessId}-{context.RunId.Value:N}", context.RunId.Value, context.RunId.Value.ToString("D"), settings),
            async (progress, token) => await events.WriteProgressAsync(new(
                progress.Current,
                progress.Maximum,
                progress.Maximum == 0 ? null : Math.Clamp((double)progress.Current / progress.Maximum * 100d, 0d, 100d),
                progress.Phase,
                progress.Message), token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.profiles.claimed", NumericValue: result.Claimed, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.profiles.completed", NumericValue: result.Completed, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.profiles.changed", NumericValue: result.Changed, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.profiles.unchanged", NumericValue: result.Unchanged, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.deep_analysis.queued", NumericValue: result.DeepAnalysisQueued, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.monitor", NumericValue: result.Monitored, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.filtered", NumericValue: result.FilteredOut, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("processing.manual_review", NumericValue: result.ManualReview, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        foreach ((string code, int count) in result.ReasonCodeDistribution.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            await events.WriteMetricAsync(new("processing.reason", NumericValue: count, Unit: "profiles", Tags: new Dictionary<string, string>(StringComparer.Ordinal) { ["code"] = code }), cancellationToken).ConfigureAwait(false);
        }
        await events.WriteCheckpointAsync(new(
            "founder-scout-screening",
            JsonSerializer.SerializeToElement(new
            {
                phase = "screen",
                result.Claimed,
                result.Completed,
                result.Changed,
                result.Unchanged,
                result.ParserFailures,
                result.ProcessingFailures,
                result.IdentityConflicts,
                reasonCodeDistribution = result.ReasonCodeDistribution,
            }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        if (result.StoppedForParserHealth)
        {
            await events.WriteErrorAsync(new("founderScout.parser.repeatedFailure", "Repeated parser-health failures stopped processing and paused the affected source segment.", false), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.PermanentFailure, AgentRunStatus.Failed, "Founder Scout stopped on repeated parser-health failure.", JsonSerializer.SerializeToElement(new { result = "ParserFailure", scheduleAction = "Pause", result.Claimed, result.ParserFailures }, JsonOptions));
        }
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            result.Claimed == 0 ? "Founder Scout found no captured profiles requiring processing." : $"Founder Scout processed {result.Completed} of {result.Claimed} claimed profiles without AI calls.",
            JsonSerializer.SerializeToElement(new { result = result.Claimed == 0 ? "NoWork" : "Completed", phase = "screen", result.Claimed, result.Completed, result.Changed, result.Unchanged, result.DeepAnalysisQueued, result.Monitored, result.FilteredOut, result.ManualReview, result.ParserFailures, result.ProcessingFailures, result.IdentityConflicts, noAiCalls = true }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteDeepAnalysisAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        FounderScoutConfiguration configuration,
        FounderScoutRepository repository,
        IAgentEventWriter events,
        int maximum,
        CancellationToken cancellationToken)
    {
        FounderEvaluationPrompt prompt;
        try
        {
            prompt = await FounderEvaluationPromptCatalog.LoadAsync(
                Path.Combine(AppContext.BaseDirectory, "prompts"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await events.WriteErrorAsync(new("founderScout.analysis.promptMissing", "Versioned evaluation prompt assets are unavailable.", false), cancellationToken).ConfigureAwait(false);
            return Failure(AgentExitCode.InvalidConfiguration, "Founder Scout evaluation prompt assets are invalid.");
        }

        string? apiKey = input.GetResolvedSecret(configuration.Ai.ApiKeySecretReference);
        FounderEvaluationModelClientHost provider;
        try
        {
            provider = FounderEvaluationModelClientHost.Create(configuration.Ai, apiKey);
        }
        catch (FounderModelException exception)
        {
            await events.WriteErrorAsync(new(exception.Code, "Founder Scout AI provider configuration requires attention.", false), cancellationToken).ConfigureAwait(false);
            return new(
                AgentExitCode.InvalidConfiguration,
                AgentRunStatus.Failed,
                "Founder Scout AI provider configuration requires attention.",
                JsonSerializer.SerializeToElement(new { result = "AttentionRequired", scheduleAction = "Pause", reasonCode = exception.Code }, JsonOptions));
        }

        await using (provider.ConfigureAwait(false))
        {
            var service = new FounderDeepAnalysisService(
                repository,
                repository,
                repository,
                repository,
                repository,
                repository,
                provider.Client,
                TimeProvider.System);
            FounderDeepAnalysisBatchResult result = await service.AnalyzeAsync(new(
                maximum,
                configuration.Analysis.MaximumConcurrency,
                $"deep-{Environment.ProcessId}-{context.RunId.Value:N}",
                context.RunId.Value,
                context.RunId.Value.ToString("D"),
                configuration,
                prompt), async (progress, token) => await events.WriteProgressAsync(new(
                    progress.Current,
                    progress.Maximum,
                    progress.Maximum == 0 ? null : Math.Clamp((double)progress.Current / progress.Maximum * 100d, 0d, 100d),
                    "deep-analysis",
                    progress.Message), token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            await WriteAnalysisMetricsAsync(events, result, cancellationToken).ConfigureAwait(false);
            await events.WriteCheckpointAsync(new(
                "founder-scout-deep-analysis",
                JsonSerializer.SerializeToElement(new
                {
                    phase = "deep",
                    result.Claimed,
                    result.Completed,
                    result.CacheHits,
                    result.ManualReview,
                    result.Failed,
                    result.ProviderRequests,
                    result.ProviderRetries,
                    result.ReevaluationQueued,
                    result.AttentionRequired,
                    result.AttentionCode,
                }, JsonOptions)), cancellationToken).ConfigureAwait(false);
            if (result.AttentionRequired)
            {
                await events.WriteErrorAsync(new(result.AttentionCode ?? "founderScout.analysis.attentionRequired", "AI provider authentication or configuration requires attention; analysis should be paused.", false), cancellationToken).ConfigureAwait(false);
                return new(
                    AgentExitCode.InvalidConfiguration,
                    AgentRunStatus.Failed,
                    "Founder Scout deep analysis requires provider attention.",
                    JsonSerializer.SerializeToElement(new { result = "AttentionRequired", scheduleAction = "Pause", result.AttentionCode, result.Claimed, result.Completed }, JsonOptions));
            }

            bool failedOnly = result.Claimed > 0 && result.Completed == 0 && result.Failed > 0;
            return new(
                failedOnly ? AgentExitCode.TransientFailure : AgentExitCode.Success,
                failedOnly ? AgentRunStatus.Failed : AgentRunStatus.Completed,
                result.Claimed == 0
                    ? "Founder Scout found no candidates requiring deep analysis."
                    : $"Founder Scout completed {result.Completed} of {result.Claimed} claimed deep evaluations with {result.CacheHits} cache hits.",
                JsonSerializer.SerializeToElement(new
                {
                    result = failedOnly ? "Failed" : result.Claimed == 0 ? "NoWork" : "Completed",
                    phase = "deep",
                    provider = configuration.Ai.Provider,
                    result.Claimed,
                    result.Completed,
                    result.CacheHits,
                    result.ManualReview,
                    result.Failed,
                    result.ProviderRequests,
                    result.ProviderRetries,
                    result.InvitationsGenerated,
                    result.InvitationsNeedsReview,
                    topCandidates = result.TopCandidates,
                    invitationsSent = 0,
                }, JsonOptions));
        }
    }

    private static async ValueTask WriteAnalysisMetricsAsync(
        IAgentEventWriter events,
        FounderDeepAnalysisBatchResult result,
        CancellationToken cancellationToken)
    {
        (string Name, int Value)[] metrics =
        [
            ("analysis.claimed", result.Claimed),
            ("analysis.completed", result.Completed),
            ("analysis.cacheHit", result.CacheHits),
            ("analysis.filtered", result.Filtered),
            ("analysis.manualReview", result.ManualReview),
            ("analysis.failed", result.Failed),
            ("analysis.providerRequests", result.ProviderRequests),
            ("analysis.providerRetries", result.ProviderRetries),
            ("analysis.reevaluationQueued", result.ReevaluationQueued),
            ("candidates.strongConnect", result.StrongConnect),
            ("candidates.exploratory", result.Exploratory),
            ("candidates.monitor", result.Monitor),
            ("candidates.pass", result.Pass),
            ("invitations.generated", result.InvitationsGenerated),
            ("invitations.needsReview", result.InvitationsNeedsReview),
        ];
        foreach ((string name, int value) in metrics)
            await events.WriteMetricAsync(new(name, NumericValue: value, Unit: "count"), cancellationToken).ConfigureAwait(false);
    }

    private static bool CanRunDeepAnalysis(FounderScoutConfiguration configuration) =>
        configuration.Analysis.Enabled
        && (configuration.Ai.Provider == "DiagnosticFake" || !string.IsNullOrWhiteSpace(configuration.Ai.Endpoint));

    private static async ValueTask<AgentExecutionResult> DeferredAsync(
        IAgentEventWriter events,
        string code,
        string message,
        int stage,
        int exitCode,
        CancellationToken cancellationToken)
    {
        await events.WriteWarningAsync(new(
            code,
            message,
            JsonSerializer.SerializeToElement(new { implementedInStage = stage }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        return new(
            exitCode,
            AgentRunStatus.Failed,
            message,
            JsonSerializer.SerializeToElement(new { result = "NotImplemented", implementedInStage = stage }, JsonOptions));
    }

    private static async ValueTask WriteImportMetricsAsync(
        IAgentEventWriter events,
        FounderScoutImportResult result,
        CancellationToken cancellationToken)
    {
        await events.WriteMetricAsync(new("profiles.read", NumericValue: result.CapturesRead, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("candidates.created", NumericValue: result.CandidatesCreated, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("snapshots.created", NumericValue: result.SnapshotsCreated, Unit: "snapshots"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("snapshots.duplicate", NumericValue: result.DuplicateSnapshots, Unit: "snapshots"), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteCountMetricsAsync(
        IAgentEventWriter events,
        FounderScoutDomainCounts counts,
        CancellationToken cancellationToken)
    {
        await events.WriteMetricAsync(new("domain.candidates", NumericValue: counts.Candidates, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("domain.snapshots", NumericValue: counts.Snapshots, Unit: "snapshots"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("domain.pending_analysis", NumericValue: counts.PendingAnalysis, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteRuntimeMetricsAsync(
        IAgentEventWriter events,
        BrowserRuntimeStatus runtime,
        CancellationToken cancellationToken)
    {
        await events.WriteMetricAsync(new("browser.runtime.available", NumericValue: runtime.IsAvailable ? 1 : 0, Unit: "boolean"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("browser.playwright.version", TextValue: runtime.PlaywrightVersion), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("browser.runtime.name", TextValue: runtime.RuntimeName), cancellationToken).ConfigureAwait(false);
        if (runtime.BrowserVersion is not null)
        {
            await events.WriteMetricAsync(new("browser.version", TextValue: runtime.BrowserVersion), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask WriteDiscoveryEventsAsync(
        IAgentEventWriter events,
        FounderScoutDiscoveryResult result,
        CancellationToken cancellationToken)
    {
        await events.WriteMetricAsync(new("discovery.profiles.viewed", NumericValue: result.ViewedProfiles, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("discovery.candidates.created", NumericValue: result.NewCandidates, Unit: "candidates"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("discovery.snapshots.created", NumericValue: result.NewSnapshots, Unit: "snapshots"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("discovery.profiles.known_unchanged", NumericValue: result.KnownUnchangedProfiles, Unit: "profiles"), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("discovery.navigation.attempts", NumericValue: result.NavigationAttempts, Unit: "attempts"), cancellationToken).ConfigureAwait(false);
        foreach (BrowserDiagnosticArtifact artifact in result.Diagnostics)
        {
            await events.WriteArtifactAsync(new(
                "browser-diagnostic",
                artifact.RelativePath,
                artifact.ContentType,
                "Bounded sanitized browser diagnostic; raw page/session content is excluded from the summary.",
                artifact.DeleteAfterUtc), cancellationToken).ConfigureAwait(false);
        }

        if (result.StopSignal.Kind != BrowserStopKind.None)
        {
            await events.WriteWarningAsync(new(
                result.StopSignal.ReasonCode,
                result.StopSignal.Message,
                JsonSerializer.SerializeToElement(new
                {
                    stopKind = result.StopSignal.Kind.ToString(),
                    accountFailover = false,
                    diagnostics = result.Diagnostics.Select(item => item.RelativePath).ToArray(),
                }, JsonOptions)), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<BrowserAccount> EnsureBrowserAccountAsync(
        string accountId,
        StartupSchoolSourceOptions source,
        FounderScoutDatabase database,
        FounderScoutRepository repository,
        TimeProvider timeProvider,
        bool allowCreate,
        CancellationToken cancellationToken)
    {
        _ = BrowserProfilePath.Resolve(database.DataDirectory, accountId);
        IBrowserAccountRepository accountRepository = repository;
        BrowserAccount? account = await accountRepository.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        string relativePath = $"browser/{accountId}";
        if (account is null)
        {
            if (!allowCreate)
            {
                throw new ArgumentException("The browser account does not exist.", nameof(accountId));
            }

            DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            string defaultSegmentId = $"default-{accountId}";
            account = await accountRepository.UpsertAsync(new(
                accountId,
                accountId,
                relativePath,
                Enabled: true,
                BrowserSessionStatus.Unknown,
                [defaultSegmentId],
                null,
                null,
                null,
                null,
                null,
                null,
                nowUtc,
                nowUtc,
                1), cancellationToken).ConfigureAwait(false);
            var defaultSegment = new DiscoverySegment(
                defaultSegmentId,
                "Default Startup School discovery",
                Enabled: true,
                Priority: 100,
                JsonSerializer.Serialize(new { version = "1.0", source.EntryUrl }, JsonOptions),
                accountId,
                null,
                0,
                0,
                0,
                0,
                0,
                null,
                nowUtc,
                nowUtc,
                1);
            await ((IDiscoverySegmentRepository)repository).UpsertAsync(defaultSegment, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(account.BrowserProfileRelativePath.Replace('\\', '/'), relativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The browser account profile path is not canonical.");
        }

        return account;
    }

    private static async ValueTask TransitionAccountStopAsync(
        FounderScoutRepository repository,
        BrowserAccount account,
        BrowserStopSignal signal,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        BrowserSessionStatus status = signal.Kind switch
        {
            BrowserStopKind.ReauthenticationRequired => BrowserSessionStatus.ReauthenticationRequired,
            BrowserStopKind.AccessDenied => BrowserSessionStatus.AccessDenied,
            BrowserStopKind.Throttled => BrowserSessionStatus.Throttled,
            BrowserStopKind.ChallengeDetected => BrowserSessionStatus.ChallengeDetected,
            BrowserStopKind.ParserFailure or BrowserStopKind.UnexpectedHost => BrowserSessionStatus.ParserFailure,
            _ => BrowserSessionStatus.Unknown,
        };
        if (account.SessionStatus == BrowserSessionStatus.Disabled || !account.Enabled)
        {
            _ = await ((IBrowserAccountRepository)repository).UpsertAsync(account with
            {
                Enabled = false,
                SessionStatus = status,
                LastFailedRunAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
                LastErrorReasonCode = signal.ReasonCode,
                LastErrorMessage = signal.Message.Length <= 1_000 ? signal.Message : signal.Message[..1_000],
                UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ((IBrowserAccountRepository)repository).TransitionHealthAsync(
                account.Id,
                status,
                signal.ReasonCode,
                signal.Message,
                null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static AgentExecutionResult StopResult(BrowserStopSignal signal, string summary)
    {
        int exitCode = signal.Kind switch
        {
            BrowserStopKind.ReauthenticationRequired => AgentExitCode.AuthenticationRequired,
            BrowserStopKind.Throttled => AgentExitCode.Throttled,
            BrowserStopKind.ChallengeDetected => AgentExitCode.ChallengeDetected,
            BrowserStopKind.NavigationFailure or BrowserStopKind.ProfileLocked => AgentExitCode.TransientFailure,
            BrowserStopKind.Cancelled => AgentExitCode.Cancelled,
            _ => AgentExitCode.PermanentFailure,
        };
        return new(
            exitCode,
            AgentRunStatus.Failed,
            summary,
            JsonSerializer.SerializeToElement(new
            {
                result = "Stopped",
                stopKind = signal.Kind.ToString(),
                signal.ReasonCode,
                accountFailover = false,
                scheduleAction = ShouldPauseSchedule(signal.Kind) ? "Pause" : "None",
            }, JsonOptions));
    }

    private static bool ShouldPauseSchedule(BrowserStopKind kind) => kind is
        BrowserStopKind.ReauthenticationRequired
        or BrowserStopKind.AccessDenied
        or BrowserStopKind.Throttled
        or BrowserStopKind.ChallengeDetected
        or BrowserStopKind.UnexpectedHost
        or BrowserStopKind.ParserFailure;

    private static AgentExecutionResult Failure(int exitCode, string summary) => new(
        exitCode,
        AgentRunStatus.Failed,
        summary,
        JsonSerializer.SerializeToElement(new { result = "Failed" }, JsonOptions));

    private static string? ReadInputPath(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string name in new[] { "inputPath", "input" })
        {
            if (arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement arguments, params string[] names)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement arguments, params string[] names)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (string name in names)
        {
            if (arguments.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed)) return parsed;
        }
        return null;
    }

    private static bool TryExtractCommandOptions(
        IReadOnlyList<string> arguments,
        out string[] standardArguments,
        out FounderScoutDirectOptions options)
    {
        options = new(null, null, null, null, null, null, null);
        if (arguments.Count == 0)
        {
            standardArguments = [];
            return false;
        }

        var normalized = new List<string>(arguments.Count) { arguments[0] };
        for (var index = 1; index < arguments.Count; index++)
        {
            string name = arguments[index];
            bool recognized = name == "--input" && arguments[0] == "import"
                || name == "--account" && arguments[0] is "run" or "discover" or "authenticate" or "diagnose" or "record-fixture"
                || name == "--segment" && arguments[0] is "run" or "discover"
                || name == "--phase" && arguments[0] is "run" or "analyze"
                || name == "--max" && arguments[0] is "run" or "analyze"
                || name == "--type" && arguments[0] == "report"
                || name == "--top" && arguments[0] == "report";
            if (!recognized)
            {
                normalized.Add(arguments[index]);
                continue;
            }

            if (index + 1 >= arguments.Count
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                standardArguments = [];
                return false;
            }

            string value = arguments[++index];
            if (name == "--input")
            {
                if (options.InputPath is not null)
                {
                    standardArguments = [];
                    return false;
                }

                options = options with { InputPath = value };
            }
            else if (name == "--account")
            {
                if (options.AccountId is not null)
                {
                    standardArguments = [];
                    return false;
                }

                options = options with { AccountId = value };
            }
            else if (name == "--segment")
            {
                if (options.SegmentId is not null)
                {
                    standardArguments = [];
                    return false;
                }

                options = options with { SegmentId = value };
            }
            else if (name == "--phase")
            {
                if (options.Phase is not null) { standardArguments = []; return false; }
                options = options with { Phase = value };
            }
            else if (name == "--max")
            {
                if (options.Maximum is not null || !int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int maximum)) { standardArguments = []; return false; }
                options = options with { Maximum = maximum };
            }
            else if (name == "--type")
            {
                if (options.ReportType is not null || value is not ("all" or "top-candidates" or "invitation-queue")) { standardArguments = []; return false; }
                options = options with { ReportType = value };
            }
            else
            {
                if (options.Top is not null || !int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int top) || top is < 1 or > 1_000) { standardArguments = []; return false; }
                options = options with { Top = top };
            }
        }

        standardArguments = normalized.ToArray();
        return true;
    }

    private static async ValueTask WriteUsageAsync(TextWriter standardError) =>
        await standardError.WriteLineAsync(
            "Usage: FounderScout run|discover [--account <id>] [--segment <id>]|authenticate --account <id>|record-fixture --account <id>|analyze --phase screen|deep|all [--max <1..500>]|import [--input <absolute-import-path>]|report [--type all|top-candidates|invitation-queue] [--top <1..1000>]|diagnose [--account <id>] <standard Agent SDK options> | protocol-demo").ConfigureAwait(false);

    private sealed record FounderScoutDirectOptions(string? InputPath, string? AccountId, string? SegmentId, string? Phase, int? Maximum, string? ReportType, int? Top);
}
