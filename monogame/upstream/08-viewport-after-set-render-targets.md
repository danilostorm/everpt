> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] A viewport set after SetRenderTarget is replaced by the full target at the next draw

### Problem

`MGG_GraphicsDevice_SetRenderTargets` only records the new targets. The render pass begins lazily, at the next draw
(`MGVK_UpdateRenderPass`), and that function sets the viewport and the scissor rectangle to the full target. A
viewport or scissor rectangle the application set between `SetRenderTarget` and the draw, the normal XNA order, is
thrown away:

```csharp
device.SetRenderTarget(atlas);
device.Viewport = new Viewport(x, y, w, h);   // a tile of an atlas, a band of a map
effect.CurrentTechnique.Passes[0].Apply();
device.DrawPrimitives(...);                   // draws over the whole target
```

On OpenGL and DirectX the draw covers only the viewport. On Vulkan the full-screen triangle of each tile or band
covers the whole target, so the last one drawn overwrites the others.

In a test scene a shadow map is built a quarter at a time, one band per frame. Every band overwrote the whole map
with its own rows stretched over it, the map's front depths moved far beyond the ground, and the shadow disappeared
from the scene: a shaded scene was lit by the direct sun and metered 2 EV brighter than the OpenGL build.

### The fix in the fork (change 8)

`SetViewport` and `SetScissorRectangle` mark the value as chosen; `SetRenderTargets` clears the marks;
`MGVK_UpdateRenderPass` applies the full-target defaults only where nothing was chosen:

```cpp
if (!device->viewportSet)
{
    MGG_GraphicsDevice_SetViewport(device, 0, 0, cached->width, cached->height, 0, 1);
    device->viewportSet = false;
}
```

(the same for the scissor rectangle). Viewport and scissor are dynamic state of the command buffer, so the value
recorded by `vkCmdSetViewport` in `SetViewport` stays in effect inside the render pass that begins later.
