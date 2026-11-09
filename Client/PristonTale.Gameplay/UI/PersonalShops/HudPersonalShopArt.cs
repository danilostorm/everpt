namespace PristonTale.Gameplay.UI.PersonalShops;

// Asset-relative paths of the original personal shop images: the window's title and picture over the store window, the
// start and stop buttons (lit, idle, label), the button that reopens a selling shop, and the inventory button.
public static class HudPersonalShopArt
{
    private const string Shop = "image/Sinimage/indishop/";

    public const string Title = Shop + "indishop_title.bmp";
    public const string Picture = Shop + "indishop_image.bmp";
    public const string Start = Shop + "indishop_sale.bmp";
    public const string StartIdle = Shop + "indishop_sale_.bmp";
    public const string StartInfo = Shop + "indishop_saleT.bmp";
    public const string Stop = Shop + "indishop_nosale.bmp";
    public const string StopIdle = Shop + "indishop_nosale_.bmp";
    public const string StopInfo = Shop + "indishop_nosaleT.bmp";
    public const string Reopen = "image/Sinimage/shopall/MyShopButton.tga";
    public const string InventoryButton = "image/Sinimage/Inven/inven-Sale.bmp";
    public const string InventoryInfo = "image/Sinimage/Inven/InfoBox/inven-SaleT.bmp";

    public static readonly string[] All = [Title, Picture, Start, StartIdle, StartInfo, Stop, StopIdle, StopInfo, Reopen, InventoryButton, InventoryInfo];
}
