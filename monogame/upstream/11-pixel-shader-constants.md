> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] A pixel shader's new constants are ignored unless something else changes too

### Problem

`MGG_GraphicsDevice_SetConstantBuffer` marks a change with one bit per shader stage:

```cpp
device->uniformsDirty |= 1 << (int)stage;   // Vertex = 0, Pixel = 1
```

`MGVK_UpdateDescriptors` decides whether a shader's constants must be uploaded by masking that with the shader's mask
of constant-buffer slots, which is bit 0 for the usual single buffer in slot 0:

```cpp
if ((device->uniformsDirty & shader->uniformSlots) == 0 && /* textures and samplers unchanged */)
    return;
```

For the vertex stage the two bits coincide. For the pixel stage they never do: a pixel shader's new constants are
uploaded only when a texture or sampler changed as well, or the vertex shader's constants did, or the pipeline was
rebound. Otherwise the draw keeps the previous draw's dynamic offset and reads the previous draw's constants.

Most draws change something else at the same time, which hides it. A sequence of draws with the same full-screen
vertex shader (no constants), the same textures and only a different pixel-shader parameter shows it every time. For
example, a GGX prefilter of the environment map draws one roughness level per draw. On Vulkan every level after the
first used the first level's roughness, read back from the target: 0.25, 0.25, 0.25, 0.25 instead of 0.25, 0.5, 0.75,
1.0. The rough reflections lost most of the environment's glow (the ground 25 % darker than on OpenGL).

Repro: `monogame/runtime/repro/StateCacheRepro`, test 5 (`Content/PixelConstants.fx`): two draws that change only a
pixel-shader colour. The official runtime draws the first colour twice.

### The fix in the fork (change 11)

The test uses the stage's bit:

```cpp
bool uniformsChanged = shader->uniformSlots != 0 && (device->uniformsDirty & (1u << (int)shader->stage)) != 0;
```
