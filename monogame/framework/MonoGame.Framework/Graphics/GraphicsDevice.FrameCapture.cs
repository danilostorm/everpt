// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork (monogame/issues/003-frame-capture-api.md).

using System;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>A presented frame handed over by the asynchronous frame capture (<see cref="GraphicsDevice.TryPopFrame"/>).</summary>
    public readonly struct PresentedFrame
    {
        internal PresentedFrame(int width, int height, ulong number, bool drawn)
        {
            Width = width;
            Height = height;
            Number = number;
            Drawn = drawn;
        }

        /// <summary>Width of the captured image in pixels (the back buffer's divided by the downscale).</summary>
        public int Width { get; }

        /// <summary>Height of the captured image in pixels.</summary>
        public int Height { get; }

        /// <summary>The device's frame counter when the frame was presented.</summary>
        public ulong Number { get; }

        /// <summary>
        /// False when nothing was drawn into the image presented that frame (it is then black or stale, a defect in
        /// itself; the capture holds zeros).
        /// </summary>
        public bool Drawn { get; }

        /// <summary>The bytes of the image, RGBA8, rows top to bottom.</summary>
        public int ByteCount => Width * Height * 4;
    }

    public partial class GraphicsDevice
    {
        private bool _capturingFrames;

        /// <summary>
        /// Starts the asynchronous capture of presented frames: every <see cref="Present"/> copies the image it presents,
        /// downscaled by <paramref name="downscale"/>, into a host buffer of its frame slot, inside the frame's own
        /// command buffer. The copy is handed over when the runtime has waited for that frame anyway, so capturing adds
        /// no synchronisation: frames in flight overlap as in normal play, and races between the CPU and frames still on
        /// the GPU stay visible (unlike <see cref="GetBackBufferData{T}(T[])"/> or a render target's <c>GetData</c>,
        /// which submit and wait for the frame being recorded).
        /// </summary>
        /// <param name="downscale">Integer reduction of each side (1 = full size; above 1 a linear blit).</param>
        /// <param name="maxQueued">Frames kept unread at most; later ones are dropped and counted (<see cref="DroppedCapturedFrames"/>).</param>
        /// <returns>False when the presented images cannot be read (no swapchain, or its format cannot be blitted).</returns>
        public unsafe bool BeginFrameCapture(int downscale, int maxQueued)
        {
            if (downscale < 1)
                throw new ArgumentOutOfRangeException(nameof(downscale), "The downscale is a factor of at least 1.");
            if (maxQueued < 1)
                throw new ArgumentOutOfRangeException(nameof(maxQueued), "At least one captured frame must be kept.");
            _capturingFrames = MGG.FrameCapture_Start(Handle, downscale, maxQueued) != 0;
            return _capturingFrames;
        }

        /// <summary>Whether <see cref="BeginFrameCapture"/> is capturing the presented frames.</summary>
        public bool IsCapturingFrames => _capturingFrames;

        /// <summary>Frames the capture dropped because <c>maxQueued</c> of them were waiting to be read.</summary>
        public unsafe ulong DroppedCapturedFrames => MGG.FrameCapture_Dropped(Handle);

        /// <summary>
        /// Moves the oldest captured frame not read yet into <paramref name="rgba"/> (RGBA8, rows top to bottom).
        /// </summary>
        /// <returns>
        /// False when no frame is ready, or when <paramref name="rgba"/> is smaller than the frame: then nothing is
        /// removed, and <paramref name="frame"/> gives the size needed (<see cref="PresentedFrame.ByteCount"/>).
        /// </returns>
        public unsafe bool TryPopFrame(Span<byte> rgba, out PresentedFrame frame)
        {
            int width = 0, height = 0;
            ulong number = 0;
            byte drawn = 0;
            byte ok;
            fixed (byte* data = rgba)
                ok = MGG.FrameCapture_Pop(Handle, data, rgba.Length, &width, &height, &number, &drawn);
            frame = new PresentedFrame(width, height, number, drawn != 0);
            return ok != 0;
        }

        /// <summary>Stops capturing and frees the capture's buffers (waits for the GPU once).</summary>
        public unsafe void EndFrameCapture()
        {
            if (!_capturingFrames)
                return;
            _capturingFrames = false;
            MGG.FrameCapture_Stop(Handle);
        }
    }
}
