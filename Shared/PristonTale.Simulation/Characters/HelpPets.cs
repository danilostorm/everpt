using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Simulation.Characters;

// The beginner's helper pet (the original's low-level pet): one of the four helper dragons (1 Terry, 2 Nepsys, 3 Io,
// 4 Mute), drawn at random for a character below level 10 and beside it until level 10 unless the player hides it with
// the chat command. It fights as the premium helper pets do, whose timers take precedence.
public static class HelpPets
{
    public const int Kinds = 4;
    // The level from which the beginner's pet leaves.
    public const int LeaveLevel = 10;
    public static readonly string[] Commands = ["/도우미", "/helper"];

    // The helper pet beside the character: a premium one, else its beginner's pet while it shows.
    public static PremiumEffectKind? Active(PlayerCharacter character) =>
        PremiumPets.HelperPet(character.Premium)
        ?? (character.HelpPet != 0 && !character.HelpPetHidden && character.Level < LeaveLevel
            ? Effect(character.HelpPet)
            : null);

    // A helper pet's kind (1 to 4) and its premium effect.
    public static int KindOf(PremiumEffectKind pet) => pet - PremiumEffectKind.HelpPetTerry + 1;

    public static PremiumEffectKind Effect(int kind) => (PremiumEffectKind)((int)PremiumEffectKind.HelpPetTerry + kind - 1);
}
