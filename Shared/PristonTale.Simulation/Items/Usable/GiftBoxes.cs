namespace PristonTale.Simulation.Items.Usable;

// One prize of a box: the item, its weight out of 10000 and how many come (capped by the item's stack size).
public readonly record struct BoxPrize(string CatalogKey, int Weight, int Count = 1);

// Gift boxes SP105 (grade 1, best) to SP110 (grade 6) and the tiger capsule. Opening one needs room for a 2 x 2 item and
// ten free weight (five for the capsule); the player eats it like food and the prize lands in the inventory.
public static class GiftBoxes
{
    public const int TigerCapsule = 7;
    public const int BoxWeightMargin = 10;
    public const int CapsuleWeightMargin = 5;
    public const int RoomWidth = 2;
    public const int RoomHeight = 2;

    private static readonly BoxPrize[][] Tables =
    [
        [new("PM104", 2000, 10), new("BI108", 2900), new("GP119", 1000), new("FO108", 1500), new("FO108", 1000), new("BI209", 100), new("BI121", 750),
            new("BI119", 750)],
        [new("PM104", 2600, 10), new("BI108", 2620), new("GP119", 700), new("FO108", 1000), new("FO107", 1500), new("BI209", 80), new("BI121", 750),
            new("BI119", 750)],
        [new("PM104", 2750, 10), new("BI108", 2400), new("GP118", 800), new("FO107", 1500), new("FO106", 1000), new("BI209", 50), new("BI121", 750),
            new("BI119", 750)],
        [new("PM103", 2450, 10), new("BI108", 2850), new("GP118", 500), new("FO106", 1500), new("FO105", 1000), new("BI205", 200), new("BI121", 750),
            new("BI119", 750)],
        [new("PM103", 2320, 10), new("BI108", 2000), new("GP117", 1500), new("FO104", 1500), new("FO104", 1000), new("BI205", 180), new("BI121", 750),
            new("BI119", 750)],
        [new("PM102", 2850, 10), new("BI108", 2000), new("GP117", 1000), new("FO104", 1500), new("FO104", 1000), new("BI205", 150), new("BI121", 750),
            new("BI119", 750)],
        [new("FO106", 1000), new("FO106", 500), new("FO107", 1000), new("FO108", 1000), new("FO109", 1000), new("FO110", 800), new("FO125", 1000),
            new("FO125", 600), new("FO126", 1000), new("FO127", 1000), new("FO128", 800, 30), new("SE101", 300, 30)]
    ];

    // Grade 1..6, or TigerCapsule.
    public static IReadOnlyList<BoxPrize> Prizes(int grade) => Tables[grade - 1];

    // The prize for a roll in 0..9999.
    public static BoxPrize Draw(int grade, int roll)
    {
        var table = Tables[grade - 1];
        var sum = 0;
        foreach (var prize in table)
        {
            sum += prize.Weight;
            if (roll < sum)
            {
                return prize;
            }
        }

        return table[^1];
    }

    public static int WeightMargin(int grade) => grade == TigerCapsule ? CapsuleWeightMargin : BoxWeightMargin;

    // The box grade an event drop takes from the killed monster's level.
    public static string BoxForMonsterLevel(int level) => level switch
    {
        < 20 => "SP110",
        < 40 => "SP109",
        < 60 => "SP108",
        < 80 => "SP107",
        < 100 => "SP106",
        _ => "SP105"
    };
}
