using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Trades;

namespace PristonTale.Application.World;

// One trade between two players of a field (side 0 asked, side 1 accepted): each side's offer and lock. It changes on the
// tick's command phase, where what each side sees is rebuilt (WorldServer.RefreshTrade). Executing: both locked and the
// exchange is being stored; nothing changes the trade then.
public sealed class Trade
{
    private readonly ulong[] _sides;
    private readonly TradeOffer[] _offers = [TradeOffer.Empty, TradeOffer.Empty];
    private readonly bool[] _locked = new bool[2];
    private readonly long[] _changedAtMs = new long[2];
    private readonly TradeView?[] _views = new TradeView?[2];

    internal Trade(ulong first, ulong second) => _sides = [first, second];

    public ulong First => _sides[0];
    public ulong Second => _sides[1];
    public bool Executing { get; internal set; }

    // A side cancelled while the exchange was stored: the trade ends if the exchange does not go through.
    public bool Cancelling { get; internal set; }

    // 0 or 1; -1 for a player not in the trade.
    public int SideOf(ulong entityId) => entityId == _sides[0] ? 0 : entityId == _sides[1] ? 1 : -1;

    public ulong Of(int side) => _sides[side];

    public TradeOffer OfferOf(int side) => _offers[side];

    public bool IsLocked(int side) => _locked[side];

    public bool BothLocked => _locked[0] && _locked[1];

    // When the side last changed its offer: the other side may not lock for TradeLimits.LockDelayMs after it.
    public long ChangedAtMs(int side) => _changedAtMs[side];

    public TradeView? ViewFor(ulong entityId) => SideOf(entityId) is var side and >= 0 ? _views[side] : null;

    // A changed offer unlocks both sides.
    internal void SetOffer(int side, TradeOffer offer, long nowMs)
    {
        _offers[side] = offer;
        _changedAtMs[side] = nowMs;
        Unlock();
    }

    internal void Lock(int side) => _locked[side] = true;

    internal void Unlock()
    {
        _locked[0] = _locked[1] = false;
        Executing = Cancelling = false;
    }

    internal void SetViews(TradeView first, TradeView second) => (_views[0], _views[1]) = (first, second);
}

// What a side's trade window shows: the partner, its own offer as the server holds it, the partner's items as this side
// sees them (each copy's values for its class, anchored at its trade grid cell), the partner's gold and both locks.
public sealed record TradeView(ulong PartnerId, string PartnerName, TradeOffer Own, bool OwnLocked, IReadOnlyList<OwnedItemView> PartnerItems,
    long PartnerMoney, bool PartnerLocked);

// A request to trade waiting for its answer.
public sealed record TradeRequest(ulong FromId, string FromName, long ExpiresMs);
