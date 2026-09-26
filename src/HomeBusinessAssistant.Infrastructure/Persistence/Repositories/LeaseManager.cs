using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>SQLite-backed expiring leases with fencing tokens.</summary>
public sealed class LeaseManager(IDbContextFactory<AssistantDbContext> contextFactory) : ILeaseManager
{
    /// <inheritdoc />
    public async ValueTask<AgentLeaseRecord?> TryAcquireAsync(
        string leaseName,
        string ownerId,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        Validate(leaseName, ownerId, duration);
        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateTimeOffset expiresAt = normalizedNow.Add(duration);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<AgentLeaseEntity>()
            .Where(item => item.LeaseName == leaseName && item.ExpiresAtUtc <= normalizedNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.OwnerId, ownerId)
                .SetProperty(item => item.AcquiredAtUtc, normalizedNow)
                .SetProperty(item => item.ExpiresAtUtc, expiresAt)
                .SetProperty(item => item.FencingToken, item => item.FencingToken + 1), cancellationToken)
            .ConfigureAwait(false);
        if (updated == 1)
        {
            AgentLeaseEntity acquired = await context.Set<AgentLeaseEntity>().AsNoTracking()
                .SingleAsync(item => item.LeaseName == leaseName, cancellationToken)
                .ConfigureAwait(false);
            return PersistenceMapper.Map(acquired);
        }

        var created = new AgentLeaseEntity
        {
            LeaseName = leaseName,
            OwnerId = ownerId,
            AcquiredAtUtc = normalizedNow,
            ExpiresAtUtc = expiresAt,
            FencingToken = 1,
        };
        context.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return PersistenceMapper.Map(created);
        }
        catch (DbUpdateException exception) when (IsUniqueLeaseCollision(exception))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<AgentLeaseRecord?> TryRenewAsync(
        AgentLeaseRecord lease,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        Validate(lease.LeaseName, lease.OwnerId, duration);
        DateTimeOffset normalizedNow = nowUtc.ToUniversalTime();
        DateTimeOffset expiresAt = normalizedNow.Add(duration);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int updated = await context.Set<AgentLeaseEntity>()
            .Where(item => item.LeaseName == lease.LeaseName
                && item.OwnerId == lease.OwnerId
                && item.FencingToken == lease.FencingToken
                && item.ExpiresAtUtc > normalizedNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, expiresAt), cancellationToken)
            .ConfigureAwait(false);
        if (updated != 1)
        {
            return null;
        }

        return lease with { ExpiresAtUtc = expiresAt };
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAsync(AgentLeaseRecord lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int released = await context.Set<AgentLeaseEntity>()
            .Where(item => item.LeaseName == lease.LeaseName
                && item.OwnerId == lease.OwnerId
                && item.FencingToken == lease.FencingToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ExpiresAtUtc, DateTimeOffset.MinValue), cancellationToken)
            .ConfigureAwait(false);
        return released == 1;
    }

    private static void Validate(string leaseName, string ownerId, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(leaseName)
            || leaseName.Length > 128
            || string.IsNullOrWhiteSpace(ownerId)
            || ownerId.Length > 128
            || duration <= TimeSpan.Zero
            || duration > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("The lease request is invalid.");
        }
    }

    private static bool IsUniqueLeaseCollision(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: 19 })
            {
                return true;
            }
        }

        return false;
    }
}
