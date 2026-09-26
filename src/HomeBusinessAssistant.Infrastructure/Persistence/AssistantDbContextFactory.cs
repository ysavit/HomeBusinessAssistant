using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>Creates short-lived central database contexts with consistent SQLite connection settings.</summary>
public sealed class AssistantDbContextFactory : IDbContextFactory<AssistantDbContext>
{
    private readonly DbContextOptions<AssistantDbContext> options;

    private AssistantDbContextFactory(DbContextOptions<AssistantDbContext> options) => this.options = options;

    /// <summary>Creates a factory for a validated absolute database path.</summary>
    public static AssistantDbContextFactory Create(string databasePath, TimeSpan busyTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (busyTimeout <= TimeSpan.Zero || busyTimeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeout), "The SQLite busy timeout must be between zero and sixty seconds.");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling(busyTimeout.TotalSeconds),
        }.ToString();

        var builder = new DbContextOptionsBuilder<AssistantDbContext>();
        builder.UseSqlite(connectionString, sqlite =>
            sqlite.MigrationsAssembly(typeof(AssistantDbContext).Assembly.GetName().Name));
        builder.AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeout));
        builder.EnableDetailedErrors();
        builder.EnableSensitiveDataLogging(false);
        return new AssistantDbContextFactory(builder.Options);
    }

    /// <inheritdoc />
    public AssistantDbContext CreateDbContext() => new(options);

    /// <inheritdoc />
    public Task<AssistantDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}

internal sealed class SqlitePragmaConnectionInterceptor(TimeSpan busyTimeout)
    : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
{
    private readonly int busyTimeoutMilliseconds = (int)Math.Ceiling(busyTimeout.TotalMilliseconds);

    public override void ConnectionOpened(
        System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData) =>
        Configure(connection);

    public override async Task ConnectionOpenedAsync(
        System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

    private void Configure(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = GetCommandText();
        _ = command.ExecuteNonQuery();
    }

    private async Task ConfigureAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = GetCommandText();
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetCommandText() =>
        $"PRAGMA foreign_keys=ON; PRAGMA busy_timeout={busyTimeoutMilliseconds}; PRAGMA synchronous=NORMAL;";
}

/// <summary>Creates a design-time context only for EF migration tooling.</summary>
public sealed class AssistantDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    /// <inheritdoc />
    public AssistantDbContext CreateDbContext(string[] args)
    {
        string databasePath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath("assistant.design.db");
        return AssistantDbContextFactory.Create(databasePath, TimeSpan.FromSeconds(5)).CreateDbContext();
    }
}
