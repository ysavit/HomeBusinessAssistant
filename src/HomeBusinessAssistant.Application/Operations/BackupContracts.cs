namespace HomeBusinessAssistant.Application.Operations;

/// <summary>Retention applied after a completed database backup is promoted.</summary>
public sealed record BackupRetentionPolicy(int DailySets = 7, int WeeklySets = 4)
{
    /// <summary>Validates conservative V1 bounds.</summary>
    public void Validate()
    {
        if (DailySets is < 1 or > 365 || WeeklySets is < 0 or > 104)
        {
            throw new ArgumentOutOfRangeException(nameof(DailySets), "Backup retention is outside the supported range.");
        }
    }
}

/// <summary>Integrity and schema metadata for one database in a backup set.</summary>
public sealed record BackupDatabaseEntry(
    string Name,
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string LatestMigration,
    int TableCount);

/// <summary>Versioned manifest promoted only after a complete backup validates.</summary>
public sealed record BackupSetManifest(
    string SchemaVersion,
    string BackupSetId,
    DateTimeOffset CreatedAtUtc,
    string ProductVersion,
    string GitCommit,
    string BuildUtc,
    string RuntimeTarget,
    IReadOnlyList<BackupDatabaseEntry> Databases,
    IReadOnlyList<string> Exclusions);

/// <summary>Bounded list projection for one completed backup set.</summary>
public sealed record BackupSetInfo(
    string BackupSetId,
    DateTimeOffset CreatedAtUtc,
    string ProductVersion,
    long SizeBytes,
    bool IsValid,
    string? ValidationCode);

/// <summary>Result of creating and retaining one consistent backup set.</summary>
public sealed record BackupCreateResult(
    BackupSetInfo BackupSet,
    int RemovedByRetention,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings);

/// <summary>Result of opening and validating a completed backup set.</summary>
public sealed record BackupValidationResult(
    string BackupSetId,
    bool IsValid,
    string Code,
    BackupSetManifest? Manifest,
    IReadOnlyList<string> Errors);

/// <summary>Explicit guarded restore request.</summary>
public sealed record BackupRestoreRequest(
    string BackupSetId,
    string ConfirmationToken,
    bool MaintenanceMode,
    string ActorId);

/// <summary>Observable result of restoring both databases.</summary>
public sealed record BackupRestoreResult(
    string BackupSetId,
    string PreRestoreBackupSetId,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    string Code);

/// <summary>Safe two-database backup and restore boundary.</summary>
public interface IDatabaseBackupService
{
    /// <summary>Creates, verifies, promotes, audits, and retains one backup set.</summary>
    ValueTask<BackupCreateResult> CreateAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>Lists completed sets; pending/interrupted directories are invisible.</summary>
    ValueTask<IReadOnlyList<BackupSetInfo>> ListAsync(int maximumResults, CancellationToken cancellationToken = default);

    /// <summary>Reopens and verifies one completed set.</summary>
    ValueTask<BackupValidationResult> ValidateAsync(string backupSetId, CancellationToken cancellationToken = default);

    /// <summary>Restores both databases only in explicit exclusive maintenance mode.</summary>
    ValueTask<BackupRestoreResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default);
}
