> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Feature: read presented frames back without stalling

### Why

`GraphicsDevice.GetBackBufferData`, and `GetData` on a render target, submit the frame being recorded and wait for it.
A test that reads every frame back therefore runs with one frame in flight.
- Races between the CPU and earlier frames still on the GPU, such as buffers rewritten while being read or uploads out
  of order, cannot show up in such a test.
- Capture-based checks passed while the game flickered in normal play.

### The addition in the fork (change 17)

| Export | What it does |
|---|---|
| `MGG_FrameCapture_Start(device, downscale, maxQueued)` | Turns the capture on. |
| `MGG_FrameCapture_Pop(device, data, bytes, &w, &h, &frame, &drawn)` | Returns the next captured frame. |
| `MGG_FrameCapture_Dropped(device)` | Reports how many frames were dropped. |
| `MGG_FrameCapture_Stop(device)` | Turns the capture off. |

How it works:
- At each `Present`, the frame's own command buffer blits the presented image, downscaled, into an image of its frame
  slot, then copies it into a host-visible buffer of the slot.
- The buffer is read when the runtime waits for that slot's fence before reusing it. That wait happens anyway, so the
  capture adds no synchronisation.
- `drawn` reports whether a render pass wrote the presented image that frame.

An application can call the exports through `NativeLibrary`, as it can the timestamp pool (change 4); the fork's
framework wraps them as `GraphicsDevice.BeginFrameCapture`/`TryPopFrame` (`../issues/003-frame-capture-api.md`).
