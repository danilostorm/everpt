namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record CharacterSaveSnapshot(
    CharacterRecord Scalars,
    SavedItems Inventory,
    SavedItems Equipment,
    SavedSkills Skills,
    SavedPremium Premium,
    SavedQuests? Quests = null)
{
    public CharacterRecord Encode()
    {
        return Scalars with
        {
            Inventory = CharacterJson.Write(Inventory),
            Equipment = CharacterJson.Write(Equipment),
            Skills = CharacterJson.Write(Skills),
            Premium = CharacterJson.Write(Premium),
            Quests = Quests is null ? Scalars.Quests : CharacterJson.Write(Quests)
        };
    }
}
