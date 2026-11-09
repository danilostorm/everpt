namespace PristonTale.Simulation.Items.Usable;

// Event foods: rice cakes, the turkey, the pumpkin and watermelons, chocolate and candy. The player eats one like a
// potion and recovers a roll of life, mana and stamina between the item's authored ranges (minimum inclusive, maximum
// exclusive).
public static class EventFoods
{
    public static IReadOnlyList<string> Keys { get; } = ["SP101", "SP102", "SP103", "SP115", "SP135", "SP136", "SP142"];

    // The soccer-ball potion's head lasts three hours of real time.
    public const long SoccerHeadSeconds = 3 * 3600;

    // A head change starting or ending guards the player for five seconds.
    public const float HeadChangeGuardMs = 5000f;
}
