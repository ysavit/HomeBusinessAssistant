using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>A validated and migrated central persistence runtime.</summary>
public sealed class AssistantDatabase
{
    private static readonly string[] BuiltInManifestFiles =
    [
        "founder-scout.agent-manifest.json",
        "wake-remote.agent-manifest.json",
    ];

    private AssistantDatabase(string dataDirectory, string databasePath, AssistantDbContextFactory contextFactory)
    {
        DataDirectory = dataDirectory;
        DatabasePath = databasePath;
        ContextFactory = contextFactory;
    }

    /// <summary>Gets the absolute root used for the database and private storage.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the absolute central SQLite file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the per-operation context factory.</summary>
    public AssistantDbContextFactory ContextFactory { get; }

    /// <summary>Validates storage, applies migrations/pragmas, and idempotently seeds built-in agents.</summary>
    public static async ValueTask<AssistantDatabase> InitializeAsync(
        AssistantDatabaseSettings settings,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        timeProvider ??= TimeProvider.System;
        string dataDirectory = StoragePathPolicy.PrepareDataDirectory(settings.DataDirectory);
        string databasePath = StoragePathPolicy.GetSafeDatabasePath(dataDirectory, settings.DatabaseFileName);
        AssistantDbContextFactory factory = AssistantDbContextFactory.Create(databasePath, settings.EffectiveBusyTimeout);

        try
        {
            await using AssistantDbContext context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (pendingMigrations.Any())
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            await ConfigureAndVerifyPragmasAsync(context, settings.EffectiveBusyTimeout, cancellationToken).ConfigureAwait(false);
            string manifestDirectory = settings.ManifestDirectory is null
                ? Path.Combine(AppContext.BaseDirectory, "manifests")
                : Path.GetFullPath(settings.ManifestDirectory);
            IReadOnlyList<AgentManifest> manifests = await LoadBuiltInManifestsAsync(
                manifestDirectory,
                cancellationToken).ConfigureAwait(false);
            await SeedBuiltInAgentsAsync(
                context,
                manifests,
                timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The central platform database could not be initialized.", exception);
        }

        return new AssistantDatabase(dataDirectory, databasePath, factory);
    }

    private static async Task ConfigureAndVerifyPragmasAsync(
        AssistantDbContext context,
        TimeSpan busyTimeout,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string journalMode = await ExecuteScalarAsync(context, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            _ = await ExecuteScalarAsync(context, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
            _ = await ExecuteScalarAsync(context, $"PRAGMA busy_timeout={(int)Math.Ceiling(busyTimeout.TotalMilliseconds)};", cancellationToken).ConfigureAwait(false);
            _ = await ExecuteScalarAsync(context, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);

            string foreignKeys = await ExecuteScalarAsync(context, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false);
            string configuredBusyTimeout = await ExecuteScalarAsync(context, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);
            string synchronous = await ExecuteScalarAsync(context, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false);
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase)
                || foreignKeys != "1"
                || configuredBusyTimeout != ((int)Math.Ceiling(busyTimeout.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                || synchronous != "1")
            {
                throw new InvalidOperationException("The required SQLite safety pragmas could not be verified.");
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> ExecuteScalarAsync(
        AssistantDbContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<IReadOnlyList<AgentManifest>> LoadBuiltInManifestsAsync(
        string manifestDirectory,
        CancellationToken cancellationToken)
    {
        var manifests = new List<AgentManifest>(BuiltInManifestFiles.Length);
        foreach (string fileName in BuiltInManifestFiles)
        {
            AgentManifestLoadResult loadResult = await AgentManifestLoader.LoadAsync(
                Path.Combine(manifestDirectory, fileName),
                cancellationToken).ConfigureAwait(false);
            manifests.Add(loadResult.Manifest
                ?? throw new InvalidOperationException($"Built-in agent manifest '{fileName}' is missing or invalid."));
        }

        return manifests;
    }

    private static async Task SeedBuiltInAgentsAsync(
        AssistantDbContext context,
        IReadOnlyList<AgentManifest> manifests,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // Reserve the short write transaction before reading seed rows so SQLite never has to upgrade a stale read snapshot.
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection())
            .BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        foreach (AgentManifest manifest in manifests)
        {
            string agentId = manifest.Id.Value;
            AgentDefinitionEntity? entity = await context.Set<AgentDefinitionEntity>()
                .SingleOrDefaultAsync(item => item.Id == agentId, cancellationToken)
                .ConfigureAwait(false);
            bool created = entity is null;
            using JsonDocument capabilities = JsonDocument.Parse(JsonSerializer.Serialize(manifest.Capabilities));
            string capabilitiesJson = CanonicalJson.Serialize(capabilities.RootElement);
            using JsonDocument commands = JsonDocument.Parse(JsonSerializer.Serialize(manifest.SupportedCommands));
            string commandsJson = CanonicalJson.Serialize(commands.RootElement);
            if (created)
            {
                entity = new AgentDefinitionEntity
                {
                    Id = agentId,
                    Enabled = true,
                    CreatedAtUtc = nowUtc,
                    DisplayName = string.Empty,
                    Description = string.Empty,
                    ManifestVersion = string.Empty,
                    InstalledVersion = string.Empty,
                    ExecutableRelativePath = string.Empty,
                    WorkingDirectoryRelativePath = string.Empty,
                    CapabilitiesJson = "[]",
                    SupportedCommandsJson = "[]",
                    DefaultConcurrencyPolicy = ConcurrencyPolicy.Forbid.ToString(),
                };
                context.Add(entity);
            }

            string manifestVersion = manifest.ManifestVersion.ToString();
            string installedVersion = manifest.Version.Value;
            string concurrencyPolicy = manifest.DefaultConcurrencyPolicy.ToString();
            bool changed = created
                || entity!.DisplayName != manifest.DisplayName
                || entity.Description != manifest.Description
                || entity.ManifestVersion != manifestVersion
                || entity.InstalledVersion != installedVersion
                || entity.ExecutableRelativePath != manifest.Executable
                || entity.WorkingDirectoryRelativePath != agentId
                || entity.CapabilitiesJson != capabilitiesJson
                || entity.SupportedCommandsJson != commandsJson
                || entity.DefaultConcurrencyPolicy != concurrencyPolicy
                || entity.SupportsScheduling != manifest.SupportsScheduling
                || entity.SupportsManualRun != manifest.SupportsManualRun
                || entity.RequiresInteractiveUserSession != manifest.RequiresInteractiveUserSession;
            if (changed)
            {
                entity!.DisplayName = manifest.DisplayName;
                entity.Description = manifest.Description;
                entity.ManifestVersion = manifestVersion;
                entity.InstalledVersion = installedVersion;
                entity.ExecutableRelativePath = manifest.Executable;
                entity.WorkingDirectoryRelativePath = agentId;
                entity.CapabilitiesJson = capabilitiesJson;
                entity.SupportedCommandsJson = commandsJson;
                entity.DefaultConcurrencyPolicy = concurrencyPolicy;
                entity.SupportsScheduling = manifest.SupportsScheduling;
                entity.SupportsManualRun = manifest.SupportsManualRun;
                entity.RequiresInteractiveUserSession = manifest.RequiresInteractiveUserSession;
                entity.UpdatedAtUtc = nowUtc;
                context.Add(CreateAuditEvent(
                    nowUtc,
                    created ? "agent.installed" : "agent.manifest-updated",
                    "agent",
                    agentId,
                    JsonSerializer.SerializeToElement(new { manifestVersion = entity.ManifestVersion, installedVersion = entity.InstalledVersion })));
            }
        }

        const string defaultTimeZoneSetting = "scheduler.default-time-zone";
        bool hasDefaultTimeZone = await context.Set<SystemSettingEntity>()
            .AnyAsync(item => item.Key == defaultTimeZoneSetting, cancellationToken)
            .ConfigureAwait(false);
        if (!hasDefaultTimeZone)
        {
            context.Add(new SystemSettingEntity
            {
                Key = defaultTimeZoneSetting,
                ValueJson = CanonicalJson.Serialize(JsonSerializer.SerializeToElement(new
                {
                    timeZoneId = SchedulingDefaults.DefaultWindowsTimeZoneId,
                })),
                SchemaVersion = "1.0",
                UpdatedAtUtc = nowUtc,
                ConcurrencyToken = 1,
            });
        }

        bool initializationAlreadyAudited = await context.Set<AuditEventEntity>()
            .AnyAsync(item => item.Action == "database.initialized", cancellationToken)
            .ConfigureAwait(false);
        if (!initializationAlreadyAudited)
        {
            context.Add(CreateAuditEvent(
                nowUtc,
                "database.initialized",
                "database",
                "assistant",
                JsonSerializer.SerializeToElement(new { journalMode = "wal", foreignKeys = true, synchronous = "normal" })));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AuditEventEntity CreateAuditEvent(
        DateTimeOffset timestampUtc,
        string action,
        string targetType,
        string targetId,
        JsonElement data) => new()
        {
            Id = Guid.NewGuid(),
            TimestampUtc = timestampUtc,
            ActorType = AuditActorType.System.ToString(),
            ActorId = "platform",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = AuditOutcome.Succeeded.ToString(),
            CorrelationId = Guid.NewGuid(),
            DataJson = AuditRedactor.Redact(data),
        };
}
