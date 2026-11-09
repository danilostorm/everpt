using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudItemIcons : IDisposable
{
    private readonly HudIconCache _cache;

    public HudItemIcons(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _cache = new HudIconCache(device, assetRoot, log);
    }

    public Texture2D Find(string key) => _cache.Find(key);

    // Item bitmaps are authored at their cell size: drawn unscaled and centred, as in the inventory; a grey cell while the
    // icon is not loaded.
    public void Draw(HudPainter painter, string catalogKey, Rectangle bounds, Color? tint = null)
    {
        var texture = Find(catalogKey);
        if (texture is null)
        {
            painter.Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), Color.FromNonPremultiplied(60, 60, 60, 160));
            return;
        }

        var x = bounds.X + (bounds.Width - texture.Width) / 2;
        var y = bounds.Y + (bounds.Height - texture.Height) / 2;
        painter.Image(texture, new Rectangle(x, y, texture.Width, texture.Height), tint ?? Color.White);
    }
    public bool IsIdle => _cache.IsIdle;
    public bool Failed => _cache.Failed;
    public void Request(IReadOnlyList<OwnedItemView> items) => _cache.Request(items.Select(item => item.CatalogKey));
    public void Request(IEnumerable<string> catalogKeys) => _cache.Request(catalogKeys);
    public void Update() => _cache.Update();
    public void Dispose() => _cache.Dispose();
}
