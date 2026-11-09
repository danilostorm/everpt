> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] A mid-frame flush submits drawing into a swapchain image that is not yet acquired

### Problem

`MGVK_FlushCommands` submits the frame's commands recorded so far and waits for them. It is called from:
- `GetData` on a render target written this frame;
- the uniform-buffer overflow (fork change 9).

The submission does not wait on `imageAcquiredSemaphore`. If those commands already draw into the frame's swapchain
image (UI drawn before a read-back), they write an image the presentation engine may still own. `Present` then waits on
the semaphore as usual.

The window-resize path (fork change 12) also submits with that wait. When a flush had already consumed it, the wait was
made a second time on a semaphore with no pending signal: `VUID-vkQueueSubmit-pWaitSemaphores-03238`.

### The fix in the fork (change 16)

`MGVK_Frame::acquireWaited` is cleared when the image is acquired. The first submission of the frame that has an image
waits on the semaphore and sets the flag:
- in a flush;
- in the resize path;
- at `Present`.

Later submissions of the frame do not wait again.

With the fix, the Khronos validation layer reports nothing in a zoom run of a test scene. Before it reported `03238`
twice (and `00279`, which was a bug in the application's shader).
