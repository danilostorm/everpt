using PristonTale.Application.Chat;
using static PristonTale.Application.GameMasters.GmInformationCommands;

namespace PristonTale.Application.GameMasters;

// Commands on players: disconnecting, killing, finding, moving and hiding them, and lending a level.
internal static class GmPlayerCommands
{
    public static Task Kick(GmCommandContext context)
    {
        var target = Target(context);
        if (target is not null)
        {
            context.Reply(Format("Congratulations. You have been kicked {0}", target.Name));
            target.Link.Disconnect("kicked by a game master");
        }

        return Task.CompletedTask;
    }

    public static async Task Kill(GmCommandContext context)
    {
        var target = Target(context);
        if (target is not null && await context.Commands.World.KillAsync(context.Issuer.EntityId, target.EntityId))
        {
            context.Reply(Format("> Killed ( {0} )", target.Name));
        }
    }

    // The game master goes to the player.
    public static async Task Near(GmCommandContext context)
    {
        var target = Target(context);
        if (target is null || await context.Commands.World.LocateAsync(context.Issuer.EntityId, target.EntityId) is not { } place)
        {
            return;
        }

        await Move(context, context.Issuer, place);
    }

    // The player comes to the game master.
    public static async Task Call(GmCommandContext context)
    {
        var target = Target(context);
        if (target is null || await context.Commands.World.LocateAsync(context.Issuer.EntityId, context.Issuer.EntityId) is not { } place)
        {
            return;
        }

        await Move(context, target, place);
    }

    // The game master goes to a field by its number: the field's start point (a random one in the castle).
    public static async Task Field(GmCommandContext context)
    {
        var word = context.Word();
        if (word.Length == 0 || context.Commands.World.FieldArrival(ChatCommandText.ToNumber(word), Random.Shared) is not { } place)
        {
            return;
        }

        await Move(context, context.Issuer, place);
    }

    // The game master moves to a position of the current field: x, y (the height) and z.
    public static async Task Location(GmCommandContext context)
    {
        var x = context.Word();
        var y = context.Word();
        var z = context.Word();
        if (z.Length == 0 || await context.Commands.World.LocateAsync(context.Issuer.EntityId, context.Issuer.EntityId) is not { } here)
        {
            return;
        }

        var position = new System.Numerics.Vector3(ChatCommandText.ToNumber(x), ChatCommandText.ToNumber(y), ChatCommandText.ToNumber(z));
        await Move(context, context.Issuer, new GmPlacement(here.FieldId, position));
    }

    public static async Task Where(GmCommandContext context)
    {
        var target = Target(context);
        if (target is not null && await context.Commands.World.LocateAsync(context.Issuer.EntityId, target.EntityId) is { } place)
        {
            context.Reply(Format("Position of {0} ( {1} ) x={2} / z={3} ", target.Name, context.Commands.World.FieldName(place.FieldId),
                (int)place.Position.X, (int)place.Position.Z));
        }
    }

    public static async Task Hide(GmCommandContext context)
    {
        if (await context.Commands.World.SetHiddenAsync(context.Issuer.EntityId, true))
        {
            context.Reply("Hide OK!");
        }
    }

    public static async Task Show(GmCommandContext context)
    {
        if (await context.Commands.World.SetHiddenAsync(context.Issuer.EntityId, false))
        {
            context.Reply("Show OK!");
        }
    }

    // Lends the game master's own level to another player until that player leaves the world.
    public static Task Grant(GmCommandContext context)
    {
        var target = Target(context);
        if (target is null)
        {
            return Task.CompletedTask;
        }

        target.GmLevel = context.Issuer.GmLevel;
        var text = Format("> {0} has been given level ({1})", target.Name, target.GmLevel);
        context.Reply(text);
        if (!ReferenceEquals(target, context.Issuer))
        {
            target.Link.Deliver(ChatLine.Reply(text));
        }

        return Task.CompletedTask;
    }

    private static ChatMember? Target(GmCommandContext context)
    {
        var name = context.Word();
        return name.Length == 0 ? null : context.Commands.Directory.FindByName(name);
    }

    private static async Task Move(GmCommandContext context, ChatMember mover, GmPlacement destination)
    {
        if (await context.Commands.World.RelocateAsync(context.Issuer.EntityId, mover.EntityId, destination) is { } arrival)
        {
            mover.Link.Warp(arrival.FieldId, arrival.Position);
            context.Note(Format("{0} to field {1} ({2}, {3})", mover.Name, arrival.FieldId, (int)arrival.Position.X, (int)arrival.Position.Z));
        }
    }
}
