using System.Data.Common;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Application.Services;

// Accounts:CreateOnLogin (docs/server.md): the first login to a free account name creates the account with that password.
// Accounts:DefaultGameMasterLevel: the least game master level of every account's session; its stored level still counts.
public sealed record AccountPolicy(bool CreateOnLogin, int DefaultGameMasterLevel = 0)
{
    public const int MinimumNameLength = 3;
    public const int MaximumNameLength = 20;

    public static bool IsValidName(string name)
    {
        if (name.Length is < MinimumNameLength or > MaximumNameLength)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class AuthAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;
    private readonly AccountPolicy _policy;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts, AccountPolicy? policy = null)
    {
        _sessions = sessions;
        _accounts = accounts;
        _policy = policy ?? new AccountPolicy(false);
    }

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        var record = await _accounts.FindByUserIdAsync(account, ct);

        if (record is null && _policy.CreateOnLogin && AccountPolicy.IsValidName(account))
        {
            record = await CreateAsync(account, password, ct);
        }

        if (record is null)
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        if (record.IsBlocked)
        {
            return LoginOutcome.Denied(LoginFailure.LoginDenied);
        }

        if (!PasswordHasher.Verify(password, record.PasswordHash))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);
        session.GmLevel = Math.Clamp(Math.Max(record.GmLevel, _policy.DefaultGameMasterLevel), 0, GameMasters.GameMasterLevel.Highest);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }

    private async Task<AccountRecord?> CreateAsync(string account, string password, CancellationToken ct)
    {
        try
        {
            return await _accounts.CreateAsync(account, PasswordHasher.Hash(password), ct);
        }
        catch (DbException)
        {
            // Another login created the name first: the password is checked against that account.
            return await _accounts.FindByUserIdAsync(account, ct);
        }
    }
}
