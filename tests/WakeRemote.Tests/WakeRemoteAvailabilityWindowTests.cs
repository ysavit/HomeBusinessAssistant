using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using Moq;
using WakeRemote.Application;

namespace WakeRemote.Tests;

internal sealed class WakeRemoteAvailabilityWindowTests
{
    [Test]
    public async Task WeekdayWindowCreatesRequiredWakeScheduleAndTypedOccurrencePayload()
    {
        DateTimeOffset now = new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero);
        DateTimeOffset due = now.AddDays(1);
        var time = new WakeRemoteWorkflowTests.ManualTimeProvider(now);
        AgentConfigurationRecord configuration = CreateConfigurationRecord(now);
        var configurations = new Mock<IAgentConfigurationService>(MockBehavior.Strict);
        configurations
            .Setup(service => service.GetCurrentAsync(WakeRemoteDefaults.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuration);
        var schedules = new Mock<IScheduleRepository>(MockBehavior.Strict);
        schedules
            .Setup(repository => repository.SaveAsync(
                It.IsAny<AgentScheduleRecord>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentScheduleRecord value, long? _, CancellationToken _) => value with { ConcurrencyToken = 1 });
        var validator = new Mock<IAgentScheduleValidator>(MockBehavior.Strict);
        validator
            .Setup(service => service.ValidateAsync(It.IsAny<AgentScheduleRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentScheduleValidationResult([]));
        var audit = new Mock<IAuditWriter>(MockBehavior.Strict);
        audit
            .Setup(writer => writer.WriteAsync(
                It.IsAny<WriteAuditEventRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WriteAuditEventRequest request, CancellationToken _) => new AuditEventRecord(
                Guid.NewGuid(),
                now,
                request.ActorType,
                request.ActorId,
                request.Action,
                request.TargetType,
                request.TargetId,
                request.Outcome,
                request.CorrelationId,
                request.RunId,
                request.Data.GetRawText()));
        var service = new WakeRemoteAvailabilityWindowService(
            configurations.Object,
            schedules.Object,
            validator.Object,
            audit.Object,
            time);

        AgentScheduleRecord schedule = await service.SaveAsync(new(
            ScheduleId: null,
            "Weekday remote window",
            new TimeOnly(7, 30),
            [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            Duration: TimeSpan.FromHours(3),
            RemoteProviderRequired: true,
            MisfirePolicy.RunImmediately,
            MisfireGracePeriod: TimeSpan.FromMinutes(10),
            StartupGrace: TimeSpan.FromMinutes(5),
            IsEnabled: true,
            ActorId: "test-user",
            CorrelationId: Guid.NewGuid()));
        string occurrenceJson = new WakeRemoteOccurrenceArgumentsProvider().CreateArguments(schedule, due);
        using JsonDocument occurrenceDocument = JsonDocument.Parse(occurrenceJson);
        (WakeRemoteOccurrenceArguments? occurrence, IReadOnlyList<WakeRemoteValidationError> errors) =
            WakeRemoteInput.ParseOccurrence(
                occurrenceDocument.RootElement,
                WakeRemoteWorkflowTests.CreateConfiguration(),
                due);

        Assert.Multiple(() =>
        {
            Assert.That(schedule.Kind, Is.EqualTo(ScheduleKind.SelectedWeekdays));
            Assert.That(schedule.WakePolicy, Is.EqualTo(WakePolicy.Required));
            Assert.That(schedule.CommandName, Is.EqualTo("run"));
            Assert.That(schedule.ConcurrencyPolicy, Is.EqualTo(ConcurrencyPolicy.Forbid));
            Assert.That(schedule.Timeout, Is.EqualTo(TimeSpan.FromHours(3).Add(TimeSpan.FromMinutes(5))));
            Assert.That(occurrence, Is.Not.Null);
            Assert.That(errors, Is.Empty);
            Assert.That(occurrence!.ScheduledWakeAtUtc, Is.EqualTo(due));
            Assert.That(occurrence.AvailableUntilUtc, Is.EqualTo(due.AddHours(3)));
            Assert.That(occurrence.RemoteProviderRequired, Is.True);
            Assert.That(occurrence.AllowImmediateMisfire, Is.True);
            Assert.That(occurrence.WindowInstanceId, Does.StartWith(schedule.Id.ToString("N")));
        });
    }

    [Test]
    public async Task PlannerCapturesWakeFlagsAndOccurrenceSpecificArguments()
    {
        DateTimeOffset now = new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero);
        DateTimeOffset due = now.AddMinutes(2);
        AgentConfigurationRecord configuration = CreateConfigurationRecord(now);
        AgentScheduleRecord schedule = CreateSchedule(now);
        var calculator = new Mock<IScheduleCalculator>(MockBehavior.Strict);
        calculator
            .Setup(value => value.Calculate(
                It.IsAny<ScheduleDefinition>(),
                schedule.TimeZoneId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>()))
            .Returns([due]);
        var occurrences = new Mock<IOccurrenceRepository>(MockBehavior.Strict);
        occurrences
            .Setup(repository => repository.GetLatestForScheduleAsync(schedule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduleOccurrenceRecord?)null);
        CreateOccurrenceRequest? captured = null;
        occurrences
            .Setup(repository => repository.CreateIfAbsentAsync(
                It.IsAny<CreateOccurrenceRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateOccurrenceRequest request, CancellationToken _) =>
            {
                captured = request;
                return ToRecord(request, now);
            });
        var configurations = new Mock<IAgentConfigurationService>(MockBehavior.Strict);
        configurations
            .Setup(service => service.GetCurrentAsync(WakeRemoteDefaults.AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuration);
        var planner = new ScheduleOccurrencePlanner(
            calculator.Object,
            occurrences.Object,
            configurations.Object,
            SchedulingOptions.Default,
            new WakeRemoteOccurrenceArgumentsProvider());

        OccurrencePlanningResult result = await planner.PlanAsync(schedule, now);
        using JsonDocument document = JsonDocument.Parse(captured!.ArgumentsJson);
        (WakeRemoteOccurrenceArguments? arguments, _) = WakeRemoteInput.ParseOccurrence(
            document.RootElement,
            WakeRemoteWorkflowTests.CreateConfiguration(),
            due);

        Assert.Multiple(() =>
        {
            Assert.That(result.OccurrencesCreated, Is.EqualTo(1));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.RequiresWake, Is.True);
            Assert.That(captured.KeepSystemAwake, Is.True);
            Assert.That(captured.KeepDisplayOn, Is.False);
            Assert.That(arguments, Is.Not.Null);
            Assert.That(arguments!.AvailableUntilUtc, Is.EqualTo(due.AddMinutes(30)));
        });
    }

    private static AgentConfigurationRecord CreateConfigurationRecord(DateTimeOffset now)
    {
        Guid revisionId = Guid.NewGuid();
        string json = JsonSerializer.Serialize(
            WakeRemoteWorkflowTests.CreateConfiguration(),
            WakeRemoteWorkflowTests.JsonOptions);
        var revision = new ConfigurationRevisionRecord(
            revisionId,
            WakeRemoteDefaults.AgentId,
            RevisionNumber: 1,
            SchemaVersion: "1.0",
            CanonicalConfigurationJson: json,
            ConfigurationHash: new string('a', 64),
            ChangedBy: "test",
            ChangeSummary: "test",
            CreatedAtUtc: now);
        return new(
            Guid.NewGuid(),
            WakeRemoteDefaults.AgentId,
            revisionId,
            "1.0",
            revision,
            now,
            ConcurrencyToken: 1);
    }

    private static AgentScheduleRecord CreateSchedule(DateTimeOffset now)
    {
        string template = JsonSerializer.Serialize(
            new WakeRemoteWindowTemplate(1_800, RemoteProviderRequired: true, AllowImmediateMisfire: true),
            WakeRemoteWorkflowTests.JsonOptions);
        return new(
            Guid.NewGuid(),
            WakeRemoteDefaults.AgentId,
            "Daily remote window",
            "run",
            template,
            ScheduleKind.Daily,
            ScheduleDefinitionJson.Serialize(new DailyScheduleDefinition(new TimeOnly(7, 30))),
            TimeZoneInfo.Local.Id,
            MisfirePolicy.RunImmediately,
            ConcurrencyPolicy.Forbid,
            TimeSpan.FromMinutes(35),
            TimeSpan.FromMinutes(10),
            RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
            WakePolicy.Required,
            PinnedConfigurationRevisionId: null,
            AllowDisabledAgent: false,
            IsEnabled: true,
            IsPaused: false,
            PausedUntilUtc: null,
            now,
            now,
            ConcurrencyToken: 1);
    }

    private static ScheduleOccurrenceRecord ToRecord(CreateOccurrenceRequest request, DateTimeOffset now) => new(
        request.Id,
        request.ScheduleId,
        request.AgentId,
        request.CommandName,
        request.ArgumentsJson,
        request.ConfigurationRevisionId,
        request.DueAtUtc,
        request.TriggerType,
        request.InitialStatus,
        request.RequiresWake,
        request.KeepSystemAwake,
        request.KeepDisplayOn,
        request.AttemptNumber,
        request.ParentOccurrenceId,
        ClaimedBy: null,
        ClaimedAtUtc: null,
        ClaimExpiresAtUtc: null,
        StartedAtUtc: null,
        CompletedAtUtc: null,
        CancellationRequestedAtUtc: null,
        CancellationReason: null,
        TerminalReasonCode: null,
        TerminalMessage: null,
        RunId: null,
        CreatedAtUtc: now,
        UpdatedAtUtc: now);
}
