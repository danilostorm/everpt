using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.Services;

// Gives a new character its next starter gift through the path of every inventory change (the save gate, the copy made
// and checked on the tick, the commit, the background save) and tells the player in the chat. A gate held by another
// operation leaves the gift for the next offer.
public sealed class StarterGiftAppService(WorldServer world, ICharacterSaves saves, ChatDirectory chat)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task GiveAsync(PlayerSession session, CancellationToken ct)
    {
        try
        {
            using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
            var given = await lease.RunAsync(
                world.PrepareStarterGiftAsync,
                world.CommitStarterGiftAsync,
                static prepared => prepared.Snapshot is null ? null : prepared.Outcome.ItemName,
                static _ => null);
            if (given is not null && chat.TryGet(session.EntityId, out var member))
            {
                member.Link.Deliver(ChatLine.Reply(StarterGifts.Message(given)));
            }
        }
        finally
        {
            await world.EndStarterGiftAsync(session);
        }
    }
}
