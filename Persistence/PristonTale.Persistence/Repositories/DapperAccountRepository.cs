using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperAccountRepository(IDbConnectionFactory connectionFactory) : IAccountRepository
{
    public async Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
            "SELECT id, user_id, password_hash, is_blocked, is_tester, gm_level FROM accounts WHERE user_id = @userId",
            new { userId }, cancellationToken: ct));
    }

    public async Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<AccountRecord>(new CommandDefinition(
            """
            INSERT INTO accounts (user_id, password_hash)
            VALUES (@userId, @passwordHash)
            RETURNING id, user_id, password_hash, is_blocked, is_tester, gm_level
            """,
            new { userId, passwordHash }, cancellationToken: ct));
    }

    public async Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET last_login_at = @now WHERE id = @accountId",
            new { accountId, now = DateTime.UtcNow }, cancellationToken: ct));
    }
}
