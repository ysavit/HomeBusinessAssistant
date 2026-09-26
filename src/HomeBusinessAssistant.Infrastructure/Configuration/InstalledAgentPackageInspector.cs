using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Configuration;

/// <summary>Bounded root-confined package inspection used by onboarding projections.</summary>
public sealed class InstalledAgentPackageInspector : IInstalledAgentPackageInspector
{
    private readonly string agentDirectory;
    private readonly IAgentConfigurationSchemaCatalog schemas;

    /// <summary>Creates an inspector for the explicitly configured installed-agent root.</summary>
    public InstalledAgentPackageInspector(
        string agentDirectory,
        IAgentConfigurationSchemaCatalog schemas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        this.agentDirectory = Path.GetFullPath(agentDirectory);
        this.schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
    }

    /// <inheritdoc />
    public async ValueTask<InstalledAgentPackageInspection> InspectAsync(
        AgentDefinitionRecord definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string packageRoot;
        try
        {
            packageRoot = StoragePathPolicy.CombineContained(agentDirectory, definition.WorkingDirectoryRelativePath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Unavailable(definition, "agent.package-path-invalid", "The registered package location is invalid.");
        }

        if (!Directory.Exists(packageRoot))
        {
            return Unavailable(definition, "agent.package-removed", "The installed package directory is missing.", removed: true);
        }

        try
        {
            StoragePathPolicy.RejectExistingReparsePoints(agentDirectory, packageRoot);
            string manifestPath = StoragePathPolicy.CombineContained(packageRoot, "manifest.json");
            if (!File.Exists(manifestPath) || (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            {
                return Unavailable(definition, "agent.package-manifest-missing", "The package manifest is missing or unsafe.");
            }

            AgentManifestLoadResult loaded = await AgentManifestLoader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            AgentManifest manifest = loaded.Manifest
                ?? throw new InvalidDataException("The package manifest is invalid.");
            if (manifest.Id != definition.Id
                || !string.Equals(Path.GetFileName(packageRoot), manifest.Id.Value, StringComparison.Ordinal))
            {
                return Unavailable(definition, "agent.package-identity-mismatch", "The package identity does not match the installed registry.");
            }

            if (!string.Equals(manifest.Version.Value, definition.InstalledVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.ManifestVersion.ToString(), definition.ManifestVersion, StringComparison.Ordinal))
            {
                return Unavailable(definition, "agent.package-registry-stale", "The package changed after the last registry scan. Scan installed agents again.");
            }

            string executable = StoragePathPolicy.CombineContained(
                packageRoot,
                manifest.Executable.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
            StoragePathPolicy.RejectExistingReparsePoints(packageRoot, Path.GetDirectoryName(executable)!);
            if (!File.Exists(executable)
                || (File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0
                || new FileInfo(executable).Length is < 1 or > 1_073_741_824)
            {
                return Unavailable(definition, "agent.package-executable-invalid", "The package executable is missing or unsafe.");
            }

            GenericConfigurationSchema? schema = await schemas.GetAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            if (schema is null)
            {
                return Unavailable(definition, "agent.package-schema-missing", "The package configuration schema is missing.");
            }

            return new(
                definition.Id,
                IsAvailable: true,
                IsRemoved: false,
                "agent.package-available",
                "The currently installed package passed bounded registry validation.",
                manifest.Version.Value,
                manifest.ManifestVersion.ToString(),
                manifest.ConfigurationSchemaVersion.ToString(),
                schema);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Unavailable(
                definition,
                exception is UnauthorizedAccessException ? "agent.package-access-denied" : "agent.package-invalid",
                exception is UnauthorizedAccessException
                    ? "The package could not be inspected with the current user's permissions."
                    : "The installed package failed bounded validation.");
        }
    }

    private static InstalledAgentPackageInspection Unavailable(
        AgentDefinitionRecord definition,
        string reasonCode,
        string message,
        bool removed = false) => new(
            definition.Id,
            IsAvailable: false,
            IsRemoved: removed,
            reasonCode,
            message,
            definition.InstalledVersion,
            definition.ManifestVersion,
            ConfigurationSchemaVersion: null,
            ConfigurationSchema: null);
}
