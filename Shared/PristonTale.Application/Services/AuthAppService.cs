using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Contracts.Sessions;

namespace PristonTale.Application.Services;

public sealed class AuthAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts)
    {
        _sessions = sessions;
        _accounts = accounts;
    }

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            return LoginOutcome.Denied(LoginFailure.BadCredentials);
        }

        var record = await _accounts.FindByUserIdAsync(account, ct);

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
        session.GmLevel = Math.Clamp(record.GmLevel, 0, GameMasters.GameMasterLevel.Highest);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }
}
