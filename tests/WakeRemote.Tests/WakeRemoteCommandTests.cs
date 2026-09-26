using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Domain.Agents;
using WakeRemote.Agent;
using WakeRemote.Application;

namespace WakeRemote.Tests;

internal sealed class WakeRemoteCommandTests
{
    [Test]
    public async Task ProtocolDemoWritesOnlyValidJsonlAndExitsSuccessfully()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await WakeRemoteCommand.ExecuteAsync(
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
    public async Task UnknownCommandUsesStandardErrorAndLeavesProtocolStreamEmpty()
    {
        using StringWriter standardOutput = new();
        using StringWriter standardError = new();

        int exitCode = await WakeRemoteCommand.ExecuteAsync(
            ["run"],
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
    public async Task WakeTestWritesTimestampArtifactAndValidProtocolWithoutChangingPower()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-wake-agent-{Guid.NewGuid():N}");
        string artifacts = Path.Combine(root, "artifacts");
        string data = Path.Combine(root, "data");
        string config = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        try
        {
            using StringWriter standardOutput = new();
            using StringWriter standardError = new();
            AgentRunId runId = AgentRunId.New();
            OccurrenceId occurrenceId = OccurrenceId.New();
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 30, 18, 3, 12, TimeSpan.Zero));

            int exitCode = await WakeRemoteCommand.ExecuteAsync(
            [
                "wake-test",
                "--run-id", runId.ToString(),
                "--occurrence-id", occurrenceId.ToString(),
                "--agent-id", "wake-remote",
                "--config-file", config,
                "--data-directory", data,
                "--artifact-directory", artifacts,
                "--protocol-version", "1.0",
            ], standardOutput, standardError, time);
            AgentEventReadResult[] events = NonEmptyLines(standardOutput.ToString())
                .Select(AgentEventSerializer.Deserialize)
                .ToArray();
            string artifactPath = Path.Combine(artifacts, "wake-test-result.json");

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(standardError.ToString(), Is.Empty);
                Assert.That(events, Has.All.Matches<AgentEventReadResult>(item => item.IsSuccess));
                Assert.That(events.Select(item => item.Event!.Type), Is.EqualTo(new[]
                {
                    AgentEventTypes.Started,
                    AgentEventTypes.Metric,
                    AgentEventTypes.Artifact,
                    AgentEventTypes.Summary,
                    AgentEventTypes.Completed,
                }));
                Assert.That(File.Exists(artifactPath), Is.True);
                Assert.That(File.ReadAllText(artifactPath), Does.Contain("2026-08-30T18:03:12+00:00"));
                Assert.That(File.ReadAllText(artifactPath), Does.Contain(occurrenceId.ToString()));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task SixtySecondDiagnosticWindowWritesOnlyJsonlAndReleasesPower()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-wake-agent-run-{Guid.NewGuid():N}");
        string artifacts = Path.Combine(root, "artifacts");
        string data = Path.Combine(root, "data");
        string inputPath = Path.Combine(root, "execution-input.json");
        Directory.CreateDirectory(root);
        try
        {
            DateTimeOffset start = new(2026, 8, 30, 18, 3, 12, TimeSpan.Zero);
            var time = new WakeRemoteWorkflowTests.ManualTimeProvider(start);
            WakeRemoteConfiguration configuration = WakeRemoteWorkflowTests.CreateConfiguration() with
            {
                WindowHeartbeatIntervalSeconds = 20,
                RemoteProvider = WakeRemoteWorkflowTests.CreateConfiguration().RemoteProvider with
                {
                    ProcessNames = ["SensitiveLocalProcessName"],
                    DiagnosticReady = true,
                },
            };
            string configurationJson = JsonSerializer.Serialize(configuration, WakeRemoteWorkflowTests.JsonOptions);
            string argumentsJson = WakeRemoteInput.SerializeOccurrence(new(
                "smoke-window",
                start,
                start.AddSeconds(60),
                RemoteProviderRequired: true));
            await File.WriteAllTextAsync(
                inputPath,
                AgentExecutionInput.Serialize(configurationJson, argumentsJson));
            var power = new WakeRemoteWorkflowTests.TrackingPowerService();
            var services = new WakeRemoteCommandServices(
                power,
                new WakeRemoteWorkflowTests.SequenceNetworkProbe([new(true, "network.ready")]),
                new WakeRemoteWorkflowTests.FixedProviderProbe(RemoteProviderReadiness.Ready),
                new WakeRemoteWorkflowTests.AdvancingDelay(time),
                new FakePowerDiagnostics(time));
            using StringWriter standardOutput = new();
            using StringWriter standardError = new();

            int exitCode = await WakeRemoteCommand.ExecuteAsync(
                CreateStandardArguments("run", inputPath, data, artifacts),
                standardOutput,
                standardError,
                time,
                services);
            string[] lines = NonEmptyLines(standardOutput.ToString());
            AgentEventReadResult[] reads = lines.Select(AgentEventSerializer.Deserialize).ToArray();
            string output = standardOutput.ToString();

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(standardError.ToString(), Is.Empty);
                Assert.That(reads, Has.All.Matches<AgentEventReadResult>(result => result.IsSuccess));
                Assert.That(reads.Select(result => result.Event!.Type), Does.Contain(AgentEventTypes.Heartbeat));
                Assert.That(output, Does.Contain("wake_delay_seconds"));
                Assert.That(output, Does.Contain("network.ready"));
                Assert.That(output, Does.Contain("remote.ready"));
                Assert.That(output, Does.Contain("power_handle_released"));
                Assert.That(output, Does.Contain("DiagnosticFake"));
                Assert.That(output, Does.Not.Contain("SensitiveLocalProcessName"));
                Assert.That(power.Acquisitions, Is.EqualTo(1));
                Assert.That(power.Releases, Is.EqualTo(1));
                Assert.That(time.GetUtcNow(), Is.EqualTo(start.AddSeconds(60)));
                Assert.That(reads[^2].Event!.Type, Is.EqualTo(AgentEventTypes.Summary));
                Assert.That(reads[^1].Event!.Type, Is.EqualTo(AgentEventTypes.Completed));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DiagnoseWritesSafeReadOnlyArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-wake-agent-diagnose-{Guid.NewGuid():N}");
        string artifacts = Path.Combine(root, "artifacts");
        string data = Path.Combine(root, "data");
        string inputPath = Path.Combine(root, "execution-input.json");
        Directory.CreateDirectory(root);
        try
        {
            var time = new WakeRemoteWorkflowTests.ManualTimeProvider(
                new DateTimeOffset(2026, 8, 30, 18, 3, 12, TimeSpan.Zero));
            WakeRemoteConfiguration configuration = WakeRemoteWorkflowTests.CreateConfiguration();
            await File.WriteAllTextAsync(
                inputPath,
                AgentExecutionInput.Serialize(JsonSerializer.Serialize(configuration, WakeRemoteWorkflowTests.JsonOptions), "{}"));
            var power = new WakeRemoteWorkflowTests.TrackingPowerService();
            var services = new WakeRemoteCommandServices(
                power,
                new WakeRemoteWorkflowTests.SequenceNetworkProbe([new(true, "network.ready")]),
                new WakeRemoteWorkflowTests.FixedProviderProbe(RemoteProviderReadiness.Ready),
                new WakeRemoteWorkflowTests.AdvancingDelay(time),
                new FakePowerDiagnostics(time));
            using StringWriter standardOutput = new();
            using StringWriter standardError = new();

            int exitCode = await WakeRemoteCommand.ExecuteAsync(
                CreateStandardArguments("diagnose", inputPath, data, artifacts),
                standardOutput,
                standardError,
                time,
                services);
            string artifact = await File.ReadAllTextAsync(Path.Combine(artifacts, "wake-remote-diagnostics.json"));

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(standardError.ToString(), Is.Empty);
                Assert.That(artifact, Does.Contain("diagnosticProvider"));
                Assert.That(artifact, Does.Contain("configuredTimeZone"));
                Assert.That(power.Acquisitions, Is.Zero);
                Assert.That(NonEmptyLines(standardOutput.ToString()).Select(AgentEventSerializer.Deserialize),
                    Has.All.Matches<AgentEventReadResult>(result => result.IsSuccess));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] NonEmptyLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private static string[] CreateStandardArguments(
        string command,
        string inputPath,
        string data,
        string artifacts) =>
    [
        command,
        "--run-id", AgentRunId.New().ToString(),
        "--occurrence-id", OccurrenceId.New().ToString(),
        "--agent-id", "wake-remote",
        "--config-file", inputPath,
        "--data-directory", data,
        "--artifact-directory", artifacts,
        "--protocol-version", "1.0",
    ];

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakePowerDiagnostics(TimeProvider timeProvider) : IPowerDiagnosticsService
    {
        public Task<PowerDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PowerDiagnosticsSnapshot(
                timeProvider.GetUtcNow(),
                [],
                SleepStatesQuerySucceeded: true,
                WakeTimersQuerySucceeded: true,
                HasReportedWakeTimer: false,
                HasArmedWakeDevice: true));
        }
    }
}
