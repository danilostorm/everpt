using PristonTale.Gameplay.UI.PersonalShops;

namespace PristonTale.Gameplay.UI;

// The personal shop: the player's own from the inventory's button, another player's when the scene reaches its seller.
public sealed partial class GameplayHud
{
    private readonly HudPersonalShopController _personalShop;

    // The signs of the players selling from a personal shop (the local player's included), from the scene; read only.
    public Dictionary<ulong, string> ShopSigns { get; set; } = PristonTale.Gameplay.World.Remote.RemoteEntityManager.NoShopSigns;

    // The player stands in a town, where a personal shop opens.
    public bool InTown { get; set; }

    // Sends the personal shop requests; set by the screen.
    public IPersonalShopClient PersonalShops
    {
        set => _personalShop.Client = value;
    }

    public void ViewPersonalShop(ulong sellerId)
    {
        CloseServices();
        _personalShop.View(sellerId);
    }

    // As in the original, the shop closes the other service windows and the special item box.
    private void OpenOwnShop()
    {
        if (_personalShop.Window.IsOpen)
        {
            return;
        }

        CloseServices();
        _inventory.CloseQuestBox();
        _personalShop.OpenOwn(InTown);
    }
}
