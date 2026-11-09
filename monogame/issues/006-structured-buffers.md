# 006. Structured buffers and their asynchronous upload

Parts: runtime, framework, effect processor (`007`).

## Need

A BVH (nodes, triangles, instances), the light lists of ReSTIR and the compute passes' intermediate data live in GPU
buffers that shaders index: `StructuredBuffer<T>`, `ByteAddressBuffer` and their RW forms. A BVH is tens of megabytes,
built on the CPU when a model loads and rebuilt when the world streams: its upload must not hold the frame (acceptance:
"a 64 MB structured buffer uploads without stalling the frame").

## Cause

MonoGame's buffers are vertex, index and constant buffers only (`MGBufferType`), uploaded by `SetData` on the render
thread. HLSL storage buffers had no type in the effect system, no descriptor in the runtime and no upload path that runs
off the render thread.

## Change

**Runtime** (change 21 of `../runtime/README.md`):
- `MGBufferType::Storage`: device-local, usage storage + indirect + transfer source/destination, filled with zeros once
  at creation (a buffer read before its first write reads zeros).
- Descriptors: `STORAGE_BUFFER` at binding 32 + t for read-only buffers, 64 + u for RW buffers; a null buffer is bound
  where a slot is empty. Every buffer a dispatch or draw uses is stamped with the frame, so it is not destroyed while a
  frame in flight reads it.
- `MGG_Buffer_SetStorageData(device, buffer, offset, data, bytes)`; the caller's memory may be reused on return:
  - on the render thread while a frame records: copy recorded in the frame's command buffer between two buffer barriers
    (earlier reads and writes finish first, later ones see the new bytes), ordered with the draws and dispatches as in
    OpenGL; staging freed when the frame completes (the scheme of change 15);
  - on the render thread between frames: submitted on the transfer pool without waiting, staging freed by
    `MGVK_CleanupPendingTransfers` once its fence signals;
  - on **any other thread**: that thread does all the work. It allocates a *dedicated* staging allocation, copies into
    it, records the copy on a transient command pool of its own, submits under the queue lock, waits for its fence and
    frees everything. The call returns with the bytes in the buffer, and every frame submitted afterwards sees them;
    the render thread waits for nothing.
- `MGG_Buffer_GetStorageData`: read-back for tests and tools (submits the frame being recorded if it used the buffer).

**The stall found and removed.** The first version used the pooled staging allocation (`MGVK_BufferCreate`, as
texture uploads do) on the worker too. The compute check measured one frame of 45 ms during the upload (median 17 ms at
60 Hz). Instrumenting `SetStorageData` showed the cause: 35–40 ms in `vmaCreateBuffer` and 5 ms in the copy.
- 64 MB does not fit the existing host-visible blocks, so VMA creates a new block with `vkAllocateMemory`.
- VMA does this inside `VmaBlockVector::Allocate`'s write lock on the memory type's block list.
- The render thread allocates and frees its own host-visible staging buffers from that block list (uploads, the
  frame's staging released in `Present`): `Present` took 37 ms instead of 8.5 ms, while the draw CPU time (6.7 ms) and
  `Update` (1.3 ms) were unchanged.
- A dedicated allocation (`VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT`) calls `vkAllocateMemory` outside that lock;
  with it the frame during one upload took 16.5 and 18.4 ms.
- Freeing on the worker after its fence keeps `vkFreeMemory` off the render thread too.

**Framework** (`Graphics/StructuredBuffer.cs`):
- `StructuredBuffer : GraphicsResource`: `new StructuredBuffer(device, elementStride, elementCount, BufferShaderAccess)`.
  `Read` binds to `StructuredBuffer`/`ByteAddressBuffer` parameters; `ReadWrite` also to the RW forms and to
  `DispatchIndirect`.
- `SetData<T>(ReadOnlySpan<T>, int byteOffset)` and `SetData<T>(T[])` from any thread (above); `GetData<T>(Span<T>, int)`.
- `EffectParameterType.StructuredBuffer`, `RWStructuredBuffer`, `ByteAddressBuffer`, `RWByteAddressBuffer`;
  `EffectParameter.SetValue(StructuredBuffer)`, `GetValueStructuredBuffer()`. `Apply` checks the stride against the
  HLSL struct's (the DX layout the compiler uses) and the access against the shader's.

Limits: no UAV counters (`IncrementCounter`, `Append`/`ConsumeStructuredBuffer`: a compile error), at most 2 GB per
buffer, offsets multiple of 4.

## Test

- The compute check in a game scene (GTX 1050 Ti, 1600 × 900, high settings, vsync on):
  - a dispatch reads 1 166 400 elements of a 64 MB `StructuredBuffer<uint>` per frame; every value matches the uploaded
    pattern, and the whole buffer read back matches element by element (16 777 216);
  - then 16 uploads of 64 MB in a row from a worker thread while the frames read the other buffer: 33 ms mean, 45 ms
    max each on the worker. Over the 32 frames they covered, the frame interval median was 16.3 ms, max 31.1 ms; the
    240 frames before: median 16.9 ms, max 25.0 ms; GPU time per frame 11.47 ms against 11.42 ms. No frame went past the
    stall limit (twice the median and the longest frame before);
  - the same upload on the render thread between two frames took 40.9 ms there and made a 60.9 ms frame.
- `repro/ComputeRepro`: *buffers* (`RWStructuredBuffer<uint2>`, `RWByteAddressBuffer` written and read back), *ordering*
  (upload A, dispatch, upload B, dispatch in one frame: first copy A, second B), *another thread* (16 MB from a worker
  during 300 frames, then read by a dispatch and read back). Validation and synchronisation validation clean.
- An effect compiler test checks strides (a 16-byte struct, `uint2`), registers and kinds.

Open: the copy runs on the graphics queue, between the frames' work. Sixteen back-to-back uploads lengthened single
frames to 31 ms (`Present` up to 23.9 ms, GPU time per frame unchanged) without crossing the stall limit; a transfer queue (the
GPU's copy engine) would overlap the copies with rendering.
