// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork (monogame/issues/006-structured-buffers.md).

using System;
using System.Runtime.InteropServices;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>Whether shaders only read a <see cref="StructuredBuffer"/> or also write it.</summary>
    public enum BufferShaderAccess
    {
        /// <summary>Bound to <c>StructuredBuffer&lt;T&gt;</c> and <c>ByteAddressBuffer</c> parameters (t registers).</summary>
        Read,

        /// <summary>
        /// Also bound to <c>RWStructuredBuffer&lt;T&gt;</c> and <c>RWByteAddressBuffer</c> parameters (u registers), and
        /// usable as the arguments of <see cref="GraphicsDevice.DispatchIndirect"/>.
        /// </summary>
        ReadWrite,
    }

    /// <summary>
    /// A GPU buffer that shaders read as <c>StructuredBuffer&lt;T&gt;</c> or <c>ByteAddressBuffer</c> and, when created
    /// with <see cref="BufferShaderAccess.ReadWrite"/>, write as <c>RWStructuredBuffer&lt;T&gt;</c> or
    /// <c>RWByteAddressBuffer</c>: bounding-volume hierarchies, light lists, compute results. Device-local memory, zero
    /// when created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SetData{T}(ReadOnlySpan{T}, int)"/> copies the data into a staging buffer at once and lets the GPU copy
    /// it into the buffer. Called on the thread that draws while a frame is being recorded, the copy is recorded in that
    /// frame, ordered with its draws and dispatches (earlier ones read the old contents, later ones the new); between
    /// frames it is submitted on its own. Called on any other thread it is the asynchronous upload of large data (tens of
    /// megabytes): that thread allocates the staging memory, copies into it, submits the copy and waits for the GPU, so
    /// the call returns with the data in the buffer and every frame submitted afterwards sees it, while the thread that
    /// draws never waits (monogame/issues/006-structured-buffers.md). A buffer written that way must not be in use by
    /// the frame being recorded at the same time.
    /// </para>
    /// <para>
    /// <see cref="GetData{T}(Span{T}, int)"/> waits for the GPU (and submits the frame being recorded if it used the
    /// buffer): for tests and tools, not for every frame.
    /// </para>
    /// </remarks>
    public sealed class StructuredBuffer : GraphicsResource
    {
        internal unsafe MGG_Buffer* Handle;

        /// <summary>
        /// Creates a buffer of <paramref name="elementCount"/> elements of <paramref name="elementStride"/> bytes (the
        /// stride of the HLSL struct with the DX layout the effect compiler uses; 4 for a raw <c>ByteAddressBuffer</c>).
        /// </summary>
        public unsafe StructuredBuffer(GraphicsDevice graphicsDevice, int elementStride, int elementCount, BufferShaderAccess shaderAccess)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice), FrameworkResources.ResourceCreationWhenDeviceIsNull);
            if (elementStride <= 0 || (elementStride % 4) != 0)
                throw new ArgumentOutOfRangeException(nameof(elementStride), "The stride of a storage buffer is a positive multiple of 4 bytes.");
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount), "A storage buffer holds at least one element.");
            long size = (long)elementStride * elementCount;
            if (size > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(elementCount), "A storage buffer holds less than 2 GB.");

            GraphicsDevice = graphicsDevice;
            ElementStride = elementStride;
            ElementCount = elementCount;
            ShaderAccess = shaderAccess;
            Handle = MGG.Buffer_Create(graphicsDevice.Handle, BufferType.Storage, false, (int)size);
        }

        /// <summary>The size of one element in bytes.</summary>
        public int ElementStride { get; }

        /// <summary>The number of elements.</summary>
        public int ElementCount { get; }

        /// <summary>The size of the buffer in bytes.</summary>
        public int SizeInBytes => ElementStride * ElementCount;

        /// <summary>Whether shaders may write the buffer.</summary>
        public BufferShaderAccess ShaderAccess { get; }

        /// <summary>
        /// Writes <paramref name="data"/> into the buffer from byte <paramref name="destinationByteOffset"/>; from any
        /// thread (see the remarks of <see cref="StructuredBuffer"/> for the ordering).
        /// </summary>
        public unsafe void SetData<T>(ReadOnlySpan<T> data, int destinationByteOffset = 0) where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(data);
            CheckRange(destinationByteOffset, bytes.Length);
            if (bytes.Length == 0)
                return;
            fixed (byte* p = bytes)
                MGG.Buffer_SetStorageData(GraphicsDevice.Handle, Handle, destinationByteOffset, p, bytes.Length);
        }

        /// <summary>Writes all of <paramref name="data"/> from the start of the buffer.</summary>
        public void SetData<T>(T[] data) where T : unmanaged
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            SetData<T>(data.AsSpan(), 0);
        }

        /// <summary>
        /// Reads bytes from <paramref name="sourceByteOffset"/> into <paramref name="destination"/>, waiting for the GPU.
        /// Called on the thread that draws.
        /// </summary>
        public unsafe void GetData<T>(Span<T> destination, int sourceByteOffset = 0) where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(destination);
            CheckRange(sourceByteOffset, bytes.Length);
            if (bytes.Length == 0)
                return;
            fixed (byte* p = bytes)
                MGG.Buffer_GetStorageData(GraphicsDevice.Handle, Handle, sourceByteOffset, p, bytes.Length);
        }

        private void CheckRange(int offset, int length)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(StructuredBuffer));
            if (offset < 0 || (offset % 4) != 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Storage buffer offsets are non-negative multiples of 4 bytes.");
            if ((long)offset + length > SizeInBytes)
                throw new ArgumentOutOfRangeException(nameof(length), $"{length} bytes at {offset} do not fit in a buffer of {SizeInBytes}.");
        }

        /// <summary>Releases the buffer once no frame in flight reads it.</summary>
        /// <param name="disposing">True when called from <see cref="GraphicsResource.Dispose()"/>.</param>
        protected override unsafe void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing && Handle != null)
            {
                MGG.Buffer_Destroy(GraphicsDevice.Handle, Handle);
                Handle = null;
            }

            base.Dispose(disposing);
        }
    }
}
