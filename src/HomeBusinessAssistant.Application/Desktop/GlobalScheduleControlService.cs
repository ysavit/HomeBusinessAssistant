using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;

namespace HomeBusinessAssistant.Application.Desktop;

/// <summary>Persists and applies one global schedule-pause intent using existing audited controls.</summary>
public sealed class GlobalScheduleControlService(
    IScheduleRepository schedules,
    IScheduleControlService controls,
    ISystemSettingRepository settings,
    TimeProvider timeProvider) : IGlobalScheduleControlService
{
    private const string SettingKey = "host.global-schedule-pause";

    /// <inheritdoc />
    public async ValueTask<GlobalSchedulePauseState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        SystemSettingRecord? setting = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            return new(false, []);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(setting.ValueJson, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            bool paused = root.TryGetProperty("isPaused", out JsonElement pausedElement)
                && pausedElement.ValueKind == JsonValueKind.True;
            Guid[] changed = root.TryGetProperty("changedScheduleIds", out JsonElement ids)
                && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray()
                    .Select(item => Guid.TryParse(item.GetString(), out Guid id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .Take(1_000)
                    .ToArray()
                : [];
            return new(paused, changed);
        }
        catch (JsonException)
        {
            return new(false, []);
        }
    }

    /// <inheritdoc />
    public async ValueTask<GlobalSchedulePauseState> PauseAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        GlobalSchedulePauseState currentState = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (currentState.IsPaused)
        {
            return currentState;
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        AgentScheduleRecord[] changed = (await schedules.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
            .Where(schedule => !ScheduleOccurrencePlanner.IsEffectivelyPaused(schedule, nowUtc))
            .ToArray();
        var applied = new List<Guid>(changed.Length);
        try
        {
            foreach (AgentScheduleRecord schedule in changed)
            {
                _ = await controls.PauseAsync(
                    schedule.Id,
                    untilUtc: null,
                    actorId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
                applied.Add(schedule.Id);
            }

            return await SaveStateAsync(true, applied, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (Guid scheduleId in applied)
            {
                try
                {
                    _ = await controls.ResumeAsync(scheduleId, actorId, correlationId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A later explicit resume can safely reconcile a rare partial rollback.
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<GlobalSchedulePauseState> ResumeAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        GlobalSchedulePauseState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsPaused)
        {
            return state;
        }

        foreach (Guid scheduleId in state.ChangedScheduleIds)
        {
            AgentScheduleRecord? schedule = await schedules.GetAsync(scheduleId, cancellationToken).ConfigureAwait(false);
            if (schedule is { IsPaused: true, PausedUntilUtc: null })
            {
                _ = await controls.ResumeAsync(
                    scheduleId,
                    actorId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return await SaveStateAsync(false, [], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GlobalSchedulePauseState> SaveStateAsync(
        bool isPaused,
        IReadOnlyList<Guid> changedScheduleIds,
        CancellationToken cancellationToken)
    {
        SystemSettingRecord? current = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        string json = JsonSerializer.Serialize(new
        {
            version = "1.0",
            isPaused,
            changedScheduleIds,
        });
        _ = await settings.SaveAsync(new(
            SettingKey,
            json,
            "1.0",
            nowUtc,
            current?.ConcurrencyToken ?? 0), current?.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
        return new(isPaused, changedScheduleIds.ToArray());
    }
}
