// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork (monogame/issues/004-gpu-timestamps-api.md).

using System;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>
    /// GPU timestamps written into the device's own frame command buffer (Vulkan timestamp queries,
    /// <c>vkCmdWriteTimestamp</c> at the bottom of the pipe), for timing sections of a frame on the GPU.
    /// </summary>
    /// <remarks>
    /// A frame resets the range it will use (<see cref="Reset"/>, outside any render pass), writes timestamps between
    /// draws (<see cref="Write"/>) and reads them back some frames later, without waiting (<see cref="TryGetNanoseconds"/>),
    /// once the frame that wrote them has completed on the GPU. Measured against frame intervals the timestamps agree
    /// to 0.1 %.
    /// </remarks>
    public sealed class GpuTimestampPool : GraphicsResource
    {
        private nint _pool;
        private readonly double _nanosecondsPerTick;

        /// <summary>Creates a pool of <paramref name="count"/> timestamps on <paramref name="graphicsDevice"/>.</summary>
        /// <exception cref="NotSupportedException">The device cannot create timestamp queries.</exception>
        public unsafe GpuTimestampPool(GraphicsDevice graphicsDevice, int count)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "A timestamp pool holds at least one timestamp.");

            GraphicsDevice = graphicsDevice;
            Count = count;
            _pool = MGG.TimestampPool_Create(graphicsDevice.Handle, (uint)count);
            if (_pool == 0)
                throw new NotSupportedException("The graphics device could not create a timestamp query pool.");
            _nanosecondsPerTick = MGG.TimestampPool_GetPeriod(graphicsDevice.Handle);
        }

        /// <summary>The number of timestamps in the pool.</summary>
        public int Count { get; }

        /// <summary>The length of one timestamp tick in nanoseconds (the device's <c>timestampPeriod</c>).</summary>
        public double NanosecondsPerTick => _nanosecondsPerTick;

        /// <summary>
        /// Resets timestamps [<paramref name="first"/>, <paramref name="first"/> + <paramref name="count"/>) in the
        /// frame's command buffer, before they are written again.
        /// </summary>
        /// <returns>
        /// False when the device is inside a render pass, where Vulkan forbids the reset (ending the pass would change its
        /// load and store), or not recording a frame: the caller leaves that frame untimed.
        /// </returns>
        public unsafe bool Reset(int first, int count)
        {
            CheckRange(first, count);
            return MGG.TimestampPool_Reset(GraphicsDevice.Handle, _pool, (uint)first, (uint)count) != 0;
        }

        /// <summary>Records timestamp <paramref name="index"/> at this point of the frame's command buffer.</summary>
        public unsafe void Write(int index)
        {
            CheckRange(index, 1);
            MGG.TimestampPool_Write(GraphicsDevice.Handle, _pool, (uint)index);
        }

        /// <summary>
        /// Timestamp <paramref name="index"/> in nanoseconds, without waiting: false while the frame that wrote it has
        /// not completed on the GPU (or it was never written since its reset).
        /// </summary>
        public unsafe bool TryGetNanoseconds(int index, out ulong nanoseconds)
        {
            CheckRange(index, 1);
            ulong ticks;
            bool ok = MGG.TimestampPool_Read(GraphicsDevice.Handle, _pool, (uint)index, 1, &ticks) != 0;
            nanoseconds = ok ? (ulong)(ticks * _nanosecondsPerTick) : 0;
            return ok;
        }

        private void CheckRange(int first, int count)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(GpuTimestampPool));
            if (first < 0 || count < 0 || first + count > Count)
                throw new ArgumentOutOfRangeException(nameof(first), $"Timestamps [{first}, {first + count}) are outside the pool of {Count}.");
        }

        /// <summary>Destroys the query pool (waits for the GPU once, as the runtime must before destroying it).</summary>
        /// <param name="disposing">True when called from <see cref="GraphicsResource.Dispose()"/>.</param>
        protected override unsafe void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing && _pool != 0)
            {
                MGG.TimestampPool_Destroy(GraphicsDevice.Handle, _pool);
                _pool = 0;
            }

            base.Dispose(disposing);
        }
    }
}
