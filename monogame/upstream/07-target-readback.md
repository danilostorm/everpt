> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] RenderTarget2D.GetData returns the previous frame when the target was only drawn into

### Problem

`MGG_Texture_GetData` copies the image on the transfer queue. Before that it flushes the frame's command buffer, but
only when `texture->frame == device->frame`, and `texture->frame` is stamped only when the texture is bound for
sampling. A render target that was drawn into this frame and is read back without having been sampled does not
trigger the flush: the copy runs before the draw commands are submitted and returns the previous frame's pixels.

Repro: `SetRenderTarget(rt); Clear(c); draw; SetRenderTarget(null); rt.GetData(pixels)`. On 3.8.6 the pixels are
those of the previous frame's draw (`monogame/runtime/repro/StateCacheRepro`: every stress frame read the expected
value of the frame before).

### The fix in the fork (change 7)

The flush also happens when the target was written this frame (`writeFrame`, the frame stamp the fork's change 2 keeps
for render targets):

```cpp
if (frame.is_recording && (texture->frame == device->frame || texture->writeFrame == device->frame))
```

Without change 2, a stamp set where the render pass binds the target (`MGVK_UpdateRenderPass`) gives the same.
