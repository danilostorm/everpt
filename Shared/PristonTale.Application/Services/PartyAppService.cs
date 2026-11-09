using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;

namespace PristonTale.Application.Services;

// Party requests outside the tick (WorldNoticeRequests).
public sealed class PartyAppService(ISessionRegistry sessions, WorldServer world, ChatDirectory chat)
{
    public Task InviteAsync(string sessionToken, ulong targetId) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.InviteToPartyAsync(session, targetId));

    public Task AnswerAsync(string sessionToken, ulong fromId, bool accept) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.AnswerPartyAsync(session, fromId, accept));

    public Task CommandAsync(string sessionToken, PartyCommand command, ulong memberId) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.CommandPartyAsync(session, command, memberId));
}
