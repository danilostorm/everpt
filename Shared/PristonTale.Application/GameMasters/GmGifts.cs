using PristonTale.Application.Chat;
using PristonTale.Application.Services;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Application.GameMasters;

// A game master's gift into a player's bag follows the path of every inventory change: the player's save gate, the copy
// made and checked on the tick, the commit on the tick, and the character stored in the background.
internal static class GmGifts
{
    public static async Task<bool> GiveAsync(GameMasterCommands commands, ChatMember issuer, ChatMember target, Func<ISimRng, ItemInstance> make)
    {
        if (commands.Saves is null)
        {
            return false;
        }

        using var lease = await new DurableOperations(commands.Saves).AdmitAsync(target.Session, TimeSpan.Zero, CancellationToken.None);
        return await lease.RunAsync(
            session => commands.World.PrepareGiftAsync(issuer.EntityId, session, make),
            commands.World.CommitGiftAsync,
            prepared => prepared.Outcome,
            _ => false);
    }
}
