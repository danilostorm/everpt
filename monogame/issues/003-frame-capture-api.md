# 003. Asynchronous frame capture as public API

Part: framework (the runtime side is change 17, `../upstream/16-async-frame-capture.md`).

## Need

An application's glitch check reads the presented frames without synchronising, so the races between the CPU and
frames still on the GPU show. It reached the runtime's `MGG_FrameCapture_*` through a helper of its own, which read the
internal `GraphicsDevice.Handle` by reflection and bound the exports with `NativeLibrary`. Reading the presented frames
is a generic GPU facility (latency measurements need it too), so it belongs to the framework.

## Cause

The framework was a NuGet package (issue 001).

## Change

`GraphicsDevice` (`Graphics/GraphicsDevice.FrameCapture.cs`):
- `bool BeginFrameCapture(int downscale, int maxQueued)`: every `Present` copies the image it presents (downscaled by a
  blit) into a host buffer of its frame slot, inside the frame's own command buffer; false when the swapchain cannot be
  read;
- `bool TryPopFrame(Span<byte> rgba, out PresentedFrame frame)`: the oldest frame not read yet (RGBA8); false when none is
  ready, or when the span is too small (nothing is removed, `frame` gives the size);
- `ulong DroppedCapturedFrames`, `bool IsCapturingFrames`, `void EndFrameCapture()`.
- `PresentedFrame`: `Width`, `Height`, `Number` (the device's frame counter), `Drawn` (false when nothing was drawn into
  the presented image), `ByteCount`.

The application's reflection-based helper is deleted and its glitch check calls the device; what is the application's
own (the glitch criteria, the dumps) stays in the application. No reflection is left.

## Test

The glitch check of the application (its three regression scenes) passes as before the change.
