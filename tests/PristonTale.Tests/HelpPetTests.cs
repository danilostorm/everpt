using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The beginner's helper pet: drawn below level 10, beside its owner while shown and below level 10, behind a premium
// helper pet, shown and hidden by the chat command, stored with the character and drawn for every viewer.
internal static class HelpPetTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a beginner draws one of the four dragons, which helps it below level 10 while shown, behind a premium helper pet", RulesAsync);
        yield return ("the beginner's pet is stored with the character and read back checked", StorageAsync);
        yield return ("a beginner entering the world meets its pet, which viewers see and the chat command hides and shows again", WorldAsync);
    }

    private static readonly CharacterClassRegistry Classes = new();

    private static Task RulesAsync()
    {
        var beginner = Classes.CreatePlayer(PlayerClass.Fighter, "Beginner", 1);
        beginner.MeetHelpPet(new ScriptedRng(3));
        Assert.True(beginner.HelpPet == 3 && HelpPets.Active(beginner) == PremiumEffectKind.HelpPetIo, "the drawn dragon does not help its beginner");
        beginner.MeetHelpPet(new ScriptedRng(1));
        Assert.True(beginner.HelpPet == 3, "a beginner drew a second pet");

        Assert.True(beginner.ToggleHelpPet() && beginner.HelpPetHidden && HelpPets.Active(beginner) is null, "a hidden pet still helps");
        Assert.True(beginner.ToggleHelpPet() && !beginner.HelpPetHidden && HelpPets.Active(beginner) is not null, "the pet did not come back");

        beginner.Premium.Start(PremiumEffectKind.HelpPetMute, 0, 600, 0);
        Assert.True(HelpPets.Active(beginner) == PremiumEffectKind.HelpPetMute, "the beginner's pet took the place of a premium helper pet");

        var veteran = Classes.CreatePlayer(PlayerClass.Fighter, "Veteran", HelpPets.LeaveLevel);
        veteran.MeetHelpPet(new ScriptedRng(2));
        Assert.True(veteran.HelpPet == 0 && HelpPets.Active(veteran) is null && !veteran.ToggleHelpPet(), "a character of level 10 met a beginner's pet");
        veteran.RestoreHelpPet(2, false);
        Assert.True(HelpPets.Active(veteran) is null, "the pet stayed past level 9");
        return Task.CompletedTask;
    }

    private static Task StorageAsync()
    {
        var record = CharacterRecordFactory.NewStarter(Classes, 1, "Beginner", 0, PlayerClass.Fighter, ChatFixture.Field);
        var beginner = Classes.CreatePlayer(PlayerClass.Fighter, "Beginner", 1);
        beginner.MeetHelpPet(new ScriptedRng(4));
        beginner.ToggleHelpPet();
        var stored = CharacterRecordFactory.FromLiveSession(record, beginner, ChatFixture.Field, default);
        var restored = Classes.CreatePlayer(PlayerClass.Fighter, "Beginner", 1);
        CharacterStateCodec.Restore(restored, stored, new ItemCatalog([]));
        Assert.True(record.HelpPet == 0 && stored is { HelpPet: 4, HelpPetHidden: true } && restored is { HelpPet: 4, HelpPetHidden: true },
            "the pet or its hidden flag did not come back");
        Assert.Throws<ArgumentException>(() => CharacterStateCodec.Restore(Classes.CreatePlayer(PlayerClass.Fighter, "Beginner", 1),
            stored with { HelpPet = HelpPets.Kinds + 1 }, new ItemCatalog([])), "a pet kind out of range was taken");
        return Task.CompletedTask;
    }

    private static async Task WorldAsync()
    {
        var fixture = new ChatFixture();
        var beginner = await fixture.JoinAsync("Beginner", 0, 0);
        var viewer = await fixture.JoinAsync("Viewer", 50, 0, level: HelpPets.LeaveLevel);
        var kind = beginner.Session.Character!.HelpPet;
        Assert.True(kind is >= 1 and <= HelpPets.Kinds && viewer.Session.Character!.HelpPet == 0, "a beginner entered without its pet, or a veteran met one");
        Assert.True(PetSeen(fixture, beginner) == kind, "the pet is not in the beginner's looks");
        await fixture.SayAsync(beginner, "/도우미");
        Assert.True(beginner.Session.Character.HelpPetHidden && PetSeen(fixture, beginner) == 0, "the command did not hide the pet");
        await fixture.SayAsync(beginner, "/helper");
        Assert.True(!beginner.Session.Character.HelpPetHidden && PetSeen(fixture, beginner) == kind, "the command did not show the pet again");
    }

    // The helper pet the field's view of the player shows.
    private static int PetSeen(ChatFixture fixture, ChatFixture.Player player)
    {
        fixture.Tick(1);
        return fixture.World.FieldFor(ChatFixture.Field)!.TryGet(player.Session.EntityId, out var seen) ? seen.Appearance.HelperPet : -1;
    }
}
