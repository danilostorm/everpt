using System.Collections.Concurrent;

namespace PristonTale.Application.Sessions;

public sealed class SessionRegistry : ISessionRegistry
{
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();
    private long _nextEntityId;
    private readonly ConcurrentDictionary<long, string> _characterOwners = new();

    public PlayerSession Create(long accountId, string userId)
    {
        var token = Guid.NewGuid().ToString("N");
        var session = new PlayerSession
        {
            SessionToken = token,
            AccountId = accountId,
            UserId = userId,
            EntityId = (ulong)Interlocked.Increment(ref _nextEntityId),
        };
        _sessions[token] = session;
        return session;
    }

    public bool TryGet(string sessionToken, out PlayerSession session)
        => _sessions.TryGetValue(sessionToken, out session!);

    public bool TryClaimCharacter(long characterId, string sessionToken)
    {
        return _characterOwners.TryAdd(characterId, sessionToken);
    }

    public void ReleaseCharacter(long characterId, string sessionToken)
    {
        _characterOwners.TryRemove(new KeyValuePair<long, string>(characterId, sessionToken));
    }

    public void Remove(string sessionToken)
    {
        if (_sessions.TryRemove(sessionToken, out var session))
        {
            session.MarkClosed();
            if (session.Loaded is not null)
            {
                ReleaseCharacter(session.Loaded.Id, sessionToken);
            }
        }
    }
}
