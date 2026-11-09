# 016. Depth surfaces: render targets that are a depth buffer

Parts: runtime, framework.

## Need

Etervale's sun shadow map was a 32-bit float colour target (`SurfaceFormat.Single`) with a 24-bit depth buffer beside
it: every caster ran its pixel shader to write `z/w` into the colour while the depth buffer did the occlusion, and the
lighting sampled the colour. At 8192 × 8192 that is 256 MB of colour plus 256 MB of depth, two writes per covered pixel,
and a pixel shader for every caster. The sun's cascades took 3.8 ms of a 9.0 ms GPU frame on a GTX 1050 Ti (Ricarten,
26 actors). A shadow map needs only the depth: a pass with no colour attachment lets the device skip the pixel shader of
an opaque caster, and the lighting can sample the depth itself.

## Cause

MonoGame offers no render target without colour and no way to sample a depth buffer: the depth of a `RenderTarget2D`
is a separate image with only the depth-attachment usage, and every target pass has one colour attachment per target.

## Change

**Framework** (`Graphics/SurfaceFormat.cs`, `Graphics/GraphicsExtensions.cs`): `SurfaceFormat.Depth32Float` (value
120, 4 bytes a texel). A `RenderTarget2D` of this format is created with `DepthFormat.None`, no mipmaps and no
multisampling.

**Runtime** (`src/include/api_enums.h`, `src/vulkan/MGG_Vulkan.cpp`): `MGSurfaceFormat::Depth32Float` maps to
`VK_FORMAT_D32_SFLOAT`; `MGVK_IsDepthSurface` names such a target.
- `MGG_RenderTarget_Create` gives its image the sampled and depth-attachment usages and no separate depth texture; its
  views take the depth aspect from the format, and it rests in `SHADER_READ_ONLY_OPTIMAL`, the layout its descriptors
  use, as every other target does.
- A pass on it has no colour attachment and the target as its depth attachment (loaded and stored like any target,
  change 2), moved to `DEPTH_STENCIL_ATTACHMENT_OPTIMAL` and back by the render pass. The pass's outgoing dependency
  also waits for the late fragment tests' depth writes, so a later shader reads them.
- The target set records its colour attachment count, and pipelines take their blend attachment count from it (0 for a
  depth surface) instead of the target count.
- `Clear` of a depth surface clears its depth (the `Target` option has nothing to clear), and a clear left with nothing
  to clear records no command.

Interface version 6 (issue 002).

## Test

`../runtime/repro/DepthSurfaceRepro`: an 8 × 4 depth surface drawn by passes without colour, read back through a pixel
shader into a `Vector4` target and compared exactly: depth from a full triangle and from one cut by `clip`, read with
`Load` and with `GatherRed`; the content kept into a later pass and depth-tested there; a clear naming the target and the
depth. All four pass.

Etervale's sun shadow map uses it: the captures of the shadowed HUD-target scenes are unchanged (largest difference 15
levels on isolated pixels, mean 0.000) and the sun's cascades fell from 3.8 ms to 1.7 ms (GPU frame 8.9 ms to 6.8 ms).
