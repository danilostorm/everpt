using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI.Trades;

// Asset-relative paths of the original trade window (on the store window's frame) and its request box.
public static class HudTradeArt
{
    private const string Shop = "image/Sinimage/shopall/";

    // The right-hand panel with the partner's and the player's gold and weight plates.
    public const string Title = Shop + "Trade.bmp";
    // One cell of the strip between the partner's grid and the player's.
    public const string Strip = Shop + "Trade_0.bmp";
    public const string Locked = Shop + "Trade_Check.bmp";
    public const string LockInfo = Shop + "Trade_Check_Info.bmp";
    public const string LockWaiting = Shop + "Trade_Check_Glay.bmp";
    public const string Close = Shop + "Trade_Cancel.bmp";
    public const string CloseInfo = Shop + "Trade_CancelInfo.bmp";

    // The request box: who asks at (38, 17); its art carries the line and the idle buttons.
    public static readonly HudRequestBoxArt Request = new(Shop + "TradeButton/TradeButtonMain.tga", null, Shop + "TradeButton/ButtonOk.bmp",
        new Rectangle(140, 11, 23, 23), null, Shop + "TradeButton/ButtonCancel.tga", new Rectangle(144, 37, 16, 16), new Point(38, 17), Point.Zero,
        BlinkOver: false);

    public static readonly string[] All = [Title, Strip, Locked, LockInfo, LockWaiting, Close, CloseInfo, .. Request.All];
}
