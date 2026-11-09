// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

namespace Microsoft.Xna.Framework.Graphics
{
	/// <summary>
	/// Defines types for effect parameters and shader constants.
	/// </summary>
	public enum EffectParameterType
	{
        /// <summary>
        /// Pointer to void type.
        /// </summary>
		Void,
        /// <summary>
        /// Boolean type. Any non-zero will be <c>true</c>; <c>false</c> otherwise.
        /// </summary>
		Bool,
        /// <summary>
        /// 32-bit integer type.
        /// </summary>
		Int32,
        /// <summary>
        /// Float type.
        /// </summary>
		Single,
        /// <summary>
        /// String type.
        /// </summary>
		String,
        /// <summary>
        /// Any texture type.
        /// </summary>
		Texture,
        /// <summary>
        /// 1D-texture type.
        /// </summary>
        Texture1D,  
        /// <summary>
        /// 2D-texture type.
        /// </summary>
        Texture2D,
        /// <summary>
        /// 3D-texture type.
        /// </summary>
        Texture3D,
        /// <summary>
        /// Cubic texture type.
        /// </summary>
		TextureCube,
        // Etervale fork: the storage resources of SM 6 shaders (monogame/issues/005, 006). Set with
        // EffectParameter.SetValue(StructuredBuffer) or SetValue(Texture2D, int).
        /// <summary>
        /// A read-only structured buffer (<c>StructuredBuffer&lt;T&gt;</c>), bound to a <see cref="Graphics.StructuredBuffer"/>.
        /// </summary>
        StructuredBuffer,
        /// <summary>
        /// A structured buffer shaders write (<c>RWStructuredBuffer&lt;T&gt;</c>), bound to a <see cref="Graphics.StructuredBuffer"/>
        /// created with <see cref="BufferShaderAccess.ReadWrite"/>.
        /// </summary>
        RWStructuredBuffer,
        /// <summary>
        /// A read-only raw buffer (<c>ByteAddressBuffer</c>), bound to a <see cref="Graphics.StructuredBuffer"/>.
        /// </summary>
        ByteAddressBuffer,
        /// <summary>
        /// A raw buffer shaders write (<c>RWByteAddressBuffer</c>), bound to a <see cref="Graphics.StructuredBuffer"/>
        /// created with <see cref="BufferShaderAccess.ReadWrite"/>.
        /// </summary>
        RWByteAddressBuffer,
        /// <summary>
        /// A 2D image shaders write (<c>RWTexture2D&lt;T&gt;</c>), bound to one mip of a <see cref="Graphics.Texture2D"/>
        /// created with <see cref="TextureShaderAccess.ReadWrite"/>.
        /// </summary>
        RWTexture2D,
        /// <summary>
        /// A ray-tracing acceleration structure (<c>RaytracingAccelerationStructure</c>, SM 6.5 profiles only); the
        /// runtime binds none yet (monogame/issues/008).
        /// </summary>
        AccelerationStructure
	}
}