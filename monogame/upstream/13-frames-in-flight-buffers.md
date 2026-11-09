> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Vertex and index buffers are rewritten while frames in flight still read them

### Problem

Vertex and index buffers live in persistently mapped `CPU_TO_GPU` memory, and there are two ways the CPU writes them
while the GPU still reads them.

**1. Discarded buffers are reused too early.** `SetData` with `SetDataOptions.Discard` swaps in a fresh buffer and puts
the old one on `device->discarded`. `Present` moves `discarded` to `pending`, and the next `Present` moves `pending` to
the free list. The buffer can therefore be handed out again two frames after it was discarded.
- That is safe only with two frames in flight. `MGVK_PrepareFrame` waits for the fence of the frame that last used the
  same swapchain slot, which is `frame − swapchainCount`.
- With three swapchain images, frame N's buffer is reused and overwritten while frame N may still be drawing from it.
  NVIDIA on Wayland with FIFO gives three images (`vkGetSwapchainImagesKHR`).

**2. `SetData` without `Discard` writes in place.** It `memcpy`s into the mapped memory with nothing in the way:
- earlier frames still in flight read the new data;
- so do draws already recorded in the frame being built.

  OpenGL orders `glBufferSubData` with the draws, so code that rewrites a buffer between draws works there and not
  here. The source has a TODO for this: "Force discard here if we find we're copying over data still in use".

**3. Buffers are not marked when draws use them.** Vertex buffers get their frame number only when they are rebound,
and index buffers never get one. So the age check of the destroy queue (`destroyBuffers`, `freeFrames`) can also free a
buffer that a frame in flight still reads.

### Symptom

UI quads (a translucent top scrim), light sprites and other dynamic geometry vanished or appeared misplaced for single
frames. It happened when the GPU was the bottleneck and the CPU ran two frames ahead: a heavy model zoomed in on a
preview screen showed it, a lighter one never did.

### The fix in the fork (change 14)

- Every draw stamps its vertex buffers and its index buffer with the frame number (`MGVK_StampDrawBuffers`).
- Discarded buffers stay on the pending list until `frame ≥ last use + swapchainCount`. The check runs in
  `MGVK_RecycleDiscardedBuffers`, called from `MGVK_PrepareFrame` after the slot's fence wait.
- `SetData` without `Discard` on a buffer that a frame within the last `swapchainCount` read is renamed instead of
  written in place: a fresh buffer starts as a copy of the old one, so a partial update keeps the rest. Draws recorded
  earlier, and frames in flight, keep the old content, as in OpenGL.

### How it was found

A glitch check reads the presented frames without synchronising (change 17). Zooming in on the heavy model
from 60 m to 12 m, the frames with a region jumping and coming back went from 9 (225 block excursions, up to 45
levels) to 0.
