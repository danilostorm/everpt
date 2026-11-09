> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Texture.SetData is not ordered with the frame's draws

### Problem

`MGG_Texture_SetData` records the upload into a command buffer from the transfer pool and submits it at once, with its
own fence. The frame being recorded is submitted later, at `Present`. As a result:
- every draw of that frame reads the new texels, including draws issued before the `SetData` call;
- a texture updated twice in one frame (once per view or per instance) shows only the last content in all of that
  frame's draws.

OpenGL orders `glTexSubImage2D` with the draws. Engines written against it update a small per-instance texture
between draws, for example a skinning or node-matrix palette, or a lamp table.

In addition, the `TRANSFER_DST → SHADER_READ_ONLY` transition waits only for the fragment stage, while vertex shaders
sample uploaded textures too (height maps).

### Symptom

With a model on a preview screen, the lit ground under the model and the UI icons alternated between two states in
single frames, by up to 85 levels. That model rewrites small textures every frame: batched node palettes (one per
instance and part, rewritten when the pose changes) and a lamp table. A model with neither did not show it. Changes 14
and 15 together removed it; this note does not separate their shares.

### The fix in the fork (change 15)

- On the thread that created the device, while a frame is recording, the upload goes into the frame's own command
  buffer. The sequence is: end the render pass, barrier, copy, barrier. The next draw begins a new render pass, which
  loads the targets.
- The staging buffer is freed once that frame's slot has come round again.
- Other threads keep the immediate submission.
- `GetData` on a texture uploaded in the current frame first submits the frame.
- The transition waits for the vertex and fragment stages.
