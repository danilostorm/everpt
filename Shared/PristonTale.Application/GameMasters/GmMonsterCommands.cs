using PristonTale.Application.Chat;
using PristonTale.Simulation.Social;
using static PristonTale.Application.GameMasters.GmInformationCommands;
using PristonTale.Contracts.Social;

namespace PristonTale.Application.GameMasters;

// Commands on monsters: calling them, pets, the field's event monster, the field's spawn report, absorption, and making a
// monster or NPC speak.
internal static class GmMonsterCommands
{
    // At most this many extra monsters per call, besides the first.
    public const int MaxExtraMonsters = 20;

    // /monster name [count]: the monster on the spot and up to 20 more around; only the highest level's monsters drop items.
    public static Task Monster(GmCommandContext context) => Spawn(context, pet: false);

    // /mymonster name: a monster that takes the game master's side for four minutes.
    public static Task Pet(GmCommandContext context) => Spawn(context, pet: true);

    // /event_monster name minutes permille: for that many minutes each regular spawn of the field becomes one of this
    // monster with the given chance in 1000. Without a name it reports the running event.
    public static async Task EventMonster(GmCommandContext context)
    {
        var name = context.Phrase();
        var world = context.Commands.World;
        if (name.Length == 0)
        {
            if (await world.EventMonsterAsync(context.Issuer.EntityId, null, 0, 0) is { } running)
            {
                context.Reply(Format("> Event Monster ( {0} ) Left {1} Sec. ({2}/1000)", running.Name, running.SecondsLeft, running.Permille));
            }

            return;
        }

        var minutes = context.Word();
        var permille = context.Word();
        if (!ChatCommandText.StartsWithDigit(minutes) || !ChatCommandText.StartsWithDigit(permille))
        {
            return;
        }

        var minuteCount = ChatCommandText.ToNumber(minutes);
        if (await world.EventMonsterAsync(context.Issuer.EntityId, name, minuteCount, ChatCommandText.ToNumber(permille)) is { } started)
        {
            context.Reply(Format("> Set Event Monster ( {0} ) ({1})Min. ({2}/1000)", started.Name, minuteCount, started.Permille));
        }
    }

    public static async Task FieldMonsters(GmCommandContext context)
    {
        if (await context.Commands.World.FieldMonstersAsync(context.Issuer.EntityId) is { } report
            && (report.ActiveSpawnPoints != 0 || report.SpawnPointMonsters != 0 || report.Monsters != 0 || report.LiveMonsters != 0))
        {
            context.Reply(Format("> ResponePoint( {0} ) ResponeMonster( {1} ) Monster( {2} ) MonsterCount( {3} ) LimitTime( {4} )",
                report.ActiveSpawnPoints, report.SpawnPointMonsters, report.Monsters, report.LiveMonsters, report.IntervalSeconds));
        }
    }

    // /set_absorb entityId value: a monster's absorption, 1 to 99.
    public static async Task SetAbsorption(GmCommandContext context)
    {
        var entityId = context.Word();
        var value = ChatCommandText.ToNumber(context.Rest);
        if (entityId.Length == 0 || value is <= 0 or >= 100)
        {
            return;
        }

        if (await context.Commands.World.SetAbsorptionAsync(context.Issuer.EntityId, ChatCommandText.ToEntityId(entityId), value) is { } changed)
        {
            context.Reply(Format("> set_absorb ({0}) {1}->{2} ", changed.Name, changed.Before, changed.After));
        }
    }

    // /talk entityId text: the monster, NPC or player with that entityId says the text to the players near the game master, in
    // a bubble over it.
    public static async Task Talk(GmCommandContext context)
    {
        var idWord = context.Word();
        if (idWord.Length == 0)
        {
            return;
        }

        var entityId = ChatCommandText.ToEntityId(idWord);
        var world = context.Commands.World;
        if (await world.EntityNameAsync(context.Issuer.EntityId, entityId) is not { } name)
        {
            return;
        }

        var line = new ChatLine(ChatKind.Server, entityId, ChatRules.ClipToCapacity(name + ": " + context.Line[context.Index..] + " "));
        foreach (var id in await world.PlayersNearAsync(context.Issuer.EntityId, ChatRules.AreaRangeSquared, includeSpeaker: true))
        {
            if (context.Commands.Directory.TryGet(id, out var hearer))
            {
                hearer.Link.Deliver(line);
            }
        }
    }

    private static async Task Spawn(GmCommandContext context, bool pet)
    {
        var name = context.Phrase();
        if (name.Length == 0)
        {
            return;
        }

        var count = context.Word();
        var extra = ChatCommandText.StartsWithDigit(count) ? ChatCommandText.ToNumber(count) : 0;
        if (extra > MaxExtraMonsters)
        {
            extra = 0;
        }

        var drops = context.Issuer.GmLevel >= GameMasterLevel.Administrator;
        var result = await context.Commands.World.SpawnMonstersAsync(context.Issuer.EntityId, name, extra, pet, drops);
        if (result.Known)
        {
            context.Note(Format("{0} x{1}", name, result.Spawned));
        }
    }
}
