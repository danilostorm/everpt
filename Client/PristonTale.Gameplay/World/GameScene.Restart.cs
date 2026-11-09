using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Progression;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World;

// The local player's death: the restart offer the server sends while it is down, and its choice of where to stand up.
public sealed partial class GameScene
{
    // What standing up again would cost, as last sent while the player was down.
    public RestartOffer? RestartOffer { get; private set; }

    public bool PlayerDead => _playerController.IsDead;

    public Task<RestartOutcome> RestartAsync(RestartChoice choice) =>
        _playChannel is not null ? _playChannel.RestartAsync(choice, _presenceCts.Token) : Task.FromResult(new RestartOutcome(RestartStatus.Rejected, choice));

    private void ObserveRestart(PresenceUpdate update)
    {
        if (update.RestartOffer is { } offer)
        {
            RestartOffer = offer;
        }
    }
}
