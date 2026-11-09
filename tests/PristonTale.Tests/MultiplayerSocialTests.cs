using System.Numerics;
using PristonTale.Protocol.Packets;
using static PristonTale.Tests.MultiplayerMovementTests;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Chat and game-master visibility between real clients: a spoken line reaches the players in speaking range, a whisper
// only its two ends, and a hidden game master leaves every other player's presence.
internal static class MultiplayerSocialTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("multiplayer: a spoken line reaches the players within speaking range and a whisper only its two ends", ChatAsync);
        yield return ("multiplayer: a hidden game master leaves other players' presence and comes back when shown", HideAsync);
    }

    private static async Task ChatAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        await using var a = await Enter(server, "Talker", camp);
        await using var near = await Enter(server, "Listener", camp + new Vector3(300f, 0f, 0f));
        // In view (under 1086 units) but beyond the spoken range of 543 units.
        await using var far = await Enter(server, "Distant", camp + new Vector3(-700f, 0f, 0f));
        await near.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the talker");
        await far.WaitUntilAsync(world => world.Entities.ContainsKey(a.EntityId), "the talker from afar");

        await a.SayAsync("hello there");
        var heard = await near.WaitAsync(world => world.Chat.FirstOrDefault(line => line.Text.Contains("hello there", StringComparison.Ordinal)),
            "the spoken line");
        Assert.True(heard.SpeakerId == a.EntityId && (heard.Kind & ChatKind.Player) != 0, $"the line came as {heard}");
        await Task.Delay(400);
        Assert.True(!far.World.Read(world => world.Chat.Any(line => line.Text.Contains("hello there", StringComparison.Ordinal))),
            "a player beyond speaking range heard the line");

        await a.SayAsync("/:Distant meet me at the gate");
        var whisper = await far.WaitAsync(world => world.Chat.FirstOrDefault(line => line.Text.Contains("meet me at the gate", StringComparison.Ordinal)),
            "the whisper");
        Assert.True(whisper.Kind == ChatKind.Whisper && whisper.Text.StartsWith("From> Talker", StringComparison.Ordinal), $"the whisper came as {whisper}");
        await a.WaitUntilAsync(world => world.Chat.Any(line => line.Text.StartsWith("To> Distant", StringComparison.Ordinal)), "the whisper's echo");
        await Task.Delay(400);
        Assert.True(!near.World.Read(world => world.Chat.Any(line => line.Text.Contains("meet me at the gate", StringComparison.Ordinal))),
            "a bystander read the whisper");
    }

    private static async Task HideAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith());
        var camp = MultiplayerServer.Camp;
        var account = await server.CreatePlayerAsync(new PlayerSetup("Warden", PlayerClass.Fighter, 10, MultiplayerServer.HuntingField, camp));
        await server.SetGameMasterAsync(account, 1);
        await using var gm = await MultiplayerClient.EnterAsync(server, account, "Warden");
        await using var b = await Enter(server, "Watcher", camp + new Vector3(150f, 0f, 0f));
        await using var c = await Enter(server, "Onlooker", camp + new Vector3(-150f, 0f, 0f));
        await b.WaitUntilAsync(world => world.Entities.ContainsKey(gm.EntityId), "the game master");

        await gm.SayAsync("/hide");
        foreach (var viewer in new[] { b, c })
        {
            await viewer.WaitUntilAsync(world => !world.Entities.ContainsKey(gm.EntityId) && (world.Removed.Contains(gm.EntityId) || !world.EverSeen.Contains(gm.EntityId)),
                "the game master hidden");
        }

        // Hidden, the game master still sees the others and moves without anyone noticing.
        await gm.WaitUntilAsync(world => world.Entities.ContainsKey(b.EntityId) && world.Entities.ContainsKey(c.EntityId), "the players while hidden");
        var removedAt = b.World.Read(world => world.Removed.Count);
        await gm.WalkAsync(camp, camp + new Vector3(0f, 0f, 60f));
        Assert.True(!b.World.Read(world => world.Entities.ContainsKey(gm.EntityId)), "a hidden game master reappeared by moving");

        await gm.SayAsync("/show");
        foreach (var viewer in new[] { b, c })
        {
            await viewer.WaitUntilAsync(world => world.Entities.ContainsKey(gm.EntityId), "the game master shown again");
        }

        Assert.True(removedAt >= 1, "the watcher never removed the hidden game master");
    }
}
