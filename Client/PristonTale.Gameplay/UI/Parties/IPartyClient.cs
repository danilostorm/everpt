using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.UI.Parties;

// The party requests the HUD sends (GameScene); none is answered, the members and chat lines show what changed.
public interface IPartyClient
{
    Task InviteToPartyAsync(ulong targetId);

    Task AnswerPartyAsync(ulong fromId, bool accept);

    Task CommandPartyAsync(PartyCommand command, ulong memberId);
}
