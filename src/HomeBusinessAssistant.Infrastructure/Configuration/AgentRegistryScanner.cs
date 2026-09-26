using System.Security.Cryptography;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Configuration;

/// <summary>The disposition of one immediate child inspected by the installed-agent scanner.</summary>
public enum AgentPackageScanStatus
{
    /// <summary>A new valid definition was registered disabled.</summary>
    Installed,
    /// <summary>An existing valid definition was reconciled without changing user state.</summary>
    Updated,
    /// <summary>An invalid package was rejected.</summary>
    Invalid,
    /// <summary>A previously registered package directory is no longer present.</summary>
    Removed,
}

/// <summary>A safe result for one scanned or missing installed agent package.</summary>
public sealed record AgentPackageScanItem(
    string AgentId,
    AgentPackageScanStatus Status,
    string Code,
    string Message,
    string? Version = null,
    string? ExecutableSha256 = null);

/// <summary>The deterministic summary returned by an installed-agent scan.</summary>
public sealed record AgentRegistryScanResult(IReadOnlyList<AgentPackageScanItem> Items)
{
    /// <summary>Gets the number of valid installed or updated packages.</summary>
    public int ValidCount => Items.Count(item => item.Status is AgentPackageScanStatus.Installed or AgentPackageScanStatus.Updated);
    /// <summary>Gets the number of rejected packages.</summary>
    public int InvalidCount => Items.Count(item => item.Status == AgentPackageScanStatus.Invalid);
    /// <summary>Gets the number of missing packages disabled by reconciliation.</summary>
    public int RemovedCount => Items.Count(item => item.Status == AgentPackageScanStatus.Removed);
}

/// <summary>Safely reconciles explicitly packaged immediate child directories into persisted agent definitions.</summary>
public sealed class AgentRegistryScanner(
    string agentDirectory,
    IAgentDefinitionRepository definitions,
    IAgentConfigurationSchemaCatalog schemas,
    IAgentConfigurationService configurations,
    IAuditWriter audit,
    TimeProvider? timeProvider = null)
{
    private const int MaximumPackages = 128;
    private readonly string agentDirectory = ValidateRoot(agentDirectory);
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Scans the configured directory without executing package content.</summary>
    public async ValueTask<AgentRegistryScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        string[] directories = Directory.GetDirectories(this.agentDirectory, "*", SearchOption.TopDirectoryOnly);
        if (directories.Length > MaximumPackages)
        {
            throw new InvalidDataException($"The configured agent directory contains more than {MaximumPackages} immediate child directories.");
        }

        IReadOnlyList<AgentDefinitionRecord> existing = await definitions.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var existingById = existing.ToDictionary(item => item.Id, item => item);
        var observed = new HashSet<AgentId>();
        var items = new List<AgentPackageScanItem>();

        foreach (string directory in directories.Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folderName = Path.GetFileName(directory);
            AgentId? parsedFolderId = AgentId.TryParse(folderName, out AgentId folderId) ? folderId : null;
            try
            {
                AgentPackageValidation validation = await ValidatePackageAsync(directory, folderName, cancellationToken).ConfigureAwait(false);
                if (!observed.Add(validation.Manifest.Id))
                {
                    throw new InvalidDataException("The agent identifier is duplicated in the configured package directory.");
                }

                bool existed = existingById.TryGetValue(validation.Manifest.Id, out AgentDefinitionRecord? current);
                DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
                var definition = new AgentDefinitionRecord(
                    validation.Manifest.Id,
                    validation.Manifest.DisplayName,
                    validation.Manifest.Description,
                    validation.Manifest.ManifestVersion.ToString(),
                    validation.Manifest.Version.Value,
                    validation.Manifest.Executable,
                    folderName,
                    CanonicalJson.Serialize(JsonSerializer.SerializeToElement(validation.Manifest.Capabilities)),
                    CanonicalJson.Serialize(JsonSerializer.SerializeToElement(validation.Manifest.SupportedCommands)),
                    validation.Manifest.DefaultConcurrencyPolicy,
                    validation.Manifest.SupportsScheduling,
                    validation.Manifest.SupportsManualRun,
                    validation.Manifest.RequiresInteractiveUserSession,
                    Enabled: existed && current!.Enabled,
                    CreatedAtUtc: current?.CreatedAtUtc ?? now,
                    UpdatedAtUtc: now);
                _ = await definitions.UpsertManifestAsync(definition, cancellationToken).ConfigureAwait(false);
                if (validation.Schema.SupportsGenericEditor
                    && await configurations.GetCurrentAsync(validation.Manifest.Id, cancellationToken).ConfigureAwait(false) is null)
                {
                    _ = await configurations.SaveAsync(new(
                        validation.Manifest.Id,
                        validation.Manifest.ConfigurationSchemaVersion.ToString(),
                        validation.Schema.CreateDefaultDocument(),
                        "agent-registry-scanner",
                        "Seeded defaults from the installed configuration schema.",
                        Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
                }
                items.Add(new(
                    validation.Manifest.Id.Value,
                    existed ? AgentPackageScanStatus.Updated : AgentPackageScanStatus.Installed,
                    existed ? "agent.packageUpdated" : "agent.packageInstalled",
                    existed ? "The installed agent package is valid and user state was preserved." : "The installed agent package was registered disabled.",
                    validation.Manifest.Version.Value,
                    validation.ExecutableSha256));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                AgentDefinitionRecord? current = parsedFolderId is AgentId knownId && existingById.TryGetValue(knownId, out AgentDefinitionRecord? value)
                    ? value : null;
                if (current is not null)
                {
                    observed.Add(current.Id);
                    _ = await definitions.SetEnabledBySystemAsync(current.Id, false, cancellationToken).ConfigureAwait(false);
                }

                string safeId = parsedFolderId?.Value ?? "invalid-package";
                items.Add(new(safeId, AgentPackageScanStatus.Invalid, "agent.packageInvalid", SafeFailure(exception)));
                await WriteAuditAsync(
                    "agent.package-invalid",
                    safeId,
                    AuditOutcome.Failed,
                    new { code = "agent.packageInvalid", packageDirectory = folderName },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (AgentDefinitionRecord definition in existing.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            if (observed.Contains(definition.Id)) continue;
            string expectedRoot = StoragePathPolicy.CombineContained(this.agentDirectory, definition.WorkingDirectoryRelativePath);
            if (Directory.Exists(expectedRoot)) continue;
            _ = await definitions.SetEnabledBySystemAsync(definition.Id, false, cancellationToken).ConfigureAwait(false);
            items.Add(new(
                definition.Id.Value,
                AgentPackageScanStatus.Removed,
                "agent.packageRemoved",
                "The registered package directory is missing; the definition was disabled and history was retained.",
                definition.InstalledVersion));
            await WriteAuditAsync(
                "agent.package-removed",
                definition.Id.Value,
                AuditOutcome.Succeeded,
                new { code = "agent.packageRemoved", historyRetained = true },
                cancellationToken).ConfigureAwait(false);
        }

        return new(items.OrderBy(item => item.AgentId, StringComparer.Ordinal).ThenBy(item => item.Status).ToArray());
    }

    private async ValueTask<AgentPackageValidation> ValidatePackageAsync(
        string packageRoot,
        string folderName,
        CancellationToken cancellationToken)
    {
        StoragePathPolicy.RejectExistingReparsePoints(agentDirectory, packageRoot);
        string manifestPath = StoragePathPolicy.CombineContained(packageRoot, "manifest.json");
        string schemaPath = StoragePathPolicy.CombineContained(packageRoot, "configuration.schema.json");
        RejectRequiredFile(manifestPath, "The package manifest is missing or unsafe.");
        RejectRequiredFile(schemaPath, "The package configuration schema is missing or unsafe.");

        AgentManifestLoadResult loaded = await AgentManifestLoader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        AgentManifest manifest = loaded.Manifest ?? throw new InvalidDataException("The package manifest is invalid.");
        if (!string.Equals(manifest.Id.Value, folderName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The package directory name must exactly match the manifest agent identifier.");
        }

        GenericConfigurationSchema schema = await schemas.GetAsync(manifest.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The package configuration schema is missing.");
        string executablePath = StoragePathPolicy.CombineContained(packageRoot, NormalizeSegments(manifest.Executable));
        StoragePathPolicy.RejectExistingReparsePoints(packageRoot, Path.GetDirectoryName(executablePath)!);
        RejectRequiredFile(executablePath, "The package executable is missing or unsafe.");
        long length = new FileInfo(executablePath).Length;
        if (length is < 1 or > 1_073_741_824)
        {
            throw new InvalidDataException("The package executable size is outside supported bounds.");
        }

        string hash = await ComputeSha256Async(executablePath, cancellationToken).ConfigureAwait(false);
        return new(manifest, schema, hash);
    }

    private async ValueTask WriteAuditAsync(
        string action,
        string targetId,
        AuditOutcome outcome,
        object data,
        CancellationToken cancellationToken)
    {
        _ = await audit.WriteAsync(new(
            AuditActorType.System,
            "agent-registry-scanner",
            action,
            "agent",
            targetId,
            outcome,
            Guid.NewGuid(),
            null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);
    }

    private static string ValidateRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("The configured agent directory does not exist.");
        StoragePathPolicy.RejectExistingReparsePoints(fullPath, fullPath);
        return fullPath;
    }

    private static string SafeFailure(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "The package could not be inspected with the current user's permissions.",
        IOException => "The package files could not be read safely.",
        _ => new string(exception.Message.Where(character => !char.IsControl(character)).Take(256).ToArray()),
    };

    private static void RejectRequiredFile(string path, string message)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(message);
        }
    }

    private static string[] NormalizeSegments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static async ValueTask<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private sealed record AgentPackageValidation(
        AgentManifest Manifest,
        GenericConfigurationSchema Schema,
        string ExecutableSha256);
}
