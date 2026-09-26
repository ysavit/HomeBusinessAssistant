using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>Current central database connectivity and migration readiness.</summary>
public sealed record AssistantDatabaseReadiness(bool CanConnect, bool HasPendingMigrations);

/// <summary>Performs a bounded read-only readiness check behind the Infrastructure boundary.</summary>
public sealed class AssistantDatabaseReadinessProbe(AssistantDbContextFactory contextFactory)
{
    /// <summary>Checks central database connectivity and pending EF migrations.</summary>
    public async ValueTask<AssistantDatabaseReadiness> CheckAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        bool canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        bool pending = canConnect
            && (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
        return new(canConnect, pending);
    }
}
