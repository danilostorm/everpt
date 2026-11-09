using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Effects;

// The client's effect resources: the VFX content (Effect/Vfx) and the texture loader it owns.
public sealed class EffectLibrary : IDisposable
{
    private readonly PristonTextureLoader _textures;

    private EffectLibrary(GraphicsDevice device, IClientLog log)
    {
        _textures = new PristonTextureLoader(device, log);
        _textures.Initialize();
    }

    // The effect definitions under Effect/Vfx, prepared on a worker from the moment the library loads.
    public VfxContent Vfx { get; private set; }

    public static EffectLibrary LoadBootstrap(GraphicsDevice device, ContentManager content, string assetRoot, IClientLog log)
    {
        var library = new EffectLibrary(device, log);
        try
        {
            library.Vfx = new VfxContent(device, content, library._textures, assetRoot, log);
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Vfx?.Dispose();
        _textures.Dispose();
    }
}
