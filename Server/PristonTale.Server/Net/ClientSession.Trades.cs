using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Trades;

namespace PristonTale.Server.Net;

// Trade requests. None is answered: TradeState and chat lines show what changed.
internal sealed partial class ClientSession
{
    private Task RequestTradeAsync(TradeRequestPacket request) =>
        _player is null ? Task.CompletedTask : _services.Trades.RequestAsync(RequireToken(), request.TargetId);

    private Task AnswerTradeAsync(TradeAnswerPacket answer) =>
        _player is null ? Task.CompletedTask : _services.Trades.AnswerAsync(RequireToken(), answer.FromId, answer.Accept);

    private Task OfferTradeAsync(TradeOfferPacket offer) =>
        _player is null
            ? Task.CompletedTask
            : _services.Trades.OfferAsync(RequireToken(), new TradeOffer(offer.Entries.Select(static entry => new TradeEntry(entry.ItemId, entry.Cell)).ToArray(),
                offer.Money));

    private Task SignalTradeAsync(TradeSignalPacket signal) =>
        _player is null ? Task.CompletedTask
        : signal.Code == Protocol.Binary.EtervaleOpcodes.TradeLock ? _services.Trades.LockAsync(RequireToken())
        : _services.Trades.CancelAsync(RequireToken());
}
