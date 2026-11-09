using PristonTale.Application.Services;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Stores the quest work the world tick queues (quest drops, quest weapon growth and expiry, leftovers) through the
// quest upkeep, a few players at a time, off the tick.
public sealed class QuestUpkeepService(WorldServer world, QuestAppService quests, ILogger<QuestUpkeepService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        SessionWork.RunAsync(world.QuestUpkeepRequests, quests.RunUpkeepAsync, logger, "Quest upkeep", stoppingToken);
}
