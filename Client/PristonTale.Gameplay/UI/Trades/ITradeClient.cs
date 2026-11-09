using PristonTale.Simulation.Trades;

namespace PristonTale.Gameplay.UI.Trades;

// The trade requests the HUD sends (GameScene); none is answered, the window and chat lines show what changed.
public interface ITradeClient
{
    Task RequestTradeAsync(ulong targetId);

    Task AnswerTradeAsync(ulong fromId, bool accept);

    Task OfferTradeAsync(TradeOffer offer);

    Task LockTradeAsync();

    Task CancelTradeAsync();
}
