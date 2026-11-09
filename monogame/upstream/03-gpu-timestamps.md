> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] GPU timestamp queries in the frame command buffer

### Problem and the fix in the fork

There is no way to measure GPU time per pass on the native backends. `GetNativeHandles()` exposes the device and the
queue, but not the frame's command buffer, so an application cannot place `vkCmdWriteTimestamp` between MonoGame's
draws.

This adds a small native API, exported like the rest of `MGG_*`:

```c
void*   MGG_TimestampPool_Create(MGG_GraphicsDevice* device, mguint count);
void    MGG_TimestampPool_Destroy(MGG_GraphicsDevice* device, void* pool);
mgbool  MGG_TimestampPool_Reset(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count); // outside a render pass
void    MGG_TimestampPool_Write(MGG_GraphicsDevice* device, void* pool, mguint index);
mgbool  MGG_TimestampPool_Read(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count, mgulong* ticks); // no wait
mgfloat MGG_TimestampPool_GetPeriod(MGG_GraphicsDevice* device); // ns per tick
```

- `Reset` records `vkCmdResetQueryPool` and returns false inside a render pass. Ending the pass there would change its
  load/store operations.
- `Write` records `vkCmdWriteTimestamp(BOTTOM_OF_PIPE)` at the current point of the frame.
- `Read` returns false until the results are available, so a profiler reads them a few frames later and never stalls.

A managed wrapper could follow, for example a `GpuTimer` class next to `OcclusionQuery`; the native part is useful on
its own.

Validated in an application: GPU time of a full-screen ray-marching pass from these timestamps against the slope of
the frame interval over 1 to K passes per frame (vsync off), GTX 1050 Ti: 129.43–129.76 ms against 129.45–129.67 ms.
