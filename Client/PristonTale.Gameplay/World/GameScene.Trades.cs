using PristonTale.Gameplay.UI.Trades;
using PristonTale.Simulation.Trades;

namespace PristonTale.Gameplay.World;

// Trade requests through the world connection.
public sealed partial class GameScene : ITradeClient
{
    public Task RequestTradeAsync(ulong targetId) => SendSocial(_playChannel?.RequestTradeAsync(targetId, _presenceCts.Token), "trade request");

    public Task AnswerTradeAsync(ulong fromId, bool accept) => SendSocial(_playChannel?.AnswerTradeAsync(fromId, accept, _presenceCts.Token), "trade answer");

    public Task OfferTradeAsync(TradeOffer offer) => SendSocial(_playChannel?.OfferTradeAsync(offer, _presenceCts.Token), "trade offer");

    public Task LockTradeAsync() => SendSocial(_playChannel?.LockTradeAsync(_presenceCts.Token), "trade lock");

    public Task CancelTradeAsync() => SendSocial(_playChannel?.CancelTradeAsync(_presenceCts.Token), "trade cancel");
}
