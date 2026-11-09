using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Trades;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Trades;

namespace PristonTale.Tests;

// The trade window in the HUD: the player's offer laid down each column of its 9x4 grid, the partner's offer, the lock and
// how the window closes.
internal static class TradeHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the trade window lays offered items down each column of its grid, takes them back, follows the bag and the server's offer", OfferAsync);
        yield return ("a changed partner offer greys the lock for two seconds, a locked side keeps its offer, and only a player's close cancels", LockAsync);
    }

    private static readonly OwnedItemView Axe = new(Guid.NewGuid(), ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe, 1, 0, -1, 2, 3, "WA101");
    private static readonly OwnedItemView Potions = new(Guid.NewGuid(), ItemCodes.Parse("PL101"), "Mini Life Potion", ItemKind.Potion, 10, 5, -1, 1, 1, "PL101");
    private static readonly TradeInfo Open = new(9, "Bob", TradeOffer.Empty, false, [], 0, false);

    private static HudTradeWindow Window()
    {
        var window = new HudTradeWindow();
        window.Show();
        window.Advance(1);
        window.Apply(Open, [Axe, Potions], 0);
        return window;
    }

    private static Task OfferAsync()
    {
        var window = Window();
        Assert.True(window.TryOffer(Axe, [Axe, Potions]) && window.TryOffer(Potions, [Axe, Potions]) && !window.TryOffer(Potions, [Axe, Potions]),
            "two items offer once each");
        Assert.True(window.Entries is [{ Cell: 0 }, { Cell: 3 * TradeLimits.Columns }] && window.Holds(Axe.InstanceId),
            "the potion goes down the first column, under the axe, on the four-row grid");
        var origin = window.Origin;
        var potionCell = new Rectangle(TradeWindowCell(origin, 3 * TradeLimits.Columns), new Point(22, 22));
        Assert.True(window.Click(potionCell.Center) == HudTradeAction.TakeBack && window.Selected == Potions.InstanceId, "a click takes an item back");
        window.TakeBack(window.Selected, [Axe, Potions]);
        Assert.True(window.Entries is [{ Cell: 0 }], "the item stayed offered");

        window.TryOffer(Potions, [Axe, Potions]);
        window.Apply(Open with { Own = new TradeOffer([new TradeEntry(Axe.InstanceId, 0)], 0) }, [Axe, Potions], 0);
        Assert.True(window.Entries is [{ Cell: 0 }], "the server's answer to a refused offer did not replace the window's guess");

        window.SetMoney(250);
        Assert.True(window.Offer() is { Money: 250, Entries.Count: 1 }, "the offer misses its gold");
        window.Follow([Potions]);
        Assert.True(window.Entries.Count == 0 && !window.Holds(Axe.InstanceId), "an item that left the bag stayed offered");
        Assert.True(window.Click(HudTradeWindow.MoneyButton(origin).Center) == HudTradeAction.Money
            && window.Click(HudTradeWindow.LockButton(origin).Center) == HudTradeAction.Lock
            && window.Click(HudTradeWindow.CloseButton(origin).Center) == HudTradeAction.Close, "the buttons answer their actions");
        return Task.CompletedTask;
    }

    private static Task LockAsync()
    {
        var window = Window();
        Assert.True(window.CanLock(0), "an untouched trade could not lock");
        window.Apply(Open with { PartnerItems = [Axe with { Anchor = 0 }] }, [Potions], 1000);
        Assert.True(!window.CanLock(1000 + TradeLimits.LockDelayMs - 1) && window.CanLock(1000 + TradeLimits.LockDelayMs),
            "the lock did not grey for two seconds after the partner's change");
        window.Apply(Open with { PartnerItems = [Axe with { Anchor = 0 }] }, [Potions], 5000);
        Assert.True(window.CanLock(5000), "an unchanged partner offer greyed the lock");
        window.Apply(Open with { PartnerItems = [Axe with { Anchor = 2 }] }, [Potions], 5000);
        Assert.True(!window.CanLock(5000), "a partner item moved on the grid did not grey the lock, as the server's delay does");

        window.Apply(Open with { OwnLocked = true, Own = new TradeOffer([new TradeEntry(Potions.InstanceId, 0)], 0) }, [Potions], 6000);
        Assert.True(!window.CanLock(6000) && window.Click(new Rectangle(TradeWindowCell(window.Origin, 0), new Point(22, 22)).Center) == HudTradeAction.Consumed,
            "a locked side took an item back or locked again");

        var cancels = 0;
        window.Hidden = () => cancels++;
        window.Close();
        Assert.True(cancels == 0 && !window.IsOpen, "the server's close cancelled the trade");
        window = Window();
        window.Hidden = () => cancels++;
        window.Hide();
        window.Hide();
        Assert.True(cancels == 1, "the player's close did not cancel once");
        return Task.CompletedTask;
    }

    private static Point TradeWindowCell(Point origin, int cell) =>
        new(HudTradeWindow.OwnGrid(origin).X + cell % TradeLimits.Columns * 22, HudTradeWindow.OwnGrid(origin).Y + cell / TradeLimits.Columns * 22);
}
