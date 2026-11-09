using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Gameplay.UI.PersonalShops;

// The personal shop requests the HUD sends through the world connection (GameScene).
public interface IPersonalShopClient
{
    Task<PersonalShopReply> OpenPersonalShopAsync(string sign, IReadOnlyList<PersonalShopEntry> entries);

    Task<PersonalShopReply> ClosePersonalShopAsync();

    Task<PersonalShopReply> ViewPersonalShopAsync(ulong sellerId);

    Task<PersonalShopReply> BuyFromPersonalShopAsync(PersonalShopPurchase purchase);
}
