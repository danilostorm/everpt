using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Assets;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Rendering;

public enum HudCursorKind
{
    Default,
    Attack,
    Talk,
    GetItem,
    GetItemHeld,
    Buy,
    Sell,
    Repair,
    // No pointer: the dragged item's icon takes its place.
    Hidden
}

// The original pointer: the yellow arrow, its gem red over a monster the player can attack, a speech balloon over an
// NPC and an open hand over a drop on the ground, closed while the left button is held; in a shop, the buy, sell or
// repair pointer of the shop's mode; none while an item is dragged. Each image becomes a system cursor, so it follows
// the mouse without waiting for a frame; the tip is the image's top-left pixel. A missing image leaves the system arrow.
public sealed class HudCursor : IDisposable
{
    private const string Folder = "image/Sinimage/Cursor/";

    private readonly MouseCursor[] _cursors;
    private readonly Texture2D[] _textures;
    private HudCursorKind? _shown;

    private HudCursor(MouseCursor[] cursors, Texture2D[] textures)
    {
        _cursors = cursors;
        _textures = textures;
    }

    // A summon (a monster with an owner) and other players keep the arrow, as the original only changed it for
    // monsters of the field and NPCs.
    public static HudCursorKind ForHovered(EntityKind kind, ulong ownerId) => kind switch
    {
        EntityKind.Monster when ownerId == 0 => HudCursorKind.Attack,
        EntityKind.Npc => HudCursorKind.Talk,
        _ => HudCursorKind.Default
    };

    // A drop under the pointer wins over the monster or NPC behind it.
    public static HudCursorKind ForHovered(bool dropHovered, bool leftHeld, EntityKind kind, ulong ownerId) =>
        dropHovered ? leftHeld ? HudCursorKind.GetItemHeld : HudCursorKind.GetItem : ForHovered(kind, ownerId);

    public static string ArtPath(HudCursorKind kind) => Folder + kind switch
    {
        HudCursorKind.Attack => "Attack_Cursor.tga",
        HudCursorKind.Talk => "Talk_Cursor.tga",
        HudCursorKind.GetItem => "GetItem_Cursor1.tga",
        HudCursorKind.GetItemHeld => "GetItem_Cursor2.tga",
        HudCursorKind.Buy => "BuyCursor.tga",
        HudCursorKind.Sell => "SellCursor.tga",
        HudCursorKind.Repair => "RepairCursor.tga",
        _ => "DefaultCursor.tga"
    };

    public static HudCursor Load(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        var kinds = Enum.GetValues<HudCursorKind>();
        var cursors = new MouseCursor[kinds.Length];
        var textures = new Texture2D[kinds.Length];
        var blank = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
        blank.SetData([Color.Transparent]);
        textures[(int)HudCursorKind.Hidden] = blank;
        cursors[(int)HudCursorKind.Hidden] = MouseCursor.FromTexture2D(blank, 0, 0);
        foreach (var kind in kinds)
        {
            if (kind == HudCursorKind.Hidden)
            {
                continue;
            }

            var path = AssetFiles.Combine(assetRoot, ArtPath(kind));
            try
            {
                if (!AssetFiles.Exists(path))
                {
                    log.Warn($"[Cursor] missing {ArtPath(kind)}; the system arrow stays.");
                    continue;
                }

                var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), Path.GetExtension(path));
                if (image.Width <= 0 || image.Height <= 0)
                {
                    log.Warn($"[Cursor] unreadable {ArtPath(kind)}; the system arrow stays.");
                    continue;
                }

                var texture = new Texture2D(device, image.Width, image.Height, false, SurfaceFormat.Color);
                texture.SetData(image.Pixels);
                textures[(int)kind] = texture;
                cursors[(int)kind] = MouseCursor.FromTexture2D(texture, 0, 0);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException)
            {
                log.Error($"[Cursor] {ArtPath(kind)} failed", exception);
            }
        }

        return new HudCursor(cursors, textures);
    }

    public void Show(HudCursorKind kind)
    {
        var cursor = _cursors[(int)kind] ?? _cursors[(int)HudCursorKind.Default];
        if (_shown == kind || cursor is null)
        {
            return;
        }

        Mouse.SetCursor(cursor);
        _shown = kind;
    }

    public void Dispose()
    {
        Mouse.SetCursor(MouseCursor.Arrow);
        foreach (var cursor in _cursors)
        {
            cursor?.Dispose();
        }

        foreach (var texture in _textures)
        {
            texture?.Dispose();
        }
    }
}
