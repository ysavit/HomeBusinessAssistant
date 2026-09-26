using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>The central platform persistence context for `assistant.db`.</summary>
public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);

    private void EnforceAppendOnlyRows()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            bool appendOnly = entry.Entity is ConfigurationRevisionEntity or AgentRunEventEntity or AuditEventEntity;
            if (appendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("An append-only persistence record cannot be changed or deleted.");
            }
        }
    }
}
