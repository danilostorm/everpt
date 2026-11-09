using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;

namespace PristonTale.Server.Net;

// The host side of the game masters' server commands: the connection count and refused logins of the TCP server, the
// player limit new connections are told about, and the shutdown countdown that stops the host.
public sealed class GameServerControl(ChatDirectory chat, IHostApplicationLifetime lifetime, ILogger<GameServerControl> logger) : IServerOperations
{
    private Func<int> _connections = static () => 0;
    private LoginThrottle? _logins;
    private int _userLimit;
    private int _shutdown;
    private string _serverName = Application.Settings.ServerSettings.DefaultServerName;

    public ShutdownSchedule Schedule { get; init; } = ShutdownSchedule.Standard;

    public int OpenConnections => _connections();

    public int UserLimit
    {
        get => Volatile.Read(ref _userLimit);
        set => Volatile.Write(ref _userLimit, Math.Max(0, value));
    }

    public string ServerName
    {
        get => Volatile.Read(ref _serverName);
        set => Volatile.Write(ref _serverName, value ?? "");
    }

    internal void Attach(Func<int> connections, LoginThrottle logins)
    {
        _connections = connections;
        _logins = logins;
    }

    // A connection that arrives while the world holds the limit is told the server is full (and only a game master may
    // still log in).
    public bool Full(int players)
    {
        var limit = UserLimit;
        return limit > 0 && players >= limit;
    }

    public bool BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
        {
            return false;
        }

        logger.LogWarning("A game master started the server shutdown ({Minutes} minutes).", Schedule.Countdown.TotalMinutes);
        _ = Task.Run(async () =>
        {
            try
            {
                await ShutdownCountdown.RunAsync(chat, Schedule, lifetime.StopApplication, cancellationToken: lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException)
            {
            }
        });
        return true;
    }

    public IReadOnlyList<BlockedLogin> BlockedLogins() =>
        _logins?.BlockedAccounts(Environment.TickCount64).Select(entry => new BlockedLogin(entry.Account, entry.RemainingMs)).ToArray() ?? [];

    public int ClearBlockedLogins() => _logins?.Clear(Environment.TickCount64) ?? 0;

    public bool ReleaseBlockedLogin(string account) => account.Length > 0 && _logins is not null && _logins.Release(account, Environment.TickCount64);
}
