# 013. NoOverwrite uploads write in place

Parts: runtime, framework.

## Symptom

Etervale's HUD (SpriteBatch, a texture change every few sprites) took 31 ms of CPU per frame on Vulkan, against well
under 1 ms on DesktopGL. A SpriteBatch of 400 sprites took 0.4 ms with one texture and 610 to 1050 ms when the texture
changed between sprites.

## Cause

`GraphicsDevice.DrawUserPrimitives`/`DrawUserIndexedPrimitives` (which SpriteBatch uses for every texture change) keep a
ring of user vertices and indices: each call appends to the dynamic buffer with `SetDataOptions.NoOverwrite` and only
the call that wraps uses `Discard`. The framework passed NoOverwrite to `MGG_Buffer_SetData` as `discard = false`, and
the fork's change 14 (buffers read by frames in flight) treats a non-discard write to a buffer that a recorded or
in-flight draw reads as a rename: a new buffer that first receives a copy of the whole old content, read back from mapped
(write-combined) memory. Every user-primitive draw after the first in a frame paid that copy, about 0.8 ms each.

NoOverwrite is the caller's promise that no recorded or in-flight draw reads the range it writes, which is what the ring
guarantees: the rename was never needed.

## Change

**Runtime** (`src/vulkan/MGG_Vulkan.cpp`, `src/include/api_MGG.h`): `MGG_Buffer_SetDataNoOverwrite(device, buffer,
offset, data, elementCount, vertexStride, elementSizeInBytes)` copies into the buffer in place (push buffers by
`memcpy`, others through the persistent mapping and a flush). Interface version 4 (`MGG_Etervale_RuntimeVersion`,
issue 002).

**Framework** (`Platform/Native/VertexBuffer.Native.cs`, `IndexBuffer.Native.cs`, `Graphics.Interop.Etervale.cs`): a
dynamic buffer's `SetData` with `SetDataOptions.NoOverwrite` calls the new export; `Discard` and `None` are unchanged.

## Test

A SpriteBatch of 400 sprites (scratch benchmark, GTX 1050 Ti, Vulkan): one texture 0.037 ms; a texture change every
sprite over 10, 50, 200 and 400 textures 0.34, 0.33, 0.37 and 0.47 ms (before: 610 to 1050 ms). Etervale's render
benchmark (Ricarten at noon, HUD drawn): HUD section 31 ms → 0.04 ms of CPU. The shader probe (`--verify-shaders`),
the HUD, login and actor captures draw the same images.
