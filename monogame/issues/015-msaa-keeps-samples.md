# 015. A multisampled target keeps its samples between render passes

Parts: runtime.

## Symptom

With 4x MSAA Etervale's opening drew the sky for a few seconds and then black behind the terrain; with TAA (no
multisampled targets) the sky stayed. The sky went black from the frame the first transient point light appeared.

## Cause

The render pass of a multisampled target loaded and stored its multisampled image with `DONT_CARE` and declared it
`UNDEFINED` on entry, so only the resolve survived a pass. Once point lights exist Etervale runs the tiled light culling
(a compute dispatch) between the sky and the opaque scene and binds the scene target again: the new pass started from
undefined samples, discarding the sky, and its resolve overwrote the resolved image with them. Any rebinding of a
multisampled target after a compute dispatch, a copy or another target lost what it held, unlike every other target
(change 2 of `../runtime/README.md`: a target keeps its content).

## Change

**Runtime** (`src/vulkan/MGG_Vulkan.cpp`, `MGVK_UpdateRenderPass`): the multisampled colour attachment loads its
samples (`LOAD`, entering in `COLOR_ATTACHMENT_OPTIMAL`, the layout every pass leaves it in) unless the target has
never been written, and stores them. The resolve attachment is unchanged.

## Test

Etervale's opening with the owner's settings (4x MSAA, 1920x1009): the mean sky brightness stays 66 to 79 over seven
seconds (before: 72 for three seconds, then about 5). Render benchmark (GTX 1050 Ti, 4x MSAA, Forest 1 and Ricarten,
same GPU load): 85 to 89 FPS before, 89 to 91 after; on an immediate-mode GPU the samples were written to memory
either way.
