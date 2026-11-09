using System.Data.Common;
using Npgsql;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Postgres;

public sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenAsync(CancellationToken ct = default) =>
        await dataSource.OpenConnectionAsync(ct);
}
