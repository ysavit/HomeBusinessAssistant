using System.Text.Json;
using FounderScout.Agent;
using FounderScout.Application;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace FounderScout.Tests;

internal sealed class FounderScoutCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ProtocolDemoWritesOnlyValidJsonlAndExitsSuccessfully()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await FounderScoutCommand.ExecuteAsync(
            ["protocol-demo"],
            standardOutput,
            standardError,
            TimeProvider.System).ConfigureAwait(false);
        string[] lines = NonEmptyLines(standardOutput.ToString());
        AgentEventReadResult[] reads = lines.Select(AgentEventSerializer.Deserialize).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(AgentExitCode.Success));
            Assert.That(standardError.ToString(), Is.Empty);
            Assert.That(lines, Has.Length.EqualTo(5));
            Assert.That(reads, Has.All.Matches<AgentEventReadResult>(result => result.IsSuccess));
            Assert.That(reads.Select(result => result.Event!.Sequence), Is.EqualTo(Enumerable.Range(1, 5)));
            Assert.That(reads.Select(result => result.Event!.Type),
                Is.EqualTo(new[]
                {
                    AgentEventTypes.Started,
                    AgentEventTypes.Progress,
                    AgentEventTypes.Metric,
                    AgentEventTypes.Summary,
                    AgentEventTypes.Completed,
                }));
        });
    }

    [Test]
    public async Task InvalidCommandUsesStandardErrorAndLeavesProtocolStreamEmpty()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await FounderScoutCommand.ExecuteAsync(
            ["unknown"],
            standardOutput,
            standardError,
            TimeProvider.System).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(AgentExitCode.InvalidArguments));
            Assert.That(standardOutput.ToString(), Is.Empty);
            Assert.That(standardError.ToString(), Does.Contain("protocol-demo"));
        });
    }

    [Test]
    public async Task ImportDiagnoseReportAndNoWorkRunUseRealDatabaseAndValidJsonl()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-founder-command-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string artifacts = Path.Combine(root, "artifacts");
        string inputEnvelope = Path.Combine(root, "execution-input.json");
        string runInputEnvelope = Path.Combine(root, "run-execution-input.json");
        Directory.CreateDirectory(root);
        try
        {
            FounderScoutDatabase database = await FounderScoutDatabaseInitializer.InitializeAsync(
                new FounderScoutDatabaseSettings(data),
                TimeProvider.System);
            string fixturePath = Path.Combine(database.ImportsDirectory, "command-fixture.json");
            await File.WriteAllTextAsync(fixturePath, CreateFixtureJson());
            FounderScoutConfiguration configuration = new FounderScoutDefaults().GetDefault().Configuration
                .Deserialize<FounderScoutConfiguration>(JsonOptions)
                ?? throw new InvalidOperationException("Founder Scout defaults did not deserialize.");
            await File.WriteAllTextAsync(
                inputEnvelope,
                AgentExecutionInput.Serialize(
                    JsonSerializer.Serialize(configuration with { DataDirectory = data }, JsonOptions),
                    "{}"));
            await File.WriteAllTextAsync(
                runInputEnvelope,
                AgentExecutionInput.Serialize(
                    JsonSerializer.Serialize(configuration with { DataDirectory = data }, JsonOptions),
                    JsonSerializer.Serialize(new { inputPath = fixturePath }, JsonOptions)));

            AgentCommandResult imported = await ExecuteCommandAsync("import", inputEnvelope, data, artifacts, fixturePath);
            AgentCommandResult composedRun = await ExecuteCommandAsync("run", runInputEnvelope, data, artifacts);
            AgentCommandResult diagnosed = await ExecuteCommandAsync("diagnose", inputEnvelope, data, artifacts);
            AgentCommandResult reported = await ExecuteCommandAsync("report", inputEnvelope, data, artifacts);
            AgentCommandResult noWork = await ExecuteCommandAsync("run", inputEnvelope, data, artifacts);
            var repository = new FounderScoutRepository(database.ContextFactory, TimeProvider.System);
            FounderScoutDomainCounts counts = await repository.GetCountsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(imported.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(imported.StandardOutput, Does.Contain("snapshots.created"));
                Assert.That(imported.StandardOutput, Does.Contain("\"snapshotsCreated\":3"));
                Assert.That(composedRun.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(composedRun.StandardOutput,
                    Does.Contain("\"name\":\"snapshots.duplicate\",\"numericValue\":4"));
                Assert.That(composedRun.Events.Count(item => item.Event?.Type == AgentEventTypes.Artifact), Is.EqualTo(6));
                Assert.That(diagnosed.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(diagnosed.StandardOutput, Does.Contain("database.journal_mode"));
                Assert.That(reported.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(reported.Events.Count(item => item.Event?.Type == AgentEventTypes.Artifact), Is.EqualTo(6));
                Assert.That(noWork.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(noWork.StandardOutput, Does.Contain("NoWork"));
                Assert.That(File.Exists(Path.Combine(artifacts, "top-candidates.html")), Is.True);
                Assert.That(File.Exists(Path.Combine(artifacts, "top-candidates.md")), Is.True);
                Assert.That(File.Exists(Path.Combine(artifacts, "candidates.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(artifacts, "candidates.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(artifacts, "discovery-summary.md")), Is.True);
                Assert.That(File.Exists(Path.Combine(artifacts, "manual-invitation-queue.md")), Is.True);
                Assert.That(counts.Candidates, Is.EqualTo(3));
                Assert.That(counts.Snapshots, Is.EqualTo(3));
                Assert.That(new[] { imported, composedRun, diagnosed, reported, noWork },
                    Has.All.Matches<AgentCommandResult>(result =>
                        string.IsNullOrEmpty(result.StandardError)
                        && result.Events.Count > 0
                        && result.Events.All(item => item.IsSuccess)));
            });
        }
        finally
        {
            DeleteTestDirectory(root, "hba-founder-command-");
        }
    }

    [Test]
    public async Task AnalyzeScreenEmitsNoWorkProtocolWithoutAi()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-founder-deferred-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string artifacts = Path.Combine(root, "artifacts");
        string inputEnvelope = Path.Combine(root, "execution-input.json");
        Directory.CreateDirectory(root);
        try
        {
            FounderScoutConfiguration configuration = new FounderScoutDefaults().GetDefault().Configuration
                .Deserialize<FounderScoutConfiguration>(JsonOptions)
                ?? throw new InvalidOperationException("Founder Scout defaults did not deserialize.");
            await File.WriteAllTextAsync(
                inputEnvelope,
                AgentExecutionInput.Serialize(
                    JsonSerializer.Serialize(configuration with { DataDirectory = data }, JsonOptions),
                    "{}"));

            AgentCommandResult result = await ExecuteCommandAsync("analyze", inputEnvelope, data, artifacts);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(result.StandardError, Is.Empty);
                Assert.That(result.StandardOutput, Does.Contain("processing.profiles.claimed"));
                Assert.That(result.StandardOutput, Does.Contain("noAiCalls"));
                Assert.That(result.Events, Has.All.Matches<AgentEventReadResult>(item => item.IsSuccess));
                Assert.That(result.Events[^1].Event?.Type, Is.EqualTo(AgentEventTypes.Completed));
            });
        }
        finally
        {
            DeleteTestDirectory(root, "hba-founder-deferred-");
        }
    }

    [TestCase("top-candidates", 4)]
    [TestCase("invitation-queue", 1)]
    public async Task ReportTypeArgumentsSelectTheRequestedArtifactFamily(string reportType, int expectedArtifacts)
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-founder-report-command-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string artifacts = Path.Combine(root, "artifacts");
        string inputEnvelope = Path.Combine(root, "execution-input.json");
        Directory.CreateDirectory(root);
        try
        {
            FounderScoutConfiguration configuration = new FounderScoutDefaults().GetDefault().Configuration
                .Deserialize<FounderScoutConfiguration>(JsonOptions)
                ?? throw new InvalidOperationException("Founder Scout defaults did not deserialize.");
            await File.WriteAllTextAsync(
                inputEnvelope,
                AgentExecutionInput.Serialize(
                    JsonSerializer.Serialize(configuration with { DataDirectory = data }, JsonOptions),
                    "{}"));

            AgentCommandResult result = await ExecuteCommandAsync(
                "report",
                inputEnvelope,
                data,
                artifacts,
                directArguments: ["--type", reportType, "--top", "30"]);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(AgentExitCode.Success));
                Assert.That(result.StandardError, Is.Empty);
                Assert.That(result.Events.Count(item => item.Event?.Type == AgentEventTypes.Artifact), Is.EqualTo(expectedArtifacts));
                Assert.That(result.StandardOutput, Does.Contain($"\"reportType\":\"{reportType}\""));
            });
        }
        finally
        {
            DeleteTestDirectory(root, "hba-founder-report-command-");
        }
    }

    private static async ValueTask<AgentCommandResult> ExecuteCommandAsync(
        string command,
        string inputEnvelope,
        string data,
        string artifacts,
        string? fixturePath = null,
        IReadOnlyList<string>? directArguments = null)
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();
        var arguments = new List<string>
        {
            command,
        };
        if (directArguments is not null)
        {
            arguments.AddRange(directArguments);
        }

        if (fixturePath is not null)
        {
            arguments.Add("--input");
            arguments.Add(fixturePath);
        }

        arguments.AddRange([
            "--run-id", AgentRunId.New().ToString(),
            "--occurrence-id", OccurrenceId.New().ToString(),
            "--agent-id", "founder-scout",
            "--config-file", inputEnvelope,
            "--data-directory", data,
            "--artifact-directory", artifacts,
            "--protocol-version", "1.0",
        ]);
        int exitCode = await FounderScoutCommand.ExecuteAsync(
            arguments,
            standardOutput,
            standardError,
            TimeProvider.System);
        string output = standardOutput.ToString();
        return new(
            exitCode,
            output,
            standardError.ToString(),
            NonEmptyLines(output).Select(AgentEventSerializer.Deserialize).ToArray());
    }

    private static string CreateFixtureJson()
    {
        DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        object Capture(string key, string name) => new
        {
            captureSchemaVersion = "1.0",
            source = "fixture",
            sourceAccountId = "fixture-account",
            sourceSegmentId = "fixture-segment",
            sourceProfileKey = key,
            profileUrl = $"https://example.invalid/profile/{key}",
            capturedAtUtc,
            displayName = name,
            rawText = $"{name} builds a local business.",
            structuredFields = new { skills = new[] { "operations" } },
            sourceAdapterVersion = "fixture-1.0",
        };
        object first = Capture("candidate-001", "Candidate One");
        return JsonSerializer.Serialize(new[]
        {
            first,
            Capture("candidate-002", "Candidate Two"),
            Capture("candidate-003", "Candidate Three"),
            first,
        }, JsonOptions);
    }

    private static void DeleteTestDirectory(string path, string requiredPrefix)
    {
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected Founder Scout test directory.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static string[] NonEmptyLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private sealed record AgentCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        IReadOnlyList<AgentEventReadResult> Events);
}
