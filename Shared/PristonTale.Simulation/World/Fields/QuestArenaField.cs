using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

// The duel arena of the quests: reached only through a quest, without gates, start points or monsters of its own.
public static class QuestArenaField
{
    public static readonly FieldDef Definition = new(
        "quest_IV", "Quest Arena", FieldState.QuestArena, FieldBackdrops.Ruin2,
        "Field\\Quest\\quest_IV.smd", [])
    {
        FieldCode = FieldCodes.QuestArena,
        Center = new FieldPoint(22497, 9605),
        BackMusicCode = "BGM_CODE_SOD3",
        ServerCode = "PLAY_SERVER_CODE_EXTEND"
    };
}
