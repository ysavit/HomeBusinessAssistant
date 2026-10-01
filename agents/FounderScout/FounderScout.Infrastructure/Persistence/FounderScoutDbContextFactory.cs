using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FounderScout.Infrastructure.Persistence;

/// <summary>Creates short-lived Founder Scout contexts with consistent SQLite settings.</summary>
public sealed class FounderScoutDbContextFactory : IDbContextFactory<FounderScoutDbContext>
{
    private readonly DbContextOptions<FounderScoutDbContext> options;

    private FounderScoutDbContextFactory(DbContextOptions<FounderScoutDbContext> options) => this.options = options;

    /// <summary>Creates a factory for a validated absolute database path.</summary>
    public static FounderScoutDbContextFactory Create(string databasePath, TimeSpan busyTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (busyTimeout <= TimeSpan.Zero || busyTimeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeout));
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling(busyTimeout.TotalSeconds),
        }.ToString();
        var builder = new DbContextOptionsBuilder<FounderScoutDbContext>();
        builder.UseSqlite(connectionString, sqlite =>
            sqlite.MigrationsAssembly(typeof(FounderScoutDbContext).Assembly.GetName().Name));
        builder.AddInterceptors(new FounderScoutSqlitePragmaInterceptor(busyTimeout));
        builder.EnableDetailedErrors();
        builder.EnableSensitiveDataLogging(false);
        return new(builder.Options);
    }

    /// <summary>Opens an existing database for list and reporting queries without bootstrap writes.</summary>
    public static FounderScoutDbContextFactory CreateReadOnly(string databasePath, TimeSpan busyTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (busyTimeout <= TimeSpan.Zero || busyTimeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeout));
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling(busyTimeout.TotalSeconds),
        }.ToString();
        var builder = new DbContextOptionsBuilder<FounderScoutDbContext>();
        builder.UseSqlite(connectionString);
        builder.EnableDetailedErrors();
        builder.EnableSensitiveDataLogging(false);
        return new(builder.Options);
    }

    /// <inheritdoc />
    public FounderScoutDbContext CreateDbContext() => new(options);

    /// <inheritdoc />
    public Task<FounderScoutDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}

internal sealed class FounderScoutSqlitePragmaInterceptor(TimeSpan busyTimeout)
    : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
{
    private readonly int busyTimeoutMilliseconds = (int)Math.Ceiling(busyTimeout.TotalMilliseconds);

    public override void ConnectionOpened(
        System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData) => Configure(connection);

    public override async Task ConnectionOpenedAsync(
        System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

    private void Configure(System.Data.Common.DbConnection connection)
    {
        using System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = CommandText();
        _ = command.ExecuteNonQuery();
    }

    private async Task ConfigureAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = CommandText();
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string CommandText() =>
        $"PRAGMA foreign_keys=ON; PRAGMA busy_timeout={busyTimeoutMilliseconds}; PRAGMA synchronous=NORMAL;";
}

/// <summary>Design-time factory for Founder Scout EF migration tooling.</summary>
public sealed class FounderScoutDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FounderScoutDbContext>
{
    /// <inheritdoc />
    public FounderScoutDbContext CreateDbContext(string[] args)
    {
        string path = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath("founders.design.db");
        return FounderScoutDbContextFactory.Create(path, TimeSpan.FromSeconds(5)).CreateDbContext();
    }
}
