using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class SchedulingTests
{
    private const string CentralTimeZoneId = "Central Standard Time";
    private readonly ScheduleTimeZoneService timeZones = new();

    public static IEnumerable<ScheduleDefinition> ValidDefinitions()
    {
        yield return new ManualScheduleDefinition();
        yield return OneTimeScheduleDefinition.AtLocal(new DateTime(2026, 9, 1, 9, 30, 0));
        yield return OneTimeScheduleDefinition.AtUtc(new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero));
        yield return new DailyScheduleDefinition(new TimeOnly(9, 30));
        yield return new WeekdayScheduleDefinition([DayOfWeek.Monday, DayOfWeek.Friday], new TimeOnly(8, 15));
        yield return new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(15), new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));
        yield return new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true);
        yield return new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), StartImmediately: false);
    }

    [TestCaseSource(nameof(ValidDefinitions))]
    public void EveryScheduleDefinitionHasStableRoundTrip(ScheduleDefinition definition)
    {
        string json = ScheduleDefinitionJson.Serialize(definition);
        ScheduleDefinitionParseResult parsed = ScheduleDefinitionJson.Parse(json, definition.Kind);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.IsValid, Is.True);
            Assert.That(parsed.Definition?.Kind, Is.EqualTo(definition.Kind));
            Assert.That(ScheduleDefinitionJson.Serialize(parsed.Definition!), Is.EqualTo(json));
            Assert.That(json, Does.Contain("\"version\":\"1.0\""));
        });
    }

    [Test]
    public void PersistedKindMustMatchJsonDiscriminator()
    {
        string json = ScheduleDefinitionJson.Serialize(new DailyScheduleDefinition(new TimeOnly(9, 0)));
        ScheduleDefinitionParseResult parsed = ScheduleDefinitionJson.Parse(json, ScheduleKind.FixedInterval);

        Assert.That(parsed.Errors.Select(item => item.Code), Does.Contain("scheduleDefinition.typeMismatch"));
    }

    [Test]
    public void InvalidDefinitionsAndRetryBoundsReturnPaths()
    {
        IReadOnlyList<ScheduleValidationError> weekdayErrors = ScheduleDefinitionJson.Validate(
            new WeekdayScheduleDefinition([], new TimeOnly(9, 0)));
        IReadOnlyList<ScheduleValidationError> delayErrors = ScheduleDefinitionJson.Validate(
            new FixedDelayScheduleDefinition(TimeSpan.FromSeconds(30), InitialDueAtUtc: null, StartImmediately: true));
        (_, IReadOnlyList<ScheduleValidationError> retryErrors) = RetryPolicyJson.Parse(
            "{\"version\":\"1.0\",\"maximumRetries\":11,\"initialDelay\":\"00:00:01\",\"maximumDelay\":\"00:00:00\"}");

        Assert.Multiple(() =>
        {
            Assert.That(weekdayErrors.Single().Path, Is.EqualTo("$.days"));
            Assert.That(delayErrors.Single().Path, Is.EqualTo("$.delay"));
            Assert.That(retryErrors.Select(item => item.Path), Does.Contain("$.retryPolicy.maximumRetries"));
            Assert.That(retryErrors.Select(item => item.Path), Does.Contain("$.retryPolicy.maximumDelay"));
        });
    }

    [Test]
    public void DstGapAdvancesToFirstValidSecondAndAmbiguityChoosesEarlierUtcInstant()
    {
        LocalTimeConversionResult spring = timeZones.ConvertLocalToUtc(
            new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified),
            CentralTimeZoneId);
        LocalTimeConversionResult fall = timeZones.ConvertLocalToUtc(
            new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified),
            CentralTimeZoneId);

        Assert.Multiple(() =>
        {
            Assert.That(spring.WasInvalidAdjusted, Is.True);
            Assert.That(spring.ResolvedLocalDateTime, Is.EqualTo(new DateTime(2026, 3, 8, 3, 0, 0)));
            Assert.That(spring.UtcDateTime, Is.EqualTo(new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero)));
            Assert.That(fall.WasAmbiguous, Is.True);
            Assert.That(fall.SelectedOffset, Is.EqualTo(TimeSpan.FromHours(-5)));
            Assert.That(fall.UtcDateTime, Is.EqualTo(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void CalendarAndIntervalCalculationsAreBoundedAndUtc()
    {
        var calculator = new ScheduleCalculator(timeZones);
        DateTimeOffset start = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = start.AddDays(3);
        IReadOnlyList<DateTimeOffset> daily = calculator.Calculate(
            new DailyScheduleDefinition(new TimeOnly(9, 0)), CentralTimeZoneId, start, end, 10);
        IReadOnlyList<DateTimeOffset> weekdays = calculator.Calculate(
            new WeekdayScheduleDefinition([DayOfWeek.Monday, DayOfWeek.Wednesday], new TimeOnly(9, 0)),
            CentralTimeZoneId,
            start,
            end,
            10);
        IReadOnlyList<DateTimeOffset> interval = calculator.Calculate(
            new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(30), start),
            CentralTimeZoneId,
            start,
            start.AddHours(2),
            3);

        Assert.Multiple(() =>
        {
            Assert.That(daily, Has.Count.EqualTo(3));
            Assert.That(daily, Is.All.Property("Offset").EqualTo(TimeSpan.Zero));
            Assert.That(weekdays, Has.Count.EqualTo(2));
            Assert.That(interval, Is.EqualTo(new[] { start, start.AddMinutes(30), start.AddHours(1) }));
        });
    }

    [Test]
    public void FixedDelayDoesNotCalculateCadenceFromScheduledSlots()
    {
        var calculator = new ScheduleCalculator(timeZones);
        DateTimeOffset now = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);
        IReadOnlyList<DateTimeOffset> due = calculator.Calculate(
            new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true),
            CentralTimeZoneId,
            now,
            now.AddDays(7),
            100);

        Assert.That(due, Is.EqualTo(new[] { now }));
    }

    [Test]
    public void OccurrenceStateMachineAcceptsDocumentedTransitionsAndRejectsTerminalReuse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OccurrenceStateMachine.CanTransition(OccurrenceStatus.Planned, OccurrenceStatus.Ready), Is.True);
            Assert.That(OccurrenceStateMachine.CanTransition(OccurrenceStatus.Ready, OccurrenceStatus.Claimed), Is.True);
            Assert.That(OccurrenceStateMachine.CanTransition(OccurrenceStatus.Running, OccurrenceStatus.Completed), Is.True);
            Assert.That(OccurrenceStateMachine.CanTransition(OccurrenceStatus.Completed, OccurrenceStatus.Ready), Is.False);
            Assert.That(OccurrenceStateMachine.CanTransition(OccurrenceStatus.Planned, OccurrenceStatus.Completed), Is.False);
            Assert.That(OccurrenceStateMachine.IsTerminal(OccurrenceStatus.Skipped), Is.True);
        });
    }
}
