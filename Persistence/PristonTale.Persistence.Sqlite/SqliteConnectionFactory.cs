using System.Data.Common;
using Microsoft.Data.Sqlite;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Sqlite;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private int _journalConfigured;

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        // Single chokepoint that guarantees the SQLite Dapper config (bool<->INTEGER, underscores) is
        // applied before any query runs — even when repos are constructed directly without the DI extension.
        SqliteTypeMapping.EnsureRegistered();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();
    }

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var pragma = connection.CreateCommand();
        // WAL lets readers proceed during a write and, with synchronous=NORMAL, commits without an fsync per
        // transaction; the rollback journal capped a login burst at a few hundred writes per second.
        // journal_mode is stored in the file, synchronous applies per connection.
        pragma.CommandText = Interlocked.Exchange(ref _journalConfigured, 1) == 0
            ? "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;"
            : "PRAGMA synchronous=NORMAL;";
        await pragma.ExecuteNonQueryAsync(ct);
        return connection;
    }
}
