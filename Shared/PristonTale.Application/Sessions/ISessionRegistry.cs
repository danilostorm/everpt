namespace PristonTale.Application.Sessions;

public interface ISessionRegistry
{
    PlayerSession Create(long accountId, string userId);
    bool TryGet(string sessionToken, out PlayerSession session);
    void Remove(string sessionToken);
    bool TryClaimCharacter(long characterId, string sessionToken);
    void ReleaseCharacter(long characterId, string sessionToken);
}
