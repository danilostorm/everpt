> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] State, render-pass and pipeline caches return the wrong object on a hash collision

### Problem

`MGG_Vulkan.cpp` caches five kinds of objects by a 32-bit FNV-1a value of their description and never compares the
description itself:

| Cache | Key |
|---|---|
| `blendStates`, `depthStencilStates`, `rasterizerStates` | `MG_ComputeHash` of the `MGG_*_Info` bytes |
| `targetCache` (render pass + framebuffer) | `MG_ComputeHash` of `MGVK_TargetSet` (texture pointers, an `std::optional` array) |
| `pipelines` | `MG_ComputeHash` of `MGVK_PipelineState` (raw pointers; the code says "We could hit hash collisions here") |

1. **Collisions happen with everyday states.** FNV-1a over small integer fields collides far more often than a random
   32-bit function: among 85 683 blend states with `Add` functions and write masks All/RGB/None, 17 pairs collide.
   One pair: `ColorSourceBlend = DestinationColor, ColorDestinationBlend = Zero, AlphaSourceBlend = One,
   AlphaDestinationBlend = Zero` (a multiply) and `Zero / InverseSourceAlpha, BlendFactor / SourceAlpha`, both with
   `ColorWriteChannels = Red | Green | Blue`, hash to `0x880212a5`. `MGG_BlendState_Create` returns the multiply state
   for the second one, which then draws as a multiply.
2. **Pointers in the pipeline key.** A pipeline is found by the addresses of its states, input layout, program and
   target set. `MGG_InputLayout_Destroy` deletes the layout without removing its pipelines, so a new layout allocated
   at the same address (a different vertex format) is drawn with the old pipeline's vertex input state.
3. **A destroyed state stays current.** When a state's last reference goes, it is queued and deleted a few frames
   later, but `pipelineState.blendState` (and the others) may still point at it; `MGVK_UpdatePipeline` then writes
   `->frame` into freed memory. (MonoGame's C# applies a state again at the start of each frame, so this is a write
   into freed memory rather than a visible error in normal use.)
4. `std::map::operator[]` on a miss inserts a null entry in every cache.

### The fix in the fork (change 6)

- Each cached object gets a 64-bit id that is never reused. The pipeline key is the ids (program, input layout,
  target set, blend, rasterizer, depth-stencil) plus the topology, compared in full (`std::unordered_map` with the
  hash only choosing the bucket).
- State caches are `std::unordered_multimap<hash, state>`; each state keeps a copy of the description it was created
  from, and a lookup compares it (`memcmp`). Release removes that exact entry.
- Target sets compare their defined content (texture pointers, first-use flags, count, array slices; an unengaged
  `std::optional` is compared as unengaged, not by its storage).
- Deleting a state that is still current clears it from `pipelineState` and marks the pipeline dirty; destroying an
  input layout does the same for the current layout.

### Repro

`monogame/runtime/repro/StateCacheRepro` draws into a 4×4 render target and checks every result against the blend
equation computed on the CPU: the two colliding states above, then 1500 frames of random blend, depth-stencil and
rasterizer states, each disposed while still current. With the validation layers on (GTX 1050 Ti):

| Runtime | Result |
|---|---|
| official 3.8.6 | the second colliding state draws as a multiply; every stress frame reads the previous frame (note 07) |
| official + the read-back fix only | 1 failure: the collision |
| Etervale fork (changes 6 and 7) | 0 failures in 1500 frames, no validation errors |
