using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

/// <summary>The separate Founder Scout bounded-context database.</summary>
public sealed class FounderScoutDbContext(DbContextOptions<FounderScoutDbContext> options) : DbContext(options)
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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BrowserAccountConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoverySegmentConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new CandidateConfiguration());
        modelBuilder.ApplyConfiguration(new ProfileSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new CandidateIdentityAliasConfiguration());
        modelBuilder.ApplyConfiguration(new ScreeningDecisionConfiguration());
        modelBuilder.ApplyConfiguration(new CandidateIdentityConflictConfiguration());
        modelBuilder.ApplyConfiguration(new EvaluationConfiguration());
        modelBuilder.ApplyConfiguration(new EvaluationCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new EvaluationRiskConfiguration());
        modelBuilder.ApplyConfiguration(new InvitationDraftConfiguration());
        modelBuilder.ApplyConfiguration(new InvitationDraftRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new InvitationQueueEntryConfiguration());
        modelBuilder.ApplyConfiguration(new CandidateActionConfiguration());
        modelBuilder.ApplyConfiguration(new ManualInvitationWindowConfiguration());
        modelBuilder.ApplyConfiguration(new ReportExportConfiguration());
        modelBuilder.ApplyConfiguration(new FounderScoutLeaseConfiguration());
    }

    private void EnforceAppendOnlyRows()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ChangeTracker.Entries())
        {
            bool appendOnly = entry.Entity is CandidateActionEntity
                or DiscoveryCheckpointEntity
                or ScreeningDecisionEntity
                or CandidateIdentityConflictEntity
                or EvaluationCategoryEntity
                or EvaluationRiskEntity
                or ReportExportEntity;
            if (appendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("An append-only Founder Scout record cannot be changed or deleted.");
            }
        }
    }
}
