using PristonTale.Application.Services;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Gives the starter gifts the world tick finds due (WorldServer.StarterGiftRequests), a few players at a time, off the tick.
public sealed class StarterGiftService(WorldServer world, StarterGiftAppService gifts, ILogger<StarterGiftService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        SessionWork.RunAsync(world.StarterGiftRequests, gifts.GiveAsync, logger, "Starter gift", stoppingToken);
}
