namespace PristonTale.Contracts.Characters;

// The length of a character's name, as character creation takes it (trimmed).
public static class CharacterNames
{
    public const int MinLength = 3;
    public const int MaxLength = 16;

    public static bool IsValidLength(string name) => name.Length is >= MinLength and <= MaxLength;
}
