using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Infrastructure.Operations;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace HomeBusinessAssistant.Infrastructure.Tests;

/// <summary>Exercises consistent two-database backup and guarded restore against real SQLite files.</summary>
[TestFixture]
public sealed class DatabaseBackupServiceTests
{
    private static readonly string[] ExpectedDatabaseNames = ["assistant", "founder-scout"];

    /// <summary>Verifies set promotion, integrity metadata, and sensitive-directory exclusion.</summary>
    [Test]
    public async Task CreateValidatesBothDatabasesAndExcludesSensitiveDirectories()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        await CreateFounderDatabaseAsync(temporary.Root);
        Directory.CreateDirectory(Path.Combine(temporary.Root, "secrets"));
        await File.WriteAllTextAsync(Path.Combine(temporary.Root, "secrets", "not-backed-up.secret"), "ciphertext");
        Directory.CreateDirectory(Path.Combine(temporary.Root, "agents", "founder-scout", "browser", "account"));
        await File.WriteAllTextAsync(Path.Combine(temporary.Root, "agents", "founder-scout", "browser", "account", "session"), "sensitive");
        DatabaseBackupService service = CreateService(temporary, new());

        BackupCreateResult created = await service.CreateAsync("test");
        BackupValidationResult validation = await service.ValidateAsync(created.BackupSet.BackupSetId);
        IReadOnlyList<BackupSetInfo> listed = await service.ListAsync(10);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Manifest!.Databases.Select(item => item.Name), Is.EquivalentTo(ExpectedDatabaseNames));
            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(Directory.EnumerateFiles(Path.Combine(temporary.Root, "backups"), "*", SearchOption.AllDirectories)
                .Any(path => path.Contains("secret", StringComparison.OrdinalIgnoreCase) || path.Contains("browser", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    /// <summary>Verifies online backup under writes and incomplete-set invisibility.</summary>
    [Test]
    public async Task BackupRemainsConsistentDuringActiveSourceWritesAndIgnoresInterruptedSet()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        await CreateFounderDatabaseAsync(temporary.Root);
        DatabaseBackupService service = CreateService(temporary, new());
        string pending = Path.Combine(temporary.Root, "backups", ".pending-interrupted");
        Directory.CreateDirectory(pending);
        await File.WriteAllTextAsync(Path.Combine(pending, "assistant.db"), "partial");

        using var cancellation = new CancellationTokenSource();
        Task writer = Task.Run(async () =>
        {
            for (var index = 0; index < 25 && !cancellation.IsCancellationRequested; index++)
            {
                await using var connection = new SqliteConnection($"Data Source={temporary.Database.DatabasePath};Pooling=False");
                await connection.OpenAsync(cancellation.Token);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "INSERT INTO SystemSettings (Key, SchemaVersion, ValueJson, UpdatedAtUtc, ConcurrencyToken) VALUES ($key, '1.0', '{}', $time, 1) ON CONFLICT(Key) DO UPDATE SET ConcurrencyToken = ConcurrencyToken + 1;";
                command.Parameters.AddWithValue("$key", $"backup-write-{index}");
                command.Parameters.AddWithValue("$time", temporary.TimeProvider.GetUtcNow().ToString("O"));
                _ = await command.ExecuteNonQueryAsync(cancellation.Token);
            }
        }, cancellation.Token);

        BackupCreateResult created = await service.CreateAsync("test");
        await cancellation.CancelAsync();
        try { await writer; } catch (OperationCanceledException) { }

        BackupValidationResult validation = await service.ValidateAsync(created.BackupSet.BackupSetId);
        IReadOnlyList<BackupSetInfo> listed = await service.ListAsync(10);
        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(listed.Select(item => item.BackupSetId), Does.Not.Contain("interrupted"));
            Assert.That(listed, Has.Count.EqualTo(1));
        });
    }

    /// <summary>Verifies that a creation failure remains the original actionable error and is auditable.</summary>
    [Test]
    public async Task CreateFailureUsesABoundedAuditTargetWithoutMaskingTheCause()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        DatabaseBackupService service = CreateService(temporary, new());

        FileNotFoundException exception = Assert.ThrowsAsync<FileNotFoundException>(
            async () => await service.CreateAsync("test"))!;
        await using var connection = new SqliteConnection(
            $"Data Source={temporary.Database.DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Action, TargetId, Outcome FROM AuditEvents ORDER BY TimestampUtc DESC LIMIT 1";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain("founder-scout database"));
            Assert.That(reader.GetString(0), Is.EqualTo("backup.create"));
            Assert.That(reader.GetString(1), Is.EqualTo("backup-create-attempt"));
            Assert.That(reader.GetString(2), Is.EqualTo("Failed"));
        });
    }

    /// <summary>Verifies explicit confirmation and two-database restore behavior.</summary>
    [Test]
    public async Task RestoreRequiresConfirmationAndRollsBothDatabasesBackToTheSet()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        string founderPath = await CreateFounderDatabaseAsync(temporary.Root);
        DatabaseBackupService service = CreateService(temporary, new());
        await SetCentralMarkerAsync(temporary.Database.DatabasePath, "before");
        await SetFounderMarkerAsync(founderPath, "before");
        BackupCreateResult created = await service.CreateAsync("test");
        await SetCentralMarkerAsync(temporary.Database.DatabasePath, "after");
        await SetFounderMarkerAsync(founderPath, "after");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await service.RestoreAsync(new(
            created.BackupSet.BackupSetId, "wrong", true, "test")));
        BackupRestoreResult restored = await service.RestoreAsync(new(
            created.BackupSet.BackupSetId, "RESTORE", true, "test"));
        BackupCreateResult afterRestore = await service.CreateAsync("test-after-restore");

        Assert.Multiple(() =>
        {
            Assert.That(restored.Code, Is.EqualTo("restore.completed"));
            Assert.That(restored.PreRestoreBackupSetId, Is.Not.Empty);
            Assert.That(afterRestore.BackupSet.IsValid, Is.True);
        });
        Assert.That(await ReadCentralMarkerAsync(temporary.Database.DatabasePath), Is.EqualTo("before"));
        Assert.That(await ReadFounderMarkerAsync(founderPath), Is.EqualTo("before"));
    }

    /// <summary>Verifies bounded daily/weekly set retention.</summary>
    [Test]
    public async Task RetentionKeepsConfiguredDailyAndWeeklySets()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        await CreateFounderDatabaseAsync(temporary.Root);
        DatabaseBackupService service = CreateService(temporary, new(DailySets: 2, WeeklySets: 0));

        _ = await service.CreateAsync("test");
        temporary.TimeProvider.Advance(TimeSpan.FromDays(1));
        _ = await service.CreateAsync("test");
        temporary.TimeProvider.Advance(TimeSpan.FromDays(1));
        BackupCreateResult latest = await service.CreateAsync("test");

        IReadOnlyList<BackupSetInfo> listed = await service.ListAsync(10);
        Assert.Multiple(() =>
        {
            Assert.That(listed, Has.Count.EqualTo(2));
            Assert.That(latest.RemovedByRetention, Is.EqualTo(1));
        });
    }

    private static DatabaseBackupService CreateService(TemporaryAssistantDatabase temporary, BackupRetentionPolicy retention)
    {
        var leases = new LeaseManager(temporary.Database.ContextFactory);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var build = new ProductBuildInfo("1.0.0", "test", "2026-08-31T00:00:00Z", "win-x64", ".NETCoreApp,Version=v10.0");
        return new(
            temporary.Database.ContextFactory,
            leases,
            audit,
            temporary.TimeProvider,
            new(temporary.Root, "assistant.db", Path.Combine("agents", "founder-scout", "founders.db"), build, retention));
    }

    private static async Task<string> CreateFounderDatabaseAsync(string root)
    {
        string directory = Path.Combine(root, "agents", "founder-scout");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "founders.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);
            INSERT INTO __EFMigrationsHistory VALUES ('20260831163034_AddFounderScoutResultsWorkflow', '10.0.11');
            CREATE TABLE Candidates (Id TEXT NOT NULL PRIMARY KEY);
            CREATE TABLE ProfileSnapshots (Id TEXT NOT NULL PRIMARY KEY);
            CREATE TABLE FounderScoutLeases (LeaseName TEXT NOT NULL PRIMARY KEY);
            CREATE TABLE TestMarkers (Value TEXT NOT NULL);
            INSERT INTO TestMarkers VALUES ('initial');
            """;
        _ = await command.ExecuteNonQueryAsync();
        return path;
    }

    private static Task SetCentralMarkerAsync(string path, string value) => ExecuteAsync(
        path,
        "INSERT INTO SystemSettings (Key, SchemaVersion, ValueJson, UpdatedAtUtc, ConcurrencyToken) VALUES ('backup-marker', '1.0', $value, '2026-08-31T00:00:00Z', 1) ON CONFLICT(Key) DO UPDATE SET ValueJson = excluded.ValueJson;",
        value);

    private static Task SetFounderMarkerAsync(string path, string value) => ExecuteAsync(
        path,
        "UPDATE TestMarkers SET Value = $value;",
        value);

    private static Task<string?> ReadCentralMarkerAsync(string path) => ReadAsync(path, "SELECT ValueJson FROM SystemSettings WHERE Key = 'backup-marker';");

    private static Task<string?> ReadFounderMarkerAsync(string path) => ReadAsync(path, "SELECT Value FROM TestMarkers LIMIT 1;");

    private static async Task ExecuteAsync(string path, string sql, string value)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
