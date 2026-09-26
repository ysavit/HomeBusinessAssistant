using System.Text;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Artifacts;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using HomeBusinessAssistant.Windows.Secrets;

namespace HomeBusinessAssistant.Runner;

internal static class PersistenceDemoCommand
{
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string? dataDirectory = ParseDataDirectory(arguments);
        bool ownsDirectory = dataDirectory is null;
        dataDirectory ??= Path.Combine(Path.GetTempPath(), $"hba-persistence-demo-{Guid.NewGuid():N}");

        try
        {
            AssistantDatabase database = await AssistantDatabase.InitializeAsync(
                new AssistantDatabaseSettings(dataDirectory),
                TimeProvider.System,
                cancellationToken).ConfigureAwait(false);
            var auditWriter = new AuditWriter(database.ContextFactory, TimeProvider.System);
            var configurationService = new AgentConfigurationService(
                database.ContextFactory,
                new BasicAgentConfigurationValidator(),
                TimeProvider.System);
            var occurrenceRepository = new OccurrenceRepository(database.ContextFactory, TimeProvider.System);
            var runRepository = new AgentRunRepository(database.ContextFactory);
            var leaseManager = new LeaseManager(database.ContextFactory);
            var artifactStore = new FileSystemArtifactStore(database.ContextFactory, database.DataDirectory, TimeProvider.System);

            AgentId agentId = AgentId.Parse("founder-scout");
            SaveConfigurationResult configuration = await configurationService.SaveAsync(
                new SaveConfigurationRequest(
                    agentId,
                    "1.0",
                    JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", demo = true }),
                    "development-cli",
                    "Created non-sensitive persistence demonstration configuration.",
                    Guid.NewGuid()),
                cancellationToken).ConfigureAwait(false);

            var secretStore = new WindowsCurrentUserSecretStore(database.DataDirectory, auditWriter);
            SecretReference secretReference = SecretReference.Parse("secret://development/persistence-demo");
            const string temporarySecret = "temporary-persistence-demo-value";
            await secretStore.SetAsync(secretReference, temporarySecret, cancellationToken).ConfigureAwait(false);
            string? roundTrip = await secretStore.GetAsync(secretReference, cancellationToken).ConfigureAwait(false);
            bool secretDeleted = await secretStore.DeleteAsync(secretReference, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(roundTrip, temporarySecret, StringComparison.Ordinal) || !secretDeleted)
            {
                throw new InvalidOperationException("The secret-store demonstration did not complete.");
            }

            DateTimeOffset nowUtc = TimeProvider.System.GetUtcNow();
            AgentLeaseRecord lease = await leaseManager.TryAcquireAsync(
                "persistence-demo",
                "development-cli",
                nowUtc,
                TimeSpan.FromMinutes(1),
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The demonstration lease could not be acquired.");
            bool leaseReleased = await leaseManager.ReleaseAsync(lease, cancellationToken).ConfigureAwait(false);

            OccurrenceId occurrenceId = OccurrenceId.New();
            _ = await occurrenceRepository.CreateIfAbsentAsync(
                new CreateOccurrenceRequest(
                    occurrenceId,
                    ScheduleId: null,
                    agentId,
                    "run",
                    "{}",
                    configuration.Revision.Id,
                    nowUtc,
                    TriggerType.CommandLine,
                    AttemptNumber: 0,
                    ParentOccurrenceId: null,
                    InitialStatus: OccurrenceStatus.Ready),
                cancellationToken).ConfigureAwait(false);
            _ = await occurrenceRepository.TryClaimAsync(
                occurrenceId,
                "development-cli",
                nowUtc,
                TimeSpan.FromMinutes(1),
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The demonstration occurrence could not be claimed.");

            AgentRunId runId = AgentRunId.New();
            _ = await runRepository.CreateAsync(
                new AgentRunRecord(
                    runId,
                    occurrenceId,
                    agentId,
                    configuration.Revision.Id,
                    configuration.Revision.ConfigurationHash,
                    TriggerType.CommandLine,
                    AgentRunStatus.Starting,
                    "development",
                    "1.0.0",
                    "1.0",
                    new string('0', 64),
                    Environment.MachineName,
                    Environment.ProcessId,
                    ProcessStartedAtUtc: nowUtc,
                    StartedAtUtc: nowUtc,
                    LastHeartbeatAtUtc: null,
                    CompletedAtUtc: null,
                    DurationMilliseconds: null,
                    ExitCode: null,
                    SummaryText: null,
                    SummaryJson: null,
                    ErrorType: null,
                    ErrorMessage: null,
                    CreatedAtUtc: nowUtc,
                    UpdatedAtUtc: nowUtc,
                    ConcurrencyToken: 1),
                cancellationToken).ConfigureAwait(false);

            byte[] artifactBytes = Encoding.UTF8.GetBytes("persistence-demo-artifact");
            RunArtifactRecord artifact;
            try
            {
                await using var content = new MemoryStream(artifactBytes, writable: false);
                artifact = await artifactStore.WriteAsync(
                    new ArtifactWriteRequest(
                        runId,
                        agentId,
                        "diagnostic",
                        "persistence-demo.txt",
                        "text/plain",
                        content,
                        DeleteAfterUtc: null),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(artifactBytes);
            }

            AuditEventRecord audit = await auditWriter.WriteAsync(
                new WriteAuditEventRequest(
                    AuditActorType.System,
                    "development-cli",
                    "persistence.demo-completed",
                    "agent-run",
                    runId.ToString(),
                    AuditOutcome.Succeeded,
                    Guid.NewGuid(),
                    runId,
                    JsonSerializer.SerializeToElement(new { artifactId = artifact.Id })),
                cancellationToken).ConfigureAwait(false);

            await output.WriteLineAsync("database=ready").ConfigureAwait(false);
            await output.WriteLineAsync($"configurationRevision={configuration.Revision.Id:D}").ConfigureAwait(false);
            await output.WriteLineAsync("secretRoundTrip=passed").ConfigureAwait(false);
            await output.WriteLineAsync($"leaseReleased={leaseReleased.ToString().ToLowerInvariant()}").ConfigureAwait(false);
            await output.WriteLineAsync($"artifact={artifact.Id:D}").ConfigureAwait(false);
            await output.WriteLineAsync($"audit={audit.Id:D}").ConfigureAwait(false);
            await output.WriteLineAsync("persistenceDemo=passed").ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Persistence demo failed: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            if (ownsDirectory && Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    private static string? ParseDataDirectory(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return null;
        }

        if (arguments.Count == 2 && arguments[0] == "--data-directory" && !string.IsNullOrWhiteSpace(arguments[1]))
        {
            return Path.GetFullPath(arguments[1]);
        }

        throw new ArgumentException("Use persistence-demo with an optional --data-directory path.", nameof(arguments));
    }
}
