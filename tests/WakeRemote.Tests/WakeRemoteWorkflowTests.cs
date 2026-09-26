using System.Text.Json;
using HomeBusinessAssistant.Application.Power;
using WakeRemote.Application;

namespace WakeRemote.Tests;

internal sealed class WakeRemoteWorkflowTests
{
    [Test]
    public void ConfigurationValidationAcceptsDefaultsAndRejectsUnsafeSettings()
    {
        JsonElement valid = new WakeRemoteDefaults().GetDefault().Configuration;
        (WakeRemoteConfiguration? parsed, IReadOnlyList<WakeRemoteValidationError> validErrors) =
            WakeRemoteInput.ParseConfiguration(valid);
        JsonElement forceSleep = JsonSerializer.SerializeToElement(CreateConfiguration() with
        {
            ForceSleepAfterWindow = true,
        }, JsonOptions);
        JsonElement credential = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = "1.0",
            password = "must-not-be-stored",
        });

        (_, IReadOnlyList<WakeRemoteValidationError> sleepErrors) = WakeRemoteInput.ParseConfiguration(forceSleep);
        (_, IReadOnlyList<WakeRemoteValidationError> credentialErrors) = WakeRemoteInput.ParseConfiguration(credential);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(validErrors, Is.Empty);
            Assert.That(sleepErrors.Select(error => error.Code), Does.Contain("wakeRemote.configuration.forceSleepUnsupported"));
            Assert.That(credentialErrors.Select(error => error.Code), Does.Contain("wakeRemote.configuration.plaintextCredentialForbidden"));
        });
    }

    [Test]
    public void OccurrenceValidationEnforcesWindowDurationAndStaleness()
    {
        DateTimeOffset now = new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero);
        WakeRemoteConfiguration configuration = CreateConfiguration() with { MaximumWakeStalenessSeconds = 60 };
        JsonElement valid = JsonSerializer.SerializeToElement(new WakeRemoteOccurrenceArguments(
            "window-1", now, now.AddMinutes(5), true), JsonOptions);
        JsonElement tooLong = JsonSerializer.SerializeToElement(new WakeRemoteOccurrenceArguments(
            "window-2", now, now.AddDays(2), true), JsonOptions);
        JsonElement stale = JsonSerializer.SerializeToElement(new WakeRemoteOccurrenceArguments(
            "window-3", now.AddMinutes(-2), now.AddMinutes(5), true), JsonOptions);

        (WakeRemoteOccurrenceArguments? parsed, IReadOnlyList<WakeRemoteValidationError> validErrors) =
            WakeRemoteInput.ParseOccurrence(valid, configuration, now);
        (_, IReadOnlyList<WakeRemoteValidationError> durationErrors) = WakeRemoteInput.ParseOccurrence(tooLong, configuration, now);
        (_, IReadOnlyList<WakeRemoteValidationError> staleErrors) = WakeRemoteInput.ParseOccurrence(stale, configuration, now);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(validErrors, Is.Empty);
            Assert.That(durationErrors.Select(error => error.Code), Does.Contain("wakeRemote.occurrence.windowTooLong"));
            Assert.That(staleErrors.Select(error => error.Code), Does.Contain("wakeRemote.occurrence.tooStale"));
        });
    }

    [Test]
    public async Task NetworkRetriesThenReadyAndActiveWindowReleasePower()
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var power = new TrackingPowerService();
        var network = new SequenceNetworkProbe([
            new(false, "network.unavailable"),
            new(true, "network.ready"),
        ]);
        var events = new RecordingEventSink();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            network,
            new FixedProviderProbe(RemoteProviderReadiness.Ready),
            new AdvancingDelay(time));

        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            CreateConfiguration() with { WindowHeartbeatIntervalSeconds = 2 },
            new("window", time.GetUtcNow(), time.GetUtcNow().AddSeconds(6), true),
            events);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WakeRemoteResultKind.Completed));
            Assert.That(result.Summary.NetworkAttempts, Is.EqualTo(2));
            Assert.That(result.Summary.NetworkReadySeconds, Is.EqualTo(1));
            Assert.That(result.Summary.KeepAwakeSeconds, Is.EqualTo(6));
            Assert.That(power.Acquisitions, Is.EqualTo(1));
            Assert.That(power.Releases, Is.EqualTo(1));
            Assert.That(power.LastKeepDisplayOn, Is.False);
            Assert.That(events.Heartbeats, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task NetworkTimeoutReturnsFailureAndReleasesPower()
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var power = new TrackingPowerService();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            new SequenceNetworkProbe([new(false, "network.unavailable")]),
            new FixedProviderProbe(RemoteProviderReadiness.Ready),
            new AdvancingDelay(time));

        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            CreateConfiguration() with { NetworkReadyTimeoutSeconds = 3, NetworkProbeIntervalSeconds = 1 },
            new("window", time.GetUtcNow(), time.GetUtcNow().AddMinutes(1), true),
            new RecordingEventSink());

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WakeRemoteResultKind.NetworkTimeout));
            Assert.That(result.Summary.NetworkReady, Is.False);
            Assert.That(result.Summary.NetworkAttempts, Is.EqualTo(4));
            Assert.That(power.Releases, Is.EqualTo(1));
        });
    }

    [TestCase(RemoteProviderReadiness.NotReady)]
    [TestCase(RemoteProviderReadiness.NotConfigured)]
    public async Task RequiredProviderFailureReleasesPower(RemoteProviderReadiness readiness)
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var power = new TrackingPowerService();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            new SequenceNetworkProbe([new(true, "network.ready")]),
            new FixedProviderProbe(readiness),
            new AdvancingDelay(time));

        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            CreateConfiguration(),
            new("window", time.GetUtcNow(), time.GetUtcNow().AddMinutes(1), true),
            new RecordingEventSink());

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WakeRemoteResultKind.RemoteProviderUnavailable));
            Assert.That(result.Summary.RemoteProviderReady, Is.False);
            Assert.That(power.Releases, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OptionalProviderWarningContinuesUntilDeadline()
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var events = new RecordingEventSink();
        var power = new TrackingPowerService();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            new SequenceNetworkProbe([new(true, "network.ready")]),
            new FixedProviderProbe(RemoteProviderReadiness.NotReady),
            new AdvancingDelay(time));

        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            CreateConfiguration(),
            new("window", time.GetUtcNow(), time.GetUtcNow().AddSeconds(3), false),
            events);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WakeRemoteResultKind.Completed));
            Assert.That(result.Summary.RemoteProviderReady, Is.False);
            Assert.That(events.WarningCodes, Does.Contain("wakeRemote.remote.optionalUnavailable"));
            Assert.That(power.Releases, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExpiredWindowDoesNotAcquirePowerOrExtendDeadline()
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var power = new TrackingPowerService();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            new SequenceNetworkProbe([new(true, "network.ready")]),
            new FixedProviderProbe(RemoteProviderReadiness.Ready),
            new AdvancingDelay(time));

        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            CreateConfiguration(),
            new("expired", time.GetUtcNow().AddMinutes(-5), time.GetUtcNow(), true, true),
            new RecordingEventSink());

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WakeRemoteResultKind.WindowExpired));
            Assert.That(power.Acquisitions, Is.Zero);
            Assert.That(result.Summary.KeepAwakeSeconds, Is.Zero);
        });
    }

    [Test]
    public void CancellationDuringNetworkWaitReleasesPower()
    {
        var time = new ManualTimeProvider(new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero));
        var power = new TrackingPowerService();
        using CancellationTokenSource cancellation = new();
        WakeRemoteWorkflow workflow = CreateWorkflow(
            time,
            power,
            new SequenceNetworkProbe([new(false, "network.unavailable")]),
            new FixedProviderProbe(RemoteProviderReadiness.Ready),
            new CancellingDelay(cancellation));

        Assert.That(async () => await workflow.RunAsync(
            CreateConfiguration(),
            new("window", time.GetUtcNow(), time.GetUtcNow().AddMinutes(1), true),
            new RecordingEventSink(),
            cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(power.Releases, Is.EqualTo(1));
    }

    private static WakeRemoteWorkflow CreateWorkflow(
        TimeProvider time,
        IPowerRequestService power,
        INetworkReadinessProbe network,
        IRemoteAccessProviderProbe provider,
        IWakeRemoteDelay delay) => new(power, network, provider, delay, time);

    internal static WakeRemoteConfiguration CreateConfiguration() => new(
        WakeRemoteConfiguration.CurrentSchemaVersion,
        TimeZoneInfo.Local.Id,
        NetworkReadyTimeoutSeconds: 10,
        NetworkProbeIntervalSeconds: 1,
        WindowHeartbeatIntervalSeconds: 1,
        MaximumWakeStalenessSeconds: 300,
        KeepDisplayOn: false,
        ReleaseToNormalPowerPolicyAfterWindow: true,
        ForceSleepAfterWindow: false,
        new(
            RemoteProviderKind.DiagnosticFake,
            ServiceNames: [],
            ProcessNames: [],
            CheckLocalListener: false,
            ListenerHost: "127.0.0.1",
            ListenerPort: 3389,
            DiagnosticReady: true),
        new(false, null),
        new(false, null, 0));

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset now = utcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now = now.Add(value);
    }

    internal sealed class TrackingPowerService : IPowerRequestService
    {
        public int Acquisitions { get; private set; }
        public int Releases { get; private set; }
        public bool LastKeepDisplayOn { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireSystemRequiredAsync(
            string reason,
            bool keepDisplayOn,
            CancellationToken cancellationToken = default)
        {
            _ = reason;
            cancellationToken.ThrowIfCancellationRequested();
            Acquisitions++;
            LastKeepDisplayOn = keepDisplayOn;
            return ValueTask.FromResult<IAsyncDisposable>(new Handle(this));
        }

        private sealed class Handle(TrackingPowerService owner) : IAsyncDisposable
        {
            private int disposed;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    owner.Releases++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    internal sealed class SequenceNetworkProbe(IEnumerable<NetworkReadinessAttempt> attempts) : INetworkReadinessProbe
    {
        private readonly Queue<NetworkReadinessAttempt> values = new(attempts);
        private NetworkReadinessAttempt? last;

        public ValueTask<NetworkReadinessAttempt> ProbeAsync(
            WakeRemoteConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            _ = configuration;
            cancellationToken.ThrowIfCancellationRequested();
            if (values.Count > 0)
            {
                last = values.Dequeue();
            }

            return ValueTask.FromResult(last ?? new NetworkReadinessAttempt(false, "network.unavailable"));
        }
    }

    internal sealed class FixedProviderProbe(RemoteProviderReadiness readiness) : IRemoteAccessProviderProbe
    {
        public ValueTask<RemoteProviderProbeResult> ProbeAsync(
            RemoteProviderSettings configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RemoteProviderProbeResult(
                configuration.Kind,
                readiness,
                readiness == RemoteProviderReadiness.Ready ? "remote.provider-ready" : "remote.provider-unavailable"));
        }
    }

    internal sealed class AdvancingDelay(ManualTimeProvider time) : IWakeRemoteDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            time.Advance(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingDelay(CancellationTokenSource cancellation) : IWakeRemoteDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            _ = delay;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class RecordingEventSink : IWakeRemoteEventSink
    {
        public int Heartbeats { get; private set; }
        public List<string> WarningCodes { get; } = [];

        public ValueTask HeartbeatAsync(string phase, string message, CancellationToken cancellationToken = default)
        {
            _ = phase;
            _ = message;
            cancellationToken.ThrowIfCancellationRequested();
            Heartbeats++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ProgressAsync(string phase, string message, CancellationToken cancellationToken = default)
        {
            _ = phase;
            _ = message;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask NumericMetricAsync(string name, double value, string? unit = null, CancellationToken cancellationToken = default)
        {
            _ = name;
            _ = value;
            _ = unit;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask TextMetricAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            _ = name;
            _ = value;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WarningAsync(string code, string message, CancellationToken cancellationToken = default)
        {
            _ = message;
            cancellationToken.ThrowIfCancellationRequested();
            WarningCodes.Add(code);
            return ValueTask.CompletedTask;
        }
    }
}
