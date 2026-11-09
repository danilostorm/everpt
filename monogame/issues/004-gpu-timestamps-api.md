# 004. GPU timestamps as public API

Part: framework (the runtime side is change 4, `../upstream/03-gpu-timestamps.md`).

## Need

An application's GPU profiler times every frame section with GPU timestamps recorded in MonoGame's own frame command
buffer. Its helper reached the runtime's `MGG_TimestampPool_*` by reflection on the internal `GraphicsDevice.Handle` and
`NativeLibrary` exports. New effects are judged on their GPU time, so the timestamps are a facility of the framework.

## Cause

The framework was a NuGet package (issue 001).

## Change

`GpuTimestampPool : GraphicsResource` (`Graphics/GpuTimestampPool.cs`):
- `new GpuTimestampPool(GraphicsDevice, int count)`: a Vulkan timestamp query pool (`NotSupportedException` if the device
  cannot create one);
- `bool Reset(int first, int count)`: resets a range in the frame's command buffer; false inside a render pass or outside
  a frame (that frame goes untimed);
- `void Write(int index)`: `vkCmdWriteTimestamp` at the bottom of the pipe, at this point of the frame;
- `bool TryGetNanoseconds(int index, out ulong ns)`: without waiting; false until the frame completed;
- `Count`, `NanosecondsPerTick`; ranges are checked (`ArgumentOutOfRangeException`).

The application's reflection-based helper is deleted; its per-section profiler (the application's own) uses the pool.

## Test

Captures report the same GPU time per section before and after the change, within the run-to-run spread.
