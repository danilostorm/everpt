using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

public static class CharacterRecordFactory
{
    public static CharacterRecord NewStarter(
        CharacterClassRegistry classes,
        long accountId,
        string name,
        short slot,
        PlayerClass playerClass,
        int fieldId,
        int faceIndex = 0,
        int level = 1,
        short tier = 0,
        int hairIndex = 0,
        int sizeLevel = 0)
    {
        // CreatePlayer derives level-appropriate attributes and full vitals.
        var starter = classes.CreatePlayer(playerClass, name, level);
        var attributes = starter.Attributes;

        // Persist the selected face's head model rather than the class default.
        var defaultHead = classes.Get(playerClass).Definition.DefaultVisuals.Head.InxExtensionPath;
        var headForFace = CharacterFace.ResolveHeadPath(defaultHead, faceIndex);
        return new CharacterRecord(
            Id: 0,
            AccountId: accountId,
            Name: name,
            Slot: slot,
            JobCode: (int)playerClass,
            Level: starter.Level,
            Exp: 0,
            Money: 0,
            Brood: 0,
            Strength: attributes.Strength,
            Spirit: attributes.Spirit,
            Talent: attributes.Talent,
            Agility: attributes.Agility,
            Health: attributes.Health,
            StatPoints: 0,
            Life: starter.Life,
            Mana: starter.Mana,
            Stamina: starter.Stamina,
            FieldId: fieldId,
            PosX: 0f,
            PosZ: 0f,
            ModelName: string.Empty,
            ModelName2: headForFace,
            FaceIndex: faceIndex,
            Tier: tier,
            HairIndex: hairIndex,
            SizeLevel: sizeLevel,
            Inventory: JsonText.EmptyArray,
            Equipment: JsonText.EmptyObject,
            Skills: JsonText.EmptyArray,
            Quests: JsonText.EmptyObject,
            Premium: JsonText.EmptyObject,
            LastPlayedAt: null,
            StarterGiftsReceived: 0);
    }

    public static CharacterRecord FromLiveSession(
        CharacterRecord loaded, PlayerCharacter character, int fieldId, Vector3 position)
    {
        return Capture(loaded, character, fieldId, position).Encode();
    }

    public static CharacterSaveSnapshot Capture(
        CharacterRecord loaded, PlayerCharacter character, int fieldId, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(character);

        var attributes = character.Attributes;
        var scalars = loaded with
        {
            Level = character.Level,
            // SQLite and PostgreSQL store signed 64-bit integers: the unsigned experience keeps its 64-bit pattern (values
            // past 2^63, reachable at level 285, read back as negative numbers in SQL).
            Exp = unchecked((long)character.Experience),
            Money = character.Money,
            StatPoints = character.StatPoints,
            Tier = character.Tier,
            StarterGiftsReceived = character.StarterGiftsReceived,
            HelpPet = (short)character.HelpPet,
            HelpPetHidden = character.HelpPetHidden,
            Strength = attributes.Strength,
            Spirit = attributes.Spirit,
            Talent = attributes.Talent,
            Agility = attributes.Agility,
            Health = attributes.Health,
            Life = character.Life,
            Mana = character.Mana,
            Stamina = character.Stamina,
            FieldId = fieldId,
            PosX = position.X,
            PosZ = position.Z,
            LastPlayedAt = DateTime.UtcNow,
        };

        return new CharacterSaveSnapshot(scalars,
            CharacterStateCodec.CaptureInventory(character),
            CharacterStateCodec.CaptureEquipment(character),
            CharacterStateCodec.CaptureSkills(character),
            SavedPremium.Capture(character),
            QuestStateCodec.Capture(character.Quests));
    }
}
