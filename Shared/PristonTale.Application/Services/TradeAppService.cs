using PristonTale.Application.Saves;
using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Trades;

namespace PristonTale.Application.Services;

// Trades outside the tick (WorldNoticeRequests). The lock that completes a trade holds the locking side's save gate, then
// waits up to PartnerGateWait for its partner's (the partner's own lock or a save holds it briefly; nothing holds the
// partner's gate while waiting for the locker's, so the wait cannot deadlock). It stores both characters in one
// transaction and commits both on the tick; a partner's gate still busy unlocks the trade.
public sealed class TradeAppService(ISessionRegistry sessions, ICharacterSaves saves, ICharacterRepository characters, WorldServer world, ChatDirectory chat)
{
    private static readonly TimeSpan PartnerGateWait = TimeSpan.FromSeconds(2);
    private readonly DurableOperations _durable = new(saves);

    public Task RequestAsync(string sessionToken, ulong targetId) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.RequestTradeAsync(session, targetId));

    public Task AnswerAsync(string sessionToken, ulong fromId, bool accept) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.AnswerTradeAsync(session, fromId, accept));

    public Task OfferAsync(string sessionToken, TradeOffer offer) =>
        WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, session => world.OfferTradeAsync(session, offer));

    public Task CancelAsync(string sessionToken) => WorldNoticeRequests.RunAsync(sessions, chat, sessionToken, world.CancelTradeAsync);

    public async Task LockAsync(string sessionToken, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is not null)
        {
            return;
        }

        TradeLock locked;
        try
        {
            locked = await world.LockTradeAsync(lease.Session);
        }
        catch (WorldBusyException)
        {
            return;
        }

        chat.Deliver(locked.Notices);
        if (locked is not { Trade: { } trade, Partner: { } partner })
        {
            return;
        }

        using var partnerLease = await _durable.AdmitAsync(partner, PartnerGateWait, CancellationToken.None);
        if (partnerLease.Failure is not null)
        {
            chat.Deliver(await world.ReleaseTradeAsync(trade, TradeStatus.Busy));
            return;
        }

        var notices = await lease.RunAsync(
            session => world.PrepareTradeAsync(session, partner, trade),
            async (session, prepared) => await world.CommitTradeAsync(session, partner, prepared),
            static prepared => prepared.Notices,
            static _ => (WorldNotice[])[],
            async (session, prepared) => await characters.UpdateAllAsync([session.Stamp(prepared.Snapshot!), partner.Stamp(prepared.PartnerSnapshot!)],
                    CancellationToken.None)
                ? null
                : DurableFailure.StorageFailed,
            async (_, _) => chat.Deliver(await world.ReleaseTradeAsync(trade, TradeStatus.StorageFailed)));
        chat.Deliver(notices);
    }
}
