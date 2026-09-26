using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Operations;

/// <summary>Validated local paths and compatibility policy for database backup and restore.</summary>
public sealed record DatabaseBackupOptions(
    string DataDirectory,
    string CentralDatabaseFileName,
    string FounderDatabaseRelativePath,
    ProductBuildInfo Build,
    BackupRetentionPolicy Retention)
{
    /// <summary>Creates production defaults from the central data root.</summary>
    public static DatabaseBackupOptions CreateDefault(
        string dataDirectory,
        string centralDatabaseFileName,
        ProductBuildInfo build) => new(
            dataDirectory,
            centralDatabaseFileName,
            Path.Combine("agents", "founder-scout", "founders.db"),
            build,
            new());
}

/// <summary>Creates consistent manifest-backed SQLite backup sets and guarded two-file restores.</summary>
public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private const string ManifestFileName = "backup-manifest.json";
    private const string ManifestSchemaVersion = "1.0";
    private const string BackupLeaseName = "operations.database-backup";
    private const string RestoreLockFileName = ".database-restore.lock";
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumBusyBackupAttempts = 10;
    private static readonly string[] CentralExpectedTables = ["__EFMigrationsHistory", "AgentDefinitions", "AgentRuns", "AgentLeases"];
    private static readonly string[] FounderExpectedTables = ["__EFMigrationsHistory", "Candidates", "ProfileSnapshots", "FounderScoutLeases"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IDbContextFactory<AssistantDbContext> contextFactory;
    private readonly ILeaseManager leases;
    private readonly IAuditWriter audit;
    private readonly TimeProvider timeProvider;
    private readonly DatabaseBackupOptions options;
    private readonly Func<CancellationToken, ValueTask>? postRestore;

    /// <summary>Creates the service from short-lived persistence adapters.</summary>
    public DatabaseBackupService(
        IDbContextFactory<AssistantDbContext> contextFactory,
        ILeaseManager leases,
        IAuditWriter audit,
        TimeProvider timeProvider,
        DatabaseBackupOptions options,
        Func<CancellationToken, ValueTask>? postRestore = null)
    {
        this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
        this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.options = ValidateOptions(options);
        this.postRestore = postRestore;
    }

    /// <inheritdoc />
    public async ValueTask<BackupCreateResult> CreateAsync(
        string actorId,
        CancellationToken cancellationToken = default) =>
        await CreateAsync(actorId, protectedSetId: null, cancellationToken).ConfigureAwait(false);

    private async ValueTask<BackupCreateResult> CreateAsync(
        string actorId,
        string? protectedSetId,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorId);
        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        string ownerId = $"backup-{Environment.ProcessId}-{Guid.NewGuid():N}";
        AgentLeaseRecord? lease = await leases.TryAcquireAsync(
            BackupLeaseName,
            ownerId,
            startedAtUtc,
            TimeSpan.FromMinutes(15),
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            throw new InvalidOperationException("Another database backup is already active.");
        }

        try
        {
            BackupCreateResult result = await CreateCoreAsync(startedAtUtc, protectedSetId, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(
                actorId,
                "backup.create",
                result.BackupSet.BackupSetId,
                AuditOutcome.Succeeded,
                new
                {
                    result.BackupSet.SizeBytes,
                    durationMilliseconds = (long)result.Duration.TotalMilliseconds,
                    result.RemovedByRetention,
                    productVersion = options.Build.ProductVersion,
                    gitCommit = options.Build.GitCommit,
                },
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or InvalidOperationException)
        {
            await WriteAuditAsync(
                actorId,
                "backup.create",
                null,
                AuditOutcome.Failed,
                new { errorType = exception.GetType().Name },
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _ = await leases.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<BackupSetInfo>> ListAsync(
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        if (maximumResults is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        string root = PrepareBackupRoot();
        var results = new List<BackupSetInfo>();
        foreach (string directory in Directory.EnumerateDirectories(root)
                     .Where(path => !Path.GetFileName(path).StartsWith(".pending-", StringComparison.Ordinal))
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Take(maximumResults))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = Path.GetFileName(directory);
            BackupValidationResult validation = await ValidateAsync(id, cancellationToken).ConfigureAwait(false);
            BackupSetManifest? manifest = validation.Manifest;
            results.Add(new(
                id,
                manifest?.CreatedAtUtc ?? Directory.GetCreationTimeUtc(directory),
                manifest?.ProductVersion ?? "unknown",
                manifest?.Databases.Sum(item => item.SizeBytes) ?? 0,
                validation.IsValid,
                validation.IsValid ? null : validation.Code));
        }

        return results;
    }

    /// <inheritdoc />
    public async ValueTask<BackupValidationResult> ValidateAsync(
        string backupSetId,
        CancellationToken cancellationToken = default)
    {
        ValidateBackupSetId(backupSetId);
        string directory = GetBackupSetDirectory(backupSetId);
        string manifestPath = Path.Combine(directory, ManifestFileName);
        var errors = new List<string>();
        BackupSetManifest? manifest = null;
        try
        {
            if (!File.Exists(manifestPath))
            {
                return new(backupSetId, false, "backup.manifest.missing", null, ["The backup manifest is missing."]);
            }

            var info = new FileInfo(manifestPath);
            if (info.Length is <= 0 or > MaximumManifestBytes)
            {
                return new(backupSetId, false, "backup.manifest.size", null, ["The backup manifest size is invalid."]);
            }

            manifest = JsonSerializer.Deserialize<BackupSetManifest>(
                await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false),
                JsonOptions);
            if (manifest is null
                || manifest.SchemaVersion != ManifestSchemaVersion
                || manifest.BackupSetId != backupSetId
                || manifest.Databases.Count != 2
                || manifest.Databases.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != 2)
            {
                return new(backupSetId, false, "backup.manifest.invalid", manifest, ["The backup manifest shape is invalid."]);
            }

            foreach (BackupDatabaseEntry entry in manifest.Databases)
            {
                string path = CombineContained(directory, entry.RelativePath);
                if (!File.Exists(path))
                {
                    errors.Add($"Database '{entry.Name}' is missing.");
                    continue;
                }

                var databaseInfo = new FileInfo(path);
                string hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
                if (databaseInfo.Length != entry.SizeBytes || !string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
                {
                    errors.Add($"Database '{entry.Name}' failed its size or hash check.");
                    continue;
                }

                string[] expected = entry.Name switch
                {
                    "assistant" => CentralExpectedTables,
                    "founder-scout" => FounderExpectedTables,
                    _ => [],
                };
                DatabaseInspection inspection = await InspectDatabaseAsync(path, expected, cancellationToken).ConfigureAwait(false);
                if (!inspection.IsValid
                    || inspection.LatestMigration != entry.LatestMigration
                    || inspection.TableCount != entry.TableCount)
                {
                    errors.Add($"Database '{entry.Name}' failed schema validation.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or SqliteException or InvalidOperationException)
        {
            errors.Add($"Validation failed with {exception.GetType().Name}.");
        }

        return errors.Count == 0
            ? new(backupSetId, true, "backup.valid", manifest, [])
            : new(backupSetId, false, "backup.validation.failed", manifest, errors);
    }

    /// <inheritdoc />
    public async ValueTask<BackupRestoreResult> RestoreAsync(
        BackupRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBackupSetId(request.BackupSetId);
        ValidateActor(request.ActorId);
        if (!request.MaintenanceMode || !string.Equals(request.ConfirmationToken, "RESTORE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Restore requires maintenance mode and the explicit RESTORE confirmation token.");
        }

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (IsAnyRuntimeProcessActive())
        {
            throw new InvalidOperationException("Restore is refused while the Host or an agent process is active.");
        }

        string dataRoot = Path.GetFullPath(options.DataDirectory);
        string restoreLockPath = CombineContained(dataRoot, RestoreLockFileName);
        await using FileStream restoreLock = new(
            restoreLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.Asynchronous);
        if (await HasActiveRunsAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Restore is refused while a run is active.");
        }

        BackupValidationResult validation = await ValidateAsync(request.BackupSetId, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidDataException("The requested backup set did not pass validation.");
        }

        if (CompareProductVersions(validation.Manifest.ProductVersion, options.Build.ProductVersion) > 0)
        {
            throw new InvalidDataException("The backup was created by a newer incompatible product version.");
        }

        BackupCreateResult preRestore = await CreateAsync(
            "runner-pre-restore",
            request.BackupSetId,
            cancellationToken).ConfigureAwait(false);
        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string stagingRoot = CombineContained(dataRoot, "temp", $"restore-{operationId}");
        Directory.CreateDirectory(stagingRoot);
        string centralPath = CombineContained(dataRoot, options.CentralDatabaseFileName);
        string founderPath = CombineContained(dataRoot, options.FounderDatabaseRelativePath);
        string centralStage = Path.Combine(stagingRoot, "assistant.db");
        string founderStage = Path.Combine(stagingRoot, "founders.db");
        string centralRollback = Path.Combine(stagingRoot, "assistant.rollback.db");
        string founderRollback = Path.Combine(stagingRoot, "founders.rollback.db");
        var replaced = new List<(string Current, string Rollback)>();
        try
        {
            BackupDatabaseEntry centralEntry = validation.Manifest.Databases.Single(item => item.Name == "assistant");
            BackupDatabaseEntry founderEntry = validation.Manifest.Databases.Single(item => item.Name == "founder-scout");
            File.Copy(CombineContained(GetBackupSetDirectory(request.BackupSetId), centralEntry.RelativePath), centralStage, overwrite: false);
            File.Copy(CombineContained(GetBackupSetDirectory(request.BackupSetId), founderEntry.RelativePath), founderStage, overwrite: false);
            _ = await InspectDatabaseAsync(centralStage, CentralExpectedTables, cancellationToken).ConfigureAwait(false);
            _ = await InspectDatabaseAsync(founderStage, FounderExpectedTables, cancellationToken).ConfigureAwait(false);

            SqliteConnection.ClearAllPools();
            DeleteSidecars(centralPath);
            DeleteSidecars(founderPath);
            File.Replace(centralStage, centralPath, centralRollback, ignoreMetadataErrors: true);
            replaced.Add((centralPath, centralRollback));
            File.Replace(founderStage, founderPath, founderRollback, ignoreMetadataErrors: true);
            replaced.Add((founderPath, founderRollback));
            _ = await InspectDatabaseAsync(centralPath, CentralExpectedTables, cancellationToken).ConfigureAwait(false);
            _ = await InspectDatabaseAsync(founderPath, FounderExpectedTables, cancellationToken).ConfigureAwait(false);
            if (postRestore is not null)
            {
                await postRestore(cancellationToken).ConfigureAwait(false);
            }

            // A backup necessarily captures its own lease row while that lease is live.
            // Expire the restored snapshot's copy before exposing the restored database.
            await ExpireRestoredBackupLeaseAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            DateTimeOffset completedAtUtc = timeProvider.GetUtcNow();
            await WriteAuditAsync(
                request.ActorId,
                "backup.restore",
                request.BackupSetId,
                AuditOutcome.Succeeded,
                new
                {
                    preRestoreBackupSetId = preRestore.BackupSet.BackupSetId,
                    durationMilliseconds = stopwatch.ElapsedMilliseconds,
                    productVersion = options.Build.ProductVersion,
                },
                cancellationToken).ConfigureAwait(false);
            return new(
                request.BackupSetId,
                preRestore.BackupSet.BackupSetId,
                completedAtUtc,
                stopwatch.Elapsed,
                "restore.completed");
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            foreach ((string current, string rollback) in replaced.AsEnumerable().Reverse())
            {
                if (File.Exists(rollback))
                {
                    File.Replace(rollback, current, null, ignoreMetadataErrors: true);
                }
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private async Task ExpireRestoredBackupLeaseAsync(CancellationToken cancellationToken)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await context.Set<AgentLeaseEntity>()
            .Where(item => item.LeaseName == BackupLeaseName)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ExpiresAtUtc, DateTimeOffset.MinValue),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<BackupCreateResult> CreateCoreAsync(
        DateTimeOffset startedAtUtc,
        string? protectedSetId,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string root = PrepareBackupRoot();
        string setId = $"{startedAtUtc:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}";
        string pending = CombineContained(root, $".pending-{setId}");
        string completed = CombineContained(root, setId);
        Directory.CreateDirectory(pending);
        try
        {
            string centralSource = CombineContained(options.DataDirectory, options.CentralDatabaseFileName);
            string founderSource = CombineContained(options.DataDirectory, options.FounderDatabaseRelativePath);
            BackupDatabaseEntry central = await BackupDatabaseAsync(
                "assistant", centralSource, Path.Combine(pending, "assistant.db"), CentralExpectedTables, cancellationToken).ConfigureAwait(false);
            BackupDatabaseEntry founder = await BackupDatabaseAsync(
                "founder-scout", founderSource, Path.Combine(pending, "founders.db"), FounderExpectedTables, cancellationToken).ConfigureAwait(false);
            var manifest = new BackupSetManifest(
                ManifestSchemaVersion,
                setId,
                startedAtUtc,
                options.Build.ProductVersion,
                options.Build.GitCommit,
                options.Build.BuildUtc,
                options.Build.RuntimeTarget,
                [central, founder],
                [
                    "DPAPI secret files",
                    "browser profiles and authentication state",
                    "SQLite WAL/SHM sidecars",
                    "logs, raw captures, reports, and run artifacts",
                ]);
            string manifestTemp = Path.Combine(pending, ManifestFileName + ".tmp");
            await File.WriteAllBytesAsync(
                manifestTemp,
                JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(manifestTemp, Path.Combine(pending, ManifestFileName));
            Directory.Move(pending, completed);

            BackupValidationResult validation = await ValidateAsync(setId, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("The promoted backup set failed readback validation.");
            }

            int removed = await ApplyRetentionAsync(setId, protectedSetId, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            long bytes = central.SizeBytes + founder.SizeBytes;
            return new(
                new(setId, startedAtUtc, options.Build.ProductVersion, bytes, true, null),
                removed,
                stopwatch.Elapsed,
                []);
        }
        catch
        {
            TryDeleteDirectory(pending);
            throw;
        }
    }

    private static async Task<BackupDatabaseEntry> BackupDatabaseAsync(
        string name,
        string sourcePath,
        string destinationPath,
        IReadOnlyCollection<string> expectedTables,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"The {name} database does not exist.");
        }

        string sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        string destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var source = new SqliteConnection(sourceConnectionString);
                await using var destination = new SqliteConnection(destinationConnectionString);
                await source.OpenAsync(cancellationToken).ConfigureAwait(false);
                await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(destination);
                break;
            }
            catch (SqliteException exception) when (
                (exception.SqliteErrorCode & 0xff) is 5 or 6
                && attempt < MaximumBusyBackupAttempts)
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        DatabaseInspection inspection = await InspectDatabaseAsync(destinationPath, expectedTables, cancellationToken).ConfigureAwait(false);
        var info = new FileInfo(destinationPath);
        return new(
            name,
            Path.GetFileName(destinationPath),
            info.Length,
            await ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false),
            inspection.LatestMigration,
            inspection.TableCount);
    }

    private static async Task<DatabaseInspection> InspectDatabaseAsync(
        string databasePath,
        IReadOnlyCollection<string> expectedTables,
        CancellationToken cancellationToken)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        string integrity = Convert.ToString(
            await ExecuteScalarAsync(connection, "PRAGMA quick_check;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) ?? string.Empty;
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)
            || expectedTables.Any(table => !tables.Contains(table)))
        {
            throw new InvalidDataException("A SQLite database failed integrity or expected-table validation.");
        }

        string migration = Convert.ToString(await ExecuteScalarAsync(
            connection,
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;",
            cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(migration))
        {
            throw new InvalidDataException("A SQLite database has no applied migration history.");
        }

        return new(true, migration, tables.Count);
    }

    private async Task<int> ApplyRetentionAsync(
        string currentSetId,
        string? protectedSetId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BackupSetInfo> sets = await ListAsync(1000, cancellationToken).ConfigureAwait(false);
        var keep = new HashSet<string>(StringComparer.Ordinal) { currentSetId };
        if (protectedSetId is not null) keep.Add(protectedSetId);
        foreach (BackupSetInfo set in sets.Where(item => item.IsValid)
                     .GroupBy(item => item.CreatedAtUtc.UtcDateTime.Date)
                     .OrderByDescending(group => group.Key)
                     .Take(options.Retention.DailySets)
                     .Select(group => group.OrderByDescending(item => item.CreatedAtUtc).First()))
        {
            keep.Add(set.BackupSetId);
        }

        foreach (BackupSetInfo set in sets.Where(item => item.IsValid)
                     .GroupBy(item => (ISOWeek.GetYear(item.CreatedAtUtc.UtcDateTime), ISOWeek.GetWeekOfYear(item.CreatedAtUtc.UtcDateTime)))
                     .OrderByDescending(group => group.Key.Item1)
                     .ThenByDescending(group => group.Key.Item2)
                     .Take(options.Retention.WeeklySets)
                     .Select(group => group.OrderByDescending(item => item.CreatedAtUtc).First()))
        {
            keep.Add(set.BackupSetId);
        }

        var removed = 0;
        foreach (BackupSetInfo set in sets.Where(item => item.IsValid && !keep.Contains(item.BackupSetId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = GetBackupSetDirectory(set.BackupSetId);
            Directory.Delete(path, recursive: true);
            removed++;
        }

        return removed;
    }

    private async Task<bool> HasActiveRunsAsync(CancellationToken cancellationToken)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Set<AgentRunEntity>().AsNoTracking().AnyAsync(
            item => item.Status == nameof(AgentRunStatus.Starting) || item.Status == nameof(AgentRunStatus.Running),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteAuditAsync(
        string actorId,
        string action,
        string? targetId,
        AuditOutcome outcome,
        object data,
        CancellationToken cancellationToken)
    {
        _ = await audit.WriteAsync(new(
            AuditActorType.User,
            actorId,
            action,
            "DatabaseBackupSet",
            string.IsNullOrWhiteSpace(targetId) ? "backup-create-attempt" : targetId,
            outcome,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(data, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private string PrepareBackupRoot()
    {
        string dataRoot = Path.GetFullPath(options.DataDirectory);
        Directory.CreateDirectory(dataRoot);
        string root = CombineContained(dataRoot, "backups");
        Directory.CreateDirectory(root);
        RejectReparsePoint(dataRoot);
        RejectReparsePoint(root);
        return root;
    }

    private string GetBackupSetDirectory(string backupSetId) => CombineContained(PrepareBackupRoot(), backupSetId);

    private static DatabaseBackupOptions ValidateOptions(DatabaseBackupOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string root = Path.GetFullPath(value.DataDirectory);
        if (string.IsNullOrWhiteSpace(value.CentralDatabaseFileName)
            || Path.GetFileName(value.CentralDatabaseFileName) != value.CentralDatabaseFileName
            || string.IsNullOrWhiteSpace(value.FounderDatabaseRelativePath)
            || Path.IsPathFullyQualified(value.FounderDatabaseRelativePath))
        {
            throw new ArgumentException("Database backup paths are invalid.", nameof(value));
        }

        value.Build.Validate();
        value.Retention.Validate();
        _ = CombineContained(root, value.FounderDatabaseRelativePath);
        return value with { DataDirectory = root };
    }

    private static void ValidateBackupSetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 96
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException("The backup set identifier is invalid.", nameof(value));
        }
    }

    private static void ValidateActor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded backup actor is required.", nameof(value));
        }
    }

    private static string CombineContained(string root, params string[] segments)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string result = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        if (!result.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A backup path escapes the configured data root.");
        }

        return result;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Backup storage cannot be a reparse point.");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int CompareProductVersions(string left, string right)
    {
        static Version Parse(string value)
        {
            string core = value.Split(['-', '+'], 2)[0];
            return Version.TryParse(core, out Version? parsed) ? parsed : new Version(0, 0);
        }

        return Parse(left).CompareTo(Parse(right));
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            string path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static bool IsAnyRuntimeProcessActive()
    {
        foreach (string processName in new[] { "HomeBusinessAssistant.Host", "FounderScout", "WakeRemote" })
        {
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length > 0) return true;
            }
            finally
            {
                foreach (System.Diagnostics.Process process in processes) process.Dispose();
            }
        }

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Root-confined pending/staging content remains distinguishable and repair-cleanable.
        }
    }

    private sealed record DatabaseInspection(bool IsValid, string LatestMigration, int TableCount);
}
