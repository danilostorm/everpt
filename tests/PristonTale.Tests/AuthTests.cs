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
        yield return ("server login creates a free account only when the policy allows it", CreateOnLoginAsync);
        yield return ("the default game master level is the least level of every session", DefaultLevelAsync);
    }

    private static async Task DefaultLevelAsync()
    {
        var accounts = new AccountRepository { Record = new AccountRecord(1, "player", PasswordHasher.Hash("correct"), false, false) };
        var sessions = new SessionRegistry();
        var auth = new AuthAppService(sessions, accounts, new AccountPolicy(false, 4));
        var login = await auth.LoginAsync("player", "correct");
        Assert.True(login.Ok && sessions.TryGet(login.SessionToken, out var session) && session.GmLevel == 4, "the default level did not reach the session");
    }

    private static async Task CreateOnLoginAsync()
    {
        var accounts = new AccountRepository { AllowCreate = true };
        var auth = new AuthAppService(new SessionRegistry(), accounts, new AccountPolicy(true));

        var invalid = await auth.LoginAsync("bad name!", "password");
        Assert.True(!invalid.Ok && accounts.CreateCalls == 0, "an invalid name created an account");

        var created = await auth.LoginAsync("newplayer", "first");
        Assert.True(created.Ok && accounts.CreateCalls == 1 && accounts.Record?.UserId == "newplayer", "a free name did not create an account");

        var wrong = await auth.LoginAsync("newplayer", "other");
        Assert.True(!wrong.Ok && accounts.CreateCalls == 1, "a wrong password logged in or created another account");

        var again = await auth.LoginAsync("newplayer", "first");
        Assert.True(again.Ok && accounts.CreateCalls == 1, "the created account did not log in with its first password");
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
        public bool AllowCreate { get; init; }
        public int CreateCalls { get; private set; }
        public int LoginUpdates { get; private set; }

        public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(Record?.UserId == userId ? Record : null);

        public Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            CreateCalls++;
            if (!AllowCreate)
            {
                throw new InvalidOperationException("Login attempted to create an account.");
            }

            Record = new AccountRecord(1, userId, passwordHash, false, false);
            return Task.FromResult(Record);
        }

        public Task<bool> UpdatePasswordHashAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            if (Record?.UserId != userId) return Task.FromResult(false);
            Record = Record with { PasswordHash = passwordHash };
            return Task.FromResult(true);
        }

        public Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default)
        {
            LoginUpdates++;
            return Task.CompletedTask;
        }
    }
}
