> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Keep render target contents across frames (fixes #9459)

### Problem and the fix in the fork

`MGVK_UpdateRenderPass` marks a target as "first use" when it is bound for the first time in a frame
(`target->writeFrame != currentFrame`). It then begins the render pass with:
- `VK_ATTACHMENT_LOAD_OP_DONT_CARE`;
- `initialLayout = VK_IMAGE_LAYOUT_UNDEFINED`.

So the previous frame's pixels are undefined even for a `RenderTarget2D` created with
`RenderTargetUsage.PreserveContents`. Immediate-mode desktop GPUs usually keep them anyway. Tiled GPUs (Apple Silicon
through MoltenVK) do not: #9459 shows a preserved target losing everything outside the area drawn in the frame.

Accumulation techniques draw into a target over several frames:
- temporal anti-aliasing histories;
- progressive or banded updates of shadow and environment maps;
- paint programs.

They rely on the OpenGL/DirectX behaviour.

This change treats a target as "first use" only when it has never been written (`writeFrame == -1`). From then on it
loads its contents. On desktop GPUs the load is free: nothing is discarded and the memory is already resident.

An alternative is to honour `RenderTargetUsage` (load for `PreserveContents`, don't care for `DiscardContents`). That
matches XNA's documented semantics but changes behaviour for every game that relies on the default `DiscardContents`
keeping its pixels on desktop, as it does on DesktopGL today. Happy to switch to that if preferred.

Repro: the project in #9459, on an Apple Silicon Mac with DesktopVK.
