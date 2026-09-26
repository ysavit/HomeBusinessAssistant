namespace HomeBusinessAssistant.Application.Operations;

/// <summary>Operator-facing severity for durable local attention.</summary>
public enum AttentionSeverity
{
    /// <summary>Informational awareness.</summary>
    Info,
    /// <summary>Action may soon be required.</summary>
    Warning,
    /// <summary>An operation failed or is blocked.</summary>
    Error,
    /// <summary>Immediate operator action is required.</summary>
    Critical,
}

/// <summary>Lifecycle of a durable attention condition.</summary>
public enum AttentionStatus
{
    /// <summary>The condition is currently observed.</summary>
    Active,
    /// <summary>An operator has seen the still-active condition.</summary>
    Acknowledged,
    /// <summary>The detector observed recovery.</summary>
    Resolved,
    /// <summary>The condition is intentionally hidden.</summary>
    Suppressed,
}

/// <summary>Lifecycle of a durable local notification.</summary>
public enum LocalNotificationStatus
{
    /// <summary>Waiting for an interactive tray delivery.</summary>
    Pending,
    /// <summary>Accepted by the local tray.</summary>
    Delivered,
    /// <summary>Delivery is no longer eligible.</summary>
    Expired,
    /// <summary>Intentionally not presented.</summary>
    Suppressed,
}

/// <summary>One safe, deduplicated condition requiring local operator awareness.</summary>
public sealed record AttentionItem(
    Guid Id,
    string Category,
    AttentionSeverity Severity,
    string Title,
    string Message,
    string? AgentId,
    Guid? RunId,
    Guid? ScheduleId,
    string DedupeKey,
    AttentionStatus Status,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    int OccurrenceCount,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedAtUtc,
    string ContextJson,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>One persisted tray-delivery request.</summary>
public sealed record LocalNotification(
    Guid Id,
    Guid? AttentionItemId,
    string Title,
    string Message,
    AttentionSeverity Severity,
    string? LocalPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset? DeliveredAtUtc,
    int DeliveryAttempts,
    LocalNotificationStatus Status,
    string DedupeKey,
    string ThrottleKey,
    DateTimeOffset? ExpiresAtUtc,
    string? LastErrorCode);

/// <summary>One deterministic local-day operational digest.</summary>
public sealed record DailySummary(
    Guid Id,
    DateOnly LocalDate,
    string TimeZoneId,
    string SchemaVersion,
    string SummaryText,
    string SummaryJson,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    int GenerationNumber,
    string SourceHash);

/// <summary>Configuration for local notification policy and daily summaries.</summary>
public sealed record OperationalPolicy(
    bool NotificationsEnabled,
    TimeOnly QuietHoursStartLocal,
    TimeOnly QuietHoursEndLocal,
    int GlobalThrottleSeconds,
    int PerKeyThrottleMinutes,
    TimeOnly DailySummaryTimeLocal,
    int FailureRepeatThreshold,
    long LowDiskThresholdBytes,
    int NotificationRetentionDays,
    int ResolvedAttentionRetentionDays,
    int DailySummaryRetentionDays)
{
    /// <summary>Gets the conservative V1 local policy.</summary>
    public static OperationalPolicy Default { get; } = new(
        true,
        new TimeOnly(22, 0),
        new TimeOnly(7, 0),
        15,
        30,
        new TimeOnly(8, 0),
        3,
        2L * 1024 * 1024 * 1024,
        30,
        90,
        365);
}

/// <summary>Counts produced by one deterministic attention scan.</summary>
public sealed record AttentionScanResult(int Observed, int Created, int Updated, int Resolved, int NotificationsQueued);

/// <summary>Preview or result of conservative local retention.</summary>
public sealed record RetentionResult(bool DryRun, int Considered, int Deleted, int Skipped, int Failed, long BytesReclaimed, IReadOnlyList<string> ReasonCodes);

/// <summary>Safe diagnostics archive returned to the loopback UI.</summary>
public sealed record DiagnosticArchive(byte[] Content, string FileName, string Sha256, long SizeBytes);

/// <summary>A safe external-agent condition contributed to the central detector.</summary>
public sealed record OperationalSignal(
    string Category,
    AttentionSeverity Severity,
    string Title,
    string Message,
    string? AgentId,
    Guid? RunId,
    Guid? ScheduleId,
    string DedupeKey,
    string LocalPath,
    object Context);

/// <summary>Optional bounded source for agent-owned operational signals.</summary>
public interface IOperationalSignalSource
{
    /// <summary>Reads current safe signals without copying agent-owned persistence.</summary>
    ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Durable operational query/command boundary.</summary>
public interface IOperationalService
{
    /// <summary>Reads bounded attention history.</summary>
    ValueTask<IReadOnlyList<AttentionItem>> GetAttentionAsync(bool includeResolved = false, int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Acknowledges an active condition without resolving it.</summary>
    ValueTask<AttentionItem?> AcknowledgeAsync(Guid id, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Reads recent durable notifications.</summary>
    ValueTask<IReadOnlyList<LocalNotification>> GetNotificationsAsync(int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Reads notifications eligible under quiet-hour and throttle policy.</summary>
    ValueTask<IReadOnlyList<LocalNotification>> GetDeliverableNotificationsAsync(int take = 10, CancellationToken cancellationToken = default);
    /// <summary>Marks a notification accepted by the interactive tray.</summary>
    ValueTask MarkNotificationDeliveredAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Records a bounded retryable delivery failure.</summary>
    ValueTask MarkNotificationAttemptFailedAsync(Guid id, string errorCode, CancellationToken cancellationToken = default);
    /// <summary>Runs one deterministic signal scan.</summary>
    ValueTask<AttentionScanResult> DetectAsync(CancellationToken cancellationToken = default);
    /// <summary>Generates a new immutable daily-summary revision.</summary>
    ValueTask<DailySummary> GenerateDailySummaryAsync(DateOnly localDate, string timeZoneId, CancellationToken cancellationToken = default);
    /// <summary>Regenerates one terminal run's platform summary from persisted source records.</summary>
    ValueTask<string?> RegenerateRunSummaryAsync(Guid runId, CancellationToken cancellationToken = default);
    /// <summary>Reads recent daily-summary revisions.</summary>
    ValueTask<IReadOnlyList<DailySummary>> GetDailySummariesAsync(int take = 30, CancellationToken cancellationToken = default);
    /// <summary>Previews or applies conservative local retention.</summary>
    ValueTask<RetentionResult> ApplyRetentionAsync(bool dryRun, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Creates a bounded redacted diagnostic archive.</summary>
    ValueTask<DiagnosticArchive> CreateDiagnosticsAsync(CancellationToken cancellationToken = default);
    /// <summary>Gets the effective operational policy.</summary>
    OperationalPolicy Policy { get; }
}
