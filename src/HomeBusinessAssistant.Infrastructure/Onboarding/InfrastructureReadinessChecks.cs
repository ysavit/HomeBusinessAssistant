using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Onboarding;

/// <summary>Verifies central SQLite connectivity, migration state, and required per-connection safety pragmas.</summary>
public sealed class CentralDatabaseOnboardingCheck(
    AssistantDatabase database,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "persistence.central-database",
        OnboardingCheckScope.Persistence,
        "Central database",
        "Checks migration state and the WAL, foreign-key, busy-timeout, and synchronous settings required for durable local coordination.",
        Required: true,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(15),
        RemediationKey: "database-help");

    /// <inheritdoc />
    public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            await using AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            bool canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!canConnect)
            {
                return Result(OnboardingCheckStatus.Blocked, "database.unavailable", "The central database is not available.", observedAtUtc, false, false, false, 0, false);
            }

            bool pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string journal = await ScalarAsync(context, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
                string foreignKeys = await ScalarAsync(context, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false);
                string busyTimeout = await ScalarAsync(context, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false);
                string synchronous = await ScalarAsync(context, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false);
                bool wal = string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase);
                bool foreignKeysEnabled = foreignKeys == "1";
                _ = int.TryParse(busyTimeout, NumberStyles.None, CultureInfo.InvariantCulture, out int busyTimeoutMilliseconds);
                bool normalSync = synchronous == "1";
                bool ready = !pending && wal && foreignKeysEnabled && busyTimeoutMilliseconds > 0 && normalSync;
                return Result(
                    ready ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked,
                    ready ? "database.ready" : pending ? "database.migrations-pending" : "database.safety-settings-invalid",
                    ready
                        ? "The central database is migrated and its required SQLite safety settings are active."
                        : pending
                            ? "The central database has pending migrations. Run repair or restart the Host before setup."
                            : "One or more required SQLite safety settings are unavailable.",
                    observedAtUtc,
                    canConnect,
                    pending,
                    wal,
                    busyTimeoutMilliseconds,
                    foreignKeysEnabled && normalSync);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result(OnboardingCheckStatus.Blocked, "database.check-failed", "The central database safety check failed. Review local database diagnostics.", observedAtUtc, false, false, false, 0, false);
        }
    }

    private OnboardingReadinessCheckResult Result(
        OnboardingCheckStatus status,
        string reasonCode,
        string message,
        DateTimeOffset observedAtUtc,
        bool canConnect,
        bool pendingMigrations,
        bool wal,
        int busyTimeoutMilliseconds,
        bool connectionSafetyEnabled) => new(
            Definition,
            status,
            reasonCode,
            message,
            observedAtUtc,
            observedAtUtc.AddHours(1),
            "1.0",
            JsonSerializer.SerializeToElement(new
            {
                canConnect,
                pendingMigrations,
                wal,
                busyTimeoutMilliseconds,
                connectionSafetyEnabled,
            }),
            Definition.RemediationKey);

    private static async Task<string> ScalarAsync(
        AssistantDbContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

/// <summary>Checks the central Runner and at least one safely contained installed agent package.</summary>
public sealed class RunnerAndPackagesOnboardingCheck(
    IAgentDefinitionRepository agents,
    string applicationRoot,
    string runnerExecutablePath,
    string agentDirectory,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "packages.runner-and-agents",
        OnboardingCheckScope.Packages,
        "Runner and installed agents",
        "Confirms the central Runner exists and at least one registered agent resolves to a contained non-reparse executable package.",
        Required: true,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(10),
        RemediationKey: "agent-package-repair");

    /// <inheritdoc />
    public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        string appRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationRoot));
        string runner = Path.GetFullPath(runnerExecutablePath);
        string packagesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(agentDirectory));
        bool runnerContained = IsContained(appRoot, runner);
        bool runnerPresent = runnerContained && IsSafeFile(runner);
        int expectedRunnerMajor = typeof(RunnerAndPackagesOnboardingCheck).Assembly.GetName().Version?.Major ?? 0;
        bool runnerVersionKnown = TryGetFileMajorVersion(runner, out int runnerMajor);
        bool runnerCompatible = runnerPresent && runnerVersionKnown && runnerMajor == expectedRunnerMajor;
        IReadOnlyList<AgentDefinitionRecord> definitions = await agents.GetAllAsync(cancellationToken).ConfigureAwait(false);
        int valid = 0;
        foreach (AgentDefinitionRecord definition in definitions)
        {
            string packageRoot;
            string executable;
            try
            {
                packageRoot = Path.GetFullPath(Path.Combine(packagesRoot, definition.WorkingDirectoryRelativePath));
                executable = Path.GetFullPath(Path.Combine(packageRoot, definition.ExecutableRelativePath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (IsContained(packagesRoot, packageRoot)
                && IsContained(packageRoot, executable)
                && IsSafeDirectory(packageRoot)
                && IsSafeFile(executable))
            {
                valid++;
            }
        }

        int invalid = definitions.Count - valid;
        OnboardingCheckStatus status = !runnerPresent || !runnerCompatible || valid == 0
            ? OnboardingCheckStatus.Blocked
            : invalid > 0 ? OnboardingCheckStatus.Warning : OnboardingCheckStatus.Passed;
        string reasonCode = !runnerPresent ? "packages.runner-missing"
            : !runnerVersionKnown ? "packages.runner-version-unavailable"
            : !runnerCompatible ? "packages.runner-version-incompatible"
            : valid == 0 ? "onboarding.no-agents"
            : invalid > 0 ? "packages.some-invalid" : "packages.ready";
        string message = !runnerPresent
            ? "The central Runner executable is missing or outside the validated application root. Run repair before setup."
            : !runnerVersionKnown
                ? "The central Runner version could not be verified. Run repair before setup."
                : !runnerCompatible
                    ? "The central Runner is not compatible with this Host major version. Run repair before setup."
            : valid == 0
                ? "No valid installed agent package is available. Run the package scan or repair workflow before selecting agents."
                : invalid > 0
                    ? $"{valid} agent package(s) are ready and {invalid} registered definition(s) need repair."
                    : $"The central Runner and {valid} installed agent package(s) are ready.";
        return new(
            Definition,
            status,
            reasonCode,
            message,
            observedAtUtc,
            observedAtUtc.AddHours(1),
            "1.0",
            JsonSerializer.SerializeToElement(new
            {
                runnerPresent,
                runnerVersionKnown,
                runnerCompatible,
                runnerMajor = runnerVersionKnown ? runnerMajor : (int?)null,
                expectedRunnerMajor,
                definitionCount = definitions.Count,
                validAgentCount = valid,
                invalidAgentCount = invalid,
            }),
            Definition.RemediationKey);
    }

    private static bool TryGetFileMajorVersion(string path, out int major)
    {
        major = 0;
        if (!IsSafeFile(path))
        {
            return false;
        }

        try
        {
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            if (string.IsNullOrWhiteSpace(version.FileVersion))
            {
                return false;
            }

            major = version.FileMajorPart;
            return major >= 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsContained(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSafeFile(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Checks root containment, data writability, available disk, and whether any verified backup exists.</summary>
public sealed class StorageOnboardingCheck(
    string applicationRoot,
    string dataDirectory,
    string databasePath,
    string runnerExecutablePath,
    TimeProvider timeProvider,
    long warningFreeBytes = 2L * 1024 * 1024 * 1024,
    long blockingFreeBytes = 256L * 1024 * 1024) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "storage.local-roots",
        OnboardingCheckScope.Storage,
        "Local storage and backups",
        "Checks contained application/database roots, a temporary data-root write, free disk space, and the presence of a completed backup set.",
        Required: true,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(10),
        RemediationKey: "storage-help");

    /// <inheritdoc />
    public ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        string appRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationRoot));
        string dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        string database = Path.GetFullPath(databasePath);
        string runner = Path.GetFullPath(runnerExecutablePath);
        bool rootsContained = IsContained(dataRoot, database) && IsContained(appRoot, runner);
        bool rootsSafe = IsSafeDirectory(dataRoot) && IsSafeDirectory(appRoot);
        bool writable = ProbeWrite(dataRoot);
        long availableBytes = GetAvailableBytes(dataRoot);
        string backups = Path.Combine(dataRoot, "backups");
        bool hasBackup = Directory.Exists(backups)
            && Directory.EnumerateDirectories(backups, "*", SearchOption.TopDirectoryOnly)
                .Any(path => !Path.GetFileName(path).StartsWith(".pending-", StringComparison.Ordinal)
                    && File.Exists(Path.Combine(path, "backup-manifest.json")));
        OnboardingCheckStatus status = !rootsContained || !rootsSafe || !writable || (availableBytes >= 0 && availableBytes < blockingFreeBytes)
            ? OnboardingCheckStatus.Blocked
            : !hasBackup || availableBytes < 0 || availableBytes < warningFreeBytes
                ? OnboardingCheckStatus.Warning
                : OnboardingCheckStatus.Passed;
        string reasonCode = !rootsContained ? "storage.root-containment-invalid"
            : !rootsSafe ? "storage.reparse-root"
            : !writable ? "storage.not-writable"
            : availableBytes >= 0 && availableBytes < blockingFreeBytes ? "storage.critically-low"
            : availableBytes < 0 ? "storage.capacity-unknown"
            : availableBytes < warningFreeBytes ? "storage.low"
            : !hasBackup ? "storage.backup-missing" : "storage.ready";
        string message = status switch
        {
            OnboardingCheckStatus.Blocked => "The configured local storage roots are unsafe, unwritable, or critically low on free space.",
            OnboardingCheckStatus.Warning when !hasBackup => "Local storage is usable, but no completed database backup set was found yet.",
            OnboardingCheckStatus.Warning => "Local storage is usable, but free-space capacity needs attention or could not be determined.",
            _ => "Local roots are contained and writable, disk capacity is healthy, and a completed backup set exists.",
        };
        return ValueTask.FromResult(new OnboardingReadinessCheckResult(
            Definition,
            status,
            reasonCode,
            message,
            observedAtUtc,
            observedAtUtc.AddHours(1),
            "1.0",
            JsonSerializer.SerializeToElement(new
            {
                rootsContained,
                rootsSafe,
                writable,
                availableBytes,
                hasCompletedBackup = hasBackup,
                warningFreeBytes,
                blockingFreeBytes,
            }),
            Definition.RemediationKey));
    }

    private static bool ProbeWrite(string root)
    {
        string? path = null;
        bool writeCompleted = false;
        bool cleanupCompleted = true;
        try
        {
            Directory.CreateDirectory(root);
            path = Path.Combine(root, $".onboarding-write-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(0x48);
            stream.Flush(flushToDisk: true);
            writeCompleted = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writeCompleted = false;
        }
        finally
        {
            try
            {
                if (path is not null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                cleanupCompleted = false;
            }
        }

        return writeCompleted && cleanupCompleted;
    }

    private static long GetAvailableBytes(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path) ?? path;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return -1;
        }
    }

    private static bool IsContained(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
