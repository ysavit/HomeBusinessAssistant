using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class GlobalScheduleControlTests
{
    [Test]
    public async Task ResumeOnlyClearsSchedulesChangedByGlobalPause()
    {
        DateTimeOffset now = new(2026, 8, 30, 18, 0, 0, TimeSpan.Zero);
        AgentScheduleRecord active = CreateSchedule("active", isPaused: false, now);
        AgentScheduleRecord prePaused = CreateSchedule("pre-paused", isPaused: true, now);
        var schedules = new FakeScheduleRepository(active, prePaused);
        var controls = new FakeScheduleControls(schedules);
        var settings = new FakeSystemSettings();
        var service = new GlobalScheduleControlService(schedules, controls, settings, new FixedTimeProvider(now));

        GlobalSchedulePauseState paused = await service.PauseAllAsync("tray", Guid.NewGuid());
        GlobalSchedulePauseState resumed = await service.ResumeAllAsync("tray", Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(paused.IsPaused, Is.True);
            Assert.That(paused.ChangedScheduleIds, Is.EqualTo(new[] { active.Id }));
            Assert.That(resumed.IsPaused, Is.False);
            Assert.That(schedules.Items[active.Id].IsPaused, Is.False);
            Assert.That(schedules.Items[prePaused.Id].IsPaused, Is.True);
            Assert.That(controls.ResumedIds, Is.EqualTo(new[] { active.Id }));
        });
    }

    private static AgentScheduleRecord CreateSchedule(string name, bool isPaused, DateTimeOffset now) => new(
        Guid.NewGuid(),
        AgentId.Parse("wake-remote"),
        name,
        "run",
        "{}",
        ScheduleKind.Manual,
        "{\"version\":\"1.0\",\"type\":\"manual\"}",
        "Central Standard Time",
        MisfirePolicy.RunImmediately,
        ConcurrencyPolicy.Forbid,
        TimeSpan.FromHours(1),
        TimeSpan.FromMinutes(1),
        "{\"version\":\"1.0\",\"maximumAttempts\":1}",
        WakePolicy.Never,
        PinnedConfigurationRevisionId: null,
        AllowDisabledAgent: false,
        IsEnabled: true,
        IsPaused: isPaused,
        PausedUntilUtc: null,
        now,
        now,
        ConcurrencyToken: 1);

    private sealed class FakeScheduleRepository(params AgentScheduleRecord[] schedules) : IScheduleRepository
    {
        public Dictionary<Guid, AgentScheduleRecord> Items { get; } = schedules.ToDictionary(item => item.Id);

        public ValueTask<AgentScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.GetValueOrDefault(scheduleId));

        public ValueTask<IReadOnlyList<AgentScheduleRecord>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentScheduleRecord>>(Items.Values.Where(item => item.IsEnabled).ToArray());

        public ValueTask<IReadOnlyList<AgentScheduleRecord>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentScheduleRecord>>(Items.Values.Where(item => item.IsEnabled && !item.IsPaused).ToArray());

        public ValueTask<AgentScheduleRecord?> GetByAgentAndNameAsync(
            AgentId agentId,
            string name,
            Guid? excludingScheduleId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items.Values.FirstOrDefault(item =>
                item.AgentId == agentId && item.Name == name && item.Id != excludingScheduleId));

        public ValueTask<AgentScheduleRecord> SaveAsync(
            AgentScheduleRecord schedule,
            long? expectedConcurrencyToken,
            CancellationToken cancellationToken = default)
        {
            Items[schedule.Id] = schedule;
            return ValueTask.FromResult(schedule);
        }
    }

    private sealed class FakeScheduleControls(FakeScheduleRepository schedules) : IScheduleControlService
    {
        public List<Guid> ResumedIds { get; } = [];

        public ValueTask<AgentScheduleRecord> SetEnabledAsync(
            Guid scheduleId,
            bool enabled,
            string actorId,
            Guid correlationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<AgentScheduleRecord> PauseAsync(
            Guid scheduleId,
            DateTimeOffset? untilUtc,
            string actorId,
            Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            AgentScheduleRecord changed = schedules.Items[scheduleId] with { IsPaused = true, PausedUntilUtc = untilUtc };
            schedules.Items[scheduleId] = changed;
            return ValueTask.FromResult(changed);
        }

        public ValueTask<AgentScheduleRecord> ResumeAsync(
            Guid scheduleId,
            string actorId,
            Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            ResumedIds.Add(scheduleId);
            AgentScheduleRecord changed = schedules.Items[scheduleId] with { IsPaused = false, PausedUntilUtc = null };
            schedules.Items[scheduleId] = changed;
            return ValueTask.FromResult(changed);
        }
    }

    private sealed class FakeSystemSettings : ISystemSettingRepository
    {
        private SystemSettingRecord? current;

        public ValueTask<SystemSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(current);

        public ValueTask<SystemSettingRecord> SaveAsync(
            SystemSettingRecord setting,
            long? expectedConcurrencyToken,
            CancellationToken cancellationToken = default)
        {
            current = setting with { ConcurrencyToken = (current?.ConcurrencyToken ?? 0) + 1 };
            return ValueTask.FromResult(current);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
