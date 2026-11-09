using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.World;

namespace PristonTale.Application.Services;

// The answer to a wing gate trip; Arrival is where the player now stands when it warped.
public sealed record WingGateReply(WingGateOutcome Outcome, TeleportArrival? Arrival = null);

// A trip through the wing warp gates: the tick prepares the fee and the place, then applies it; the character is stored
// in the background (DurableOperations).
public sealed class WingGateAppService(ISessionRegistry sessions, ICharacterSaves saves, WorldServer world)
{
    private readonly DurableOperations _durable = new(saves);

    public async Task<WingGateReply> TravelAsync(string sessionToken, WingGateCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return await lease.RunAsync(
            session => world.PrepareWingGateAsync(session, command),
            world.CommitWingGateAsync,
            prepared => new WingGateReply(prepared.Outcome, prepared.Arrival),
            failure => new WingGateReply(new WingGateOutcome(failure switch
            {
                DurableFailure.Busy => WingGateStatus.Busy,
                _ => WingGateStatus.Rejected
            }, command.Destination)));
    }
}
