namespace PristonTale.Persistence.Abstractions.Accounts;

public interface IAccountRepository
{
    Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default);
    Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default);
    Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default);
}
