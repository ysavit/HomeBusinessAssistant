using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Wake;

/// <summary>Lease-fenced reconciliation of the single managed Windows wake task.</summary>
public sealed class WakeTaskReconciler(
    ILeaseManager leases,
    IOccurrenceRepository occurrences,
    IScheduleRepository schedules,
    ISystemSettingRepository settings,
    IAuditWriter auditWriter,
    IWakeTaskSchedulerBridge bridge,
    IScheduleTimeZoneService timeZones,
    TimeProvider timeProvider,
    WakeTaskReconciliationOptions options) : IWakeTaskReconciler
{
    private const string LeaseName = "wake:reconciliation";
    private const string SettingKey = "wake.next-task";

    /// <inheritdoc />
    public async ValueTask<WakeTaskReconciliationResult> ReconcileAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128)
        {
            throw new ArgumentException("A bounded wake reconciliation owner is required.", nameof(ownerId));
        }

        options.Validate();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        AgentLeaseRecord? lease = await leases.TryAcquireAsync(
            LeaseName,
            ownerId,
            nowUtc,
            options.LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return new(false, false, false, null, null, null, null);
        }

        try
        {
            WakeTaskState current = await bridge.GetStateAsync(cancellationToken).ConfigureAwait(false);
            ScheduleOccurrenceRecord? occurrence = await occurrences.GetEarliestWakeAsync(
                nowUtc,
                cancellationToken).ConfigureAwait(false);
            if (occurrence is null)
            {
                bool removalChanged = current.Exists || await IsSettingRegisteredAsync(cancellationToken).ConfigureAwait(false);
                if (current.Exists)
                {
                    await bridge.RemoveAsync(cancellationToken).ConfigureAwait(false);
                }

                if (removalChanged)
                {
                    await SaveSettingAsync(request: null, nowUtc, cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync(
                        "wake-task.removed",
                        AuditOutcome.Succeeded,
                        targetId: ManagedWakeTask.Name,
                        new { reason = "no-pending-wake-occurrence" },
                        cancellationToken).ConfigureAwait(false);
                }

                return new(true, removalChanged, current.Exists, null, null, null, null);
            }

            WakeTaskRequest request = await BuildRequestAsync(occurrence, nowUtc, cancellationToken).ConfigureAwait(false);
            string fingerprint = request.GetFingerprint();
            bool taskMatches = current.Exists
                && current.IsManaged
                && string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal)
                && current.OccurrenceId == occurrence.Id;
            bool settingMatches = await SettingMatchesAsync(request, fingerprint, cancellationToken).ConfigureAwait(false);
            bool changed = !taskMatches;
            if (changed)
            {
                await bridge.ReconcileAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (changed || !settingMatches)
            {
                await SaveSettingAsync(request, nowUtc, cancellationToken).ConfigureAwait(false);
            }

            if (changed)
            {
                await WriteAuditAsync(
                    "wake-task.reconciled",
                    AuditOutcome.Succeeded,
                    occurrence.Id.ToString(),
                    new
                    {
                        occurrenceId = occurrence.Id.ToString(),
                        configurationRevisionId = occurrence.ConfigurationRevisionId,
                        dueAtUtc = occurrence.DueAtUtc,
                        fingerprint,
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return new(
                true,
                changed,
                false,
                occurrence.Id,
                occurrence.DueAtUtc,
                fingerprint,
                null);
        }
        catch (WakeTaskBridgeException exception)
        {
            await WriteAuditAsync(
                "wake-task.reconciliation-failed",
                AuditOutcome.Failed,
                ManagedWakeTask.Name,
                new
                {
                    exception.Error.Code,
                    category = exception.Error.Category.ToString(),
                    exception.Error.NativeExitCode,
                },
                CancellationToken.None).ConfigureAwait(false);
            return new(true, false, false, null, null, null, exception.Error);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            var error = new WakeTaskError(
                "wake.reconciliation-invalid",
                WakeTaskErrorCategory.InvalidRequest,
                "The pending wake occurrence could not be converted into a safe task request.");
            await WriteAuditAsync(
                "wake-task.reconciliation-failed",
                AuditOutcome.Failed,
                ManagedWakeTask.Name,
                new { error.Code, failureType = exception.GetType().Name },
                CancellationToken.None).ConfigureAwait(false);
            return new(true, false, false, null, null, null, error);
        }
        finally
        {
            _ = await leases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<WakeTaskRequest> BuildRequestAsync(
        ScheduleOccurrenceRecord occurrence,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!occurrence.RequiresWake
            || occurrence.Status is not (OccurrenceStatus.Planned or OccurrenceStatus.Ready)
            || occurrence.DueAtUtc < nowUtc.Subtract(options.MaximumStaleness))
        {
            throw new InvalidOperationException("The occurrence is not a valid pending wake request.");
        }

        AgentScheduleRecord? schedule = occurrence.ScheduleId.HasValue
            ? await schedules.GetAsync(occurrence.ScheduleId.Value, cancellationToken).ConfigureAwait(false)
            : null;
        if (occurrence.ScheduleId.HasValue
            && (schedule is null
                || !schedule.IsEnabled
                || ScheduleOccurrencePlanner.IsEffectivelyPaused(schedule, nowUtc)
                || schedule.WakePolicy == WakePolicy.Never))
        {
            throw new InvalidOperationException("The occurrence schedule no longer permits wake registration.");
        }

        string timeZoneId = schedule?.TimeZoneId ?? options.DiagnosticTimeZoneId;
        if (!timeZones.TimeZoneExists(timeZoneId))
        {
            throw new InvalidOperationException("The wake diagnostic time zone is unavailable.");
        }

        DateTimeOffset dueLocal = timeZones.ConvertUtcToLocal(occurrence.DueAtUtc, timeZoneId);
        TimeSpan executionTimeout = (schedule?.Timeout ?? TimeSpan.FromMinutes(5)).Add(options.TaskTimeoutPadding);
        if (executionTimeout < TimeSpan.FromSeconds(1) || executionTimeout > TimeSpan.FromHours(25))
        {
            throw new InvalidOperationException("The managed task execution timeout is outside supported bounds.");
        }

        var request = new WakeTaskRequest(
            occurrence.Id,
            occurrence.ConfigurationRevisionId,
            occurrence.DueAtUtc.ToUniversalTime(),
            dueLocal,
            timeZoneId,
            Path.GetFullPath(options.RunnerExecutablePath),
            Path.GetFullPath(options.RunnerWorkingDirectory),
            options.Bootstrap with
            {
                DataDirectory = Path.GetFullPath(options.Bootstrap.DataDirectory),
                AgentDirectory = Path.GetFullPath(options.Bootstrap.AgentDirectory),
                ManifestDirectory = Path.GetFullPath(options.Bootstrap.ManifestDirectory),
            },
            executionTimeout,
            options.CurrentUserId,
            WakeTaskUserSessionPolicy.CurrentInteractiveUser,
            schedule is null || schedule.MisfirePolicy == MisfirePolicy.RunImmediately
                ? options.StartWhenAvailable
                : false,
            options.AllowStartOnBatteries,
            options.StopIfGoingOnBatteries,
            occurrence.Id.Value);
        ValidateRequest(request, nowUtc);
        return request;
    }

    private void ValidateRequest(WakeTaskRequest request, DateTimeOffset nowUtc)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ApplicationRoot));
        string runner = Path.GetFullPath(request.RunnerExecutablePath);
        if (!runner.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || request.ConfigurationRevisionId == Guid.Empty
            || request.CorrelationId == Guid.Empty
            || request.DueAtUtc.Offset != TimeSpan.Zero
            || request.DueAtUtc < nowUtc.Subtract(options.MaximumStaleness)
            || !Enum.IsDefined(request.UserSessionPolicy))
        {
            throw new InvalidOperationException("The generated wake-task request is invalid.");
        }
    }

    private async ValueTask<bool> IsSettingRegisteredAsync(CancellationToken cancellationToken)
    {
        SystemSettingRecord? setting = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(setting.ValueJson);
            return document.RootElement.TryGetProperty("registered", out JsonElement registered)
                && registered.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private async ValueTask<bool> SettingMatchesAsync(
        WakeTaskRequest request,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        SystemSettingRecord? setting = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(setting.ValueJson);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("registered", out JsonElement registered)
                && registered.ValueKind == JsonValueKind.True
                && root.TryGetProperty("occurrenceId", out JsonElement occurrence)
                && occurrence.GetString() == request.OccurrenceId.ToString()
                && root.TryGetProperty("fingerprint", out JsonElement storedFingerprint)
                && string.Equals(storedFingerprint.GetString(), fingerprint, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async ValueTask SaveSettingAsync(
        WakeTaskRequest? request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        SystemSettingRecord? existing = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        string value = JsonSerializer.Serialize(request is null
            ? new
            {
                version = "1.0",
                registered = false,
            }
            : (object)new
            {
                version = "1.0",
                registered = true,
                occurrenceId = request.OccurrenceId.ToString(),
                configurationRevisionId = request.ConfigurationRevisionId,
                dueAtUtc = request.DueAtUtc,
                fingerprint = request.GetFingerprint(),
            });
        _ = await settings.SaveAsync(new(
            SettingKey,
            value,
            "1.0",
            nowUtc,
            existing?.ConcurrencyToken ?? 0), existing?.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteAuditAsync(
        string action,
        AuditOutcome outcome,
        string targetId,
        object data,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.System,
            "wake-reconciler",
            action,
            "windows-task",
            targetId,
            outcome,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);
}
