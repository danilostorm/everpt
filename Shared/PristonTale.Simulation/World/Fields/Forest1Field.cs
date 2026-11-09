namespace PristonTale.Simulation.World.Fields;

public static class Forest1Field
{
    public static readonly FieldDef Definition = new(
        "fore-1",
        "Forest 1",
        FieldState.Forest,
        FieldBackdrops.Forest,
        "Field\\forest\\fore-1.smd",
        [])
    {
        FieldCode = 2,
        Center = new FieldPoint(4184, -11016),
        StartPoints =
        [
            new FieldPoint(1350, -13672),
            new FieldPoint(1761, -10815),
            new FieldPoint(4777, -10916)
        ],
        Gates =
        [
            new FieldGate("ruin-4", 4844, -6835, 0),
            new FieldGate("village-2", 2275, -14828, 0)
        ],
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
