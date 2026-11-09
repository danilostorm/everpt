// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork (monogame/issues/005-compute-and-uav.md).

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>Whether shaders only sample a <see cref="Texture2D"/> or also write it.</summary>
    public enum TextureShaderAccess
    {
        /// <summary>Sampled and read by shaders (every texture).</summary>
        Read,

        /// <summary>
        /// Also written by shaders as <c>RWTexture2D&lt;T&gt;</c>, one mip at a time (a storage image in the device's
        /// terms). Between dispatches it is sampled like any other texture.
        /// </summary>
        ReadWrite,
    }
}
