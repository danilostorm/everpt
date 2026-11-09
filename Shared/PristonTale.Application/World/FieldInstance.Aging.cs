using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// A player's own attacks count once a tick. A copy that reaches its goal asks the upkeep for its level.
public sealed partial class FieldInstance
{
    private const float AgingSecondMs = 1000f;
    private const long MaturedCheckIntervalMs = 5000;

    internal static void AgingAttack(WorldPlayer player, MonsterInstance monster, bool critical, long nowMs)
    {
        if (player.AgingCountedAtMs == nowMs || !WithinAgingSpread(player, monster))
        {
            return;
        }

        player.AgingCountedAtMs = nowMs;
        TallyAging(player, critical ? AgingCounter.Critical : AgingCounter.Attack);
    }

    internal static void AgingBlock(WorldPlayer player, MonsterInstance monster)
    {
        if (WithinAgingSpread(player, monster))
        {
            TallyAging(player, AgingCounter.Block);
        }
    }

    internal static void AgingBlow(WorldPlayer player, MonsterInstance monster)
    {
        if (WithinAgingSpread(player, monster))
        {
            TallyAging(player, AgingCounter.Hit);
        }
    }

    private void StepAging(float dtMs, long nowMs)
    {
        foreach (var player in _players.Values)
        {
            player.AgingWornMs += dtMs;
            if (player.AgingWornMs >= AgingSecondMs)
            {
                var seconds = (int)(player.AgingWornMs / AgingSecondMs);
                player.AgingWornMs -= seconds * AgingSecondMs;
                TallyAging(player, AgingCounter.Time, seconds);
            }

            if (nowMs >= player.AgingCheckAtMs)
            {
                // A level lost to a commit that replaced the worn copies is asked again.
                player.AgingCheckAtMs = nowMs + MaturedCheckIntervalMs;
                if (player.Character.HasMaturedItem)
                {
                    player.QuestState.UpkeepDue = true;
                }
            }
        }
    }

    private static bool WithinAgingSpread(WorldPlayer player, MonsterInstance monster) =>
        Math.Abs(monster.Def.Combat.Level - player.Character.Level) <= AgingRules.CountLevelSpread;

    private static void TallyAging(WorldPlayer player, AgingCounter counter, int amount = 1)
    {
        if (player.Character.TallyAging(counter, amount))
        {
            player.QuestState.UpkeepDue = true;
        }
    }
}
