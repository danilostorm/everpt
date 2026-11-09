> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Draws past 32 MB of constants before one Present read garbage

### Problem

Every draw whose effect constants changed appends them to the frame's uniform buffer (`MGVK_Frame::uniforms`,
32 MB), and `uniformOffset` returns to zero only in `MGVK_PrepareFrame`, after `Present`. When a frame records more
than 32 MB of constants, `MGVK_UpdateDescriptors` takes the `assert(0)` branch ("TODO: Discard or wrap around?"). In a
release build the constants are not written, the offset keeps growing, and the dynamic offsets point beyond the
buffer:

```
VUID-vkCmdBindDescriptorSets-pDescriptorSets-01979: pDynamicOffsets[0] is 33553920, which when added to the buffer
descriptor's range (4416) and offset (0) is greater than the size of the buffer (33554432)
```

An ordinary frame stays well below 32 MB. Rendering the scene several times before one `Present` does not: a
colour-chart capture renders the frame 8 times per display transform into an off-screen target and reads it back.
Every measurement came back black (mean ΔE00 47.6 instead of 1.2).

Repro: `monogame/runtime/repro/StateCacheRepro`, test 4: 80 000 draws with fresh constants into a render target,
then `GetData`. The official runtime returns the wrong colour.

### The fix in the fork (change 9)

Before each draw, `MGVK_EnsureUniformSpace` checks that at least 1 MB is left. If not, it submits the commands recorded
so far and waits for them (as `GetData` already does for a target written this frame), restarts the command buffer,
sets the offset back to zero and marks the render pass and pipeline dirty, so the next draw rebinds everything.
