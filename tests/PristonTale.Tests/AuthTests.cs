using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;

namespace PristonTale.Tests;

internal static class AuthTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("server login requires an existing account and a valid password", LoginAsync);
    }

    private static async Task LoginAsync()
    {
        var accounts = new AccountRepository();
        var auth = new AuthAppService(new SessionRegistry(), accounts);
        var unknown = await auth.LoginAsync("missing", "password");
        Assert.True(!unknown.Ok && accounts.CreateCalls == 0 && accounts.LoginUpdates == 0,
            "unknown login created an account or session");

        accounts.Record = new AccountRecord(1, "player", PasswordHasher.Hash("correct"), false, false);
        var wrong = await auth.LoginAsync("player", "wrong");
        Assert.True(!wrong.Ok && accounts.LoginUpdates == 0, "wrong password updated the account login");

        accounts.Record = accounts.Record with { IsBlocked = true };
        var blocked = await auth.LoginAsync("player", "correct");
        Assert.True(!blocked.Ok && accounts.LoginUpdates == 0, "blocked account logged in");

        accounts.Record = accounts.Record with { IsBlocked = false };
        var valid = await auth.LoginAsync("player", "correct");
        Assert.True(valid.Ok && valid.SessionToken.Length > 0 && accounts.LoginUpdates == 1,
            "valid account did not receive a session");
    }

    private sealed class AccountRepository : IAccountRepository
    {
        public AccountRecord? Record { get; set; }
        public int CreateCalls { get; private set; }
        public int LoginUpdates { get; private set; }

        public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(Record?.UserId == userId ? Record : null);

        public Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            CreateCalls++;
            throw new InvalidOperationException("Login attempted to create an account.");
        }

        public Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default)
        {
            LoginUpdates++;
            return Task.CompletedTask;
        }
    }
}
