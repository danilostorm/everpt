using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Settings;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperServerSettingsRepository(IDbConnectionFactory connectionFactory) : IServerSettingsRepository
{
    public async Task SeedAsync(IReadOnlyList<ServerSettingSeed> seeds, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var seed in seeds)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO server_settings (name, value, description, updated_at) VALUES (@Name, @DefaultValue, @Description, @now)
                ON CONFLICT (name) DO UPDATE SET description = excluded.description
                """,
                new { seed.Name, seed.DefaultValue, seed.Description, now = DateTime.UtcNow }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(string Name, string Value)>(new CommandDefinition(
            "SELECT name, value FROM server_settings", cancellationToken: ct));
        return rows.ToDictionary(static row => row.Name, static row => row.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> SetAsync(string name, string value, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE server_settings SET value = @value, updated_at = @now WHERE name = @name",
            new { name, value, now = DateTime.UtcNow }, cancellationToken: ct));
        return rows > 0;
    }
}
