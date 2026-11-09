using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class PuzzleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a sealed puzzle piece shows as the lucky bag, persists sealed and is revealed for 1000 gold", RevealAsync);
        yield return ("a puzzle is finished when one set lies in its board cells and the collector exchanges it for a reward", ExchangeAsync);
        yield return ("a sealed piece falls on the ground as the lucky bag and stays sealed when picked up", GroundAsync);
    }

    private static async Task RevealAsync()
    {
        var harness = await SpecialItemTests.Harness.CreateAsync(20, "PZ103");
        var character = harness.Session.Character!;
        character.Inventory[character.Inventory.Find(harness.Id("PZ103"))].Sealed = true;
        var owner = OwnerState.Capture(character, null, harness.NowSeconds);
        Assert.True(owner.Items.Single() is { CatalogKey: Puzzles.LuckyBagIcon, Name: Puzzles.LuckyBagName }, "the owner did not see a lucky bag");
        Assert.True(CharacterStateCodec.CaptureInventory(character).Items.Single().Sealed, "the seal was not saved");

        var poor = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("PZ103")));
        await harness.AdvanceAsync(poor);
        Assert.True((await poor).Outcome.Status == ItemUseStatus.NotEnoughGold, "revealed without gold");
        character.TryChangeMoney(1500);
        var reveal = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("PZ103")));
        await harness.AdvanceAsync(reveal);
        Assert.True((await reveal).Outcome.Ok && character.Money == 500 && harness.Item("PZ103") is { Sealed: false }, "the bag was not revealed for 1000 gold");
        Assert.True(harness.Repository.Record.Money == 500, "the gold was not stored");
        var again = harness.Use(new ItemUseCommand(Guid.NewGuid(), character.InventoryRevision, harness.Id("PZ103")));
        await harness.AdvanceAsync(again);
        Assert.True((await again).Outcome.Status == ItemUseStatus.NothingToChange && character.Money == 500, "a revealed piece charged again");
    }

    private static Task ExchangeAsync()
    {
        Assert.True(Puzzles.Draw(0).CatalogKey == "BI123" && Puzzles.Draw(2500).CatalogKey == "BI123" && Puzzles.Draw(2501).CatalogKey == "PM104"
            && Puzzles.Draw(10000).CatalogKey == "OS109" && Puzzles.Rewards.Sum(static reward => reward.Weight) == 10000, "reward table");
        Assert.True(Puzzles.Value(["PZ101", "PZ102", "PZ103", "PZ104", "PZ105", "PZ106", "PZ107", "PZ108"]) == 8
            && Puzzles.Value(["PZ201", "PZ201"]) == 0, "set values");

        var definitions = Puzzles.Pieces.Select(static key => new ItemDefinition(ItemCodes.Parse(key), key, ItemKind.Misc,
            GridWidth: key[4] is '3' or '6' ? 1 : 2, GridHeight: key[4] is '3' or '6' ? 2 : 1) { CatalogKey = key }).ToList();
        var reward = new ItemDefinition(ItemCodes.Parse("BI123"), "Firecracker", ItemKind.Misc) { CatalogKey = "BI123" };
        var potions = new ItemDefinition(ItemCodes.Parse("PM104"), "Mystic Mana Potion", ItemKind.Potion, MaxStack: 100) { CatalogKey = "PM104" };
        var catalog = new ItemCatalog([.. definitions, reward, potions]);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Puzzler");
        for (var number = 1; number <= 7; number++)
        {
            var (column, row) = Puzzles.Cell(number);
            Assert.True(character.Inventory.TryPlace(new ItemInstance(definitions[number - 1]), row * character.Inventory.Width + column), $"piece {number}");
        }

        Assert.True(PuzzleExchange.TryExchange(character, catalog, new Simulation.World.Ai.SeededSimRng(1), out _) == NpcServiceStatus.WrongPuzzle, "an unfinished puzzle was taken");
        var (lastColumn, lastRow) = Puzzles.Cell(8);
        character.Inventory.TryPlace(new ItemInstance(definitions[7]), lastRow * character.Inventory.Width + lastColumn);
        var views = OwnerState.Capture(character, null, 0).Items.Select(static item => (item.CatalogKey, item.Anchor));
        Assert.True(Puzzles.Complete(views, character.Inventory.Width), "the client does not see the finished board");
        Assert.True(PuzzleExchange.TryExchange(character, catalog, new Simulation.World.Ai.SeededSimRng(1), out var prize) == NpcServiceStatus.Ok && prize is not null
            && character.OwnedItems().All(item => !Puzzles.IsPiece(item.Definition.CatalogKey)) && character.OwnedItems().Count() == 1, "the exchange failed");
        Assert.True(NpcServiceAvailability.IsAvailable(new NpcService(NpcServiceKind.QuestEvent, Puzzles.CollectorEvent))
            && !NpcServiceAvailability.IsAvailable(new NpcService(NpcServiceKind.QuestEvent, 3)), "only the puzzle collector's event runs");
        return Task.CompletedTask;
    }

    private static Task GroundAsync()
    {
        var definition = new ItemDefinition(ItemCodes.Parse("PZ205"), "Knight 5", ItemKind.Misc, GridWidth: 2) { CatalogKey = "PZ205" };
        var loot = new FieldGroundLoot(new ItemCatalog([definition]));
        var drop = RolledDrop.Item(definition.Code) with { Sealed = true };
        loot.Place(new KillPayout(1, 7, 0, [drop]), Vector3.Zero, true, 0, new Simulation.World.Ai.SeededSimRng(3));
        loot.Step(0);
        var state = loot.Snapshot().Single();
        Assert.True(state.View.CatalogKey == Puzzles.LuckyBagIcon && state.View.Name == Puzzles.LuckyBagName, "the ground did not show the lucky bag");
        var owner = new WorldPlayer(7, state.View.Position, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Finder"), false, default);
        Assert.True(loot.TryReserve(state.View.Id, Guid.NewGuid(), owner, out _, out var picked) && picked is { Sealed: true } && picked.InstanceId == state.View.Id,
            "the picked-up piece lost its seal or its identity");
        return Task.CompletedTask;
    }
}
