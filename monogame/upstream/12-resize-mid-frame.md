> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] A window resize in the middle of a frame crashes in Clear

### Problem

When the window manager resizes the window while a frame is being recorded (a window requested taller than the work
area, e.g. 1920×1048 on a 1080-line screen; or 6400×3600), MonoGame calls `MGG_GraphicsDevice_ResizeSwapchain`
from inside the frame. It recreates the swapchain, which destroys the target-set cache and sets
`pipelineState.targets` to null, and then `MGVK_PrepareFrame` begins the frame's command buffer again (an implicit
reset) while:
- a render pass was still open in it (`inRenderPass` stays true), and
- the commands recorded so far are thrown away.

The next `Clear` calls `MGVK_UpdateRenderPass`, which returns early because `inRenderPass` is set and the targets are
not dirty, and then reads the null target set: a segmentation fault in `vkCmdClearAttachments`. Under the validation
layers the first message is `VUID-vkCmdEndRenderPass-renderpass` (ending a render pass the new command buffer never
began).

### The fix in the fork (change 12)

`ResizeSwapchain`, when the frame is recording, ends the open render pass and submits the commands recorded so far
(waiting on the image-acquired semaphore if a swapchain image was acquired), waits for them, and marks the render
targets and pipeline dirty, so the next draw begins a render pass for the targets still bound. The guard skips the
first call, made before any swapchain (and frame) exists.

Both sizes above now run without a crash and without validation messages.
