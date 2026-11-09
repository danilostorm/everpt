// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using MonoGame.Interop;
namespace Microsoft.Xna.Framework.Graphics;

public abstract partial class Texture
{
    internal unsafe MGG_Texture* Handle;
    internal bool Owned = true;

    private unsafe void PlatformGraphicsDeviceResetting()
    {
        if (Handle != null && Owned)
        {
            MGG.Texture_Destroy(GraphicsDevice.Handle, Handle);
            Handle = null;
        }
    }

    /// <summary>Releases the native texture.</summary>
    /// <param name="disposing">True when called from <see cref="GraphicsResource.Dispose()"/>.</param>
    protected unsafe override void Dispose(bool disposing)
    {
        PlatformGraphicsDeviceResetting();

        base.Dispose(disposing);
    }
}
