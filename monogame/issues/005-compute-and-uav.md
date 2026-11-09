# 005. Compute pipelines and storage images (UAV)

Parts: runtime, framework, effect processor (`007`).

## Need

Ray-traced effects run as compute shaders in the frame:
- the software traversal of a BVH, ReSTIR and the denoisers;
- each pass writes an image the next pass or the composite reads (`RWTexture2D`);
- some passes size the next dispatch on the GPU (`DispatchIndirect`).

The frame graph also needs the two orders inside one command buffer: graphics → compute (the opaque scene's depth into a
ray pass) and compute → graphics (its result into the composite).

## Cause

MonoGame 3.8.6's Native platform has two shader stages (`ShaderStage.Vertex`, `Pixel`), graphics pipelines only, and
sampled textures only:
- the runtime's `MGShaderStage` stops at `Pixel` and `MGG_Shader_Create` makes only graphics shader modules;
- no descriptor type other than uniform buffers, samplers and sampled images;
- the effect system has no compute pass (MGFX 11 writes a vertex-shader flag per shader).

## Change

**Runtime** (`src/vulkan/MGG_Vulkan.cpp`, `src/include/api_enums.h`; change 21 of `../runtime/README.md`):
- `MGShaderStage::Compute`. `MGG_Shader_Create` derives the shader's slot masks from the bindings MGFX 12 writes
  (uniforms at 0, sampled textures and samplers at 32 + t/s, storage images and RW buffers at 64 + u, read-only storage
  buffers at 32 + t). A compute shader gets its own descriptor set layout, pipeline layout and compute pipeline, created
  once with the shader (descriptor set 0: a compute shader is alone in its pipeline).
- `MGG_Texture_CreateStorage`: a texture with `VK_IMAGE_USAGE_STORAGE_BIT`, refused (null) when the format has no
  `VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT` for optimal tiling. One storage view per mip, made on first use.
- `MGG_GraphicsDevice_SetShaderImage(stage, slot, texture, mip)` and `MGG_GraphicsDevice_SetShaderBuffer` (issue 006):
  per-stage tables of the u slots (8) and of the storage buffers on t slots; the descriptor cache key includes them.
- `MGG_GraphicsDevice_Dispatch(x, y, z)` and `MGG_GraphicsDevice_DispatchIndirect(buffer, offset)` record into the
  frame's own command buffer:
  - the render pass is ended (the next draw begins a new one, which keeps the targets: change 2);
  - **barrier before**: every earlier attachment, transfer and shader write of the frame is made visible to the compute
    shader and to the indirect-argument read; the storage images move to `GENERAL`;
  - the constants go into the frame's uniform ring as for a draw (with the early submission of change 9);
  - **barrier after**: the shader's writes are made visible to every later stage (vertex input, indirect arguments,
    vertex/fragment/compute shaders, transfers, attachments); the images return to `SHADER_READ_ONLY_OPTIMAL`, the layout
    sampled descriptors expect.
  Global memory barriers cover the buffers, so nothing is tracked by hand between passes. A dispatch whose shader has an
  empty storage-image slot is skipped with a message, not recorded invalid.
- `MGG_Texture_GetData` of a storage image written this frame submits the frame first (as change 7 does for targets).
- `MGG_Shader_Destroy` of a compute shader waits for the device before destroying its pipeline.
- Interface version 2 (`MGG_Etervale_RuntimeVersion`, issue 002).

**Framework:**
- `ShaderStage.Compute`; `Shader` reads MGFX 12's stage, storage list, thread group size and requirements.
- `EffectPass.IsCompute`, `ThreadGroupSizeX/Y/Z`; `Apply` of a compute pass sets the compute stage only (shader, its
  constant buffers, `ComputeTextures`/`ComputeSamplerStates`, storage) and checks every storage parameter: unset,
  disposed, not writable, image format or buffer stride different from the shader's declaration are exceptions there, not
  device errors in a frame.
- `GraphicsDevice.Dispatch(int, int, int)`, `GraphicsDevice.DispatchIndirect(StructuredBuffer, int)`
  (`Graphics/GraphicsDevice.Compute.cs`); a new frame pushes the compute bindings again.
- `TextureShaderAccess` and `new Texture2D(device, w, h, mipmap, format, TextureShaderAccess.ReadWrite)`
  (`NotSupportedException` for a format the device cannot write); `Texture2D.ShaderAccess`.
- `EffectParameterType.RWTexture2D` (and the buffer types of issue 006, `AccelerationStructure` of issue 008);
  `EffectParameter.SetValue(Texture2D, int mipLevel)`; `SetValue(Texture)` binds mip 0.

**Limits** (compile errors in the processor, issue 007, or exceptions at `Apply`):
- RW resources in compute shaders only: a pixel or vertex shader that writes would need `fragmentStoresAndAtomics` /
  `vertexPipelineStoresAndAtomics`, which the runtime does not enable.
- `RWTexture2D` only (no 1D, 3D, array or typed RW buffers), u0–u7, formats `rgba32f`, `rgba16f`, `rg32f`, `rg16f`,
  `r32f`, `rgba8`, `rgba16`, `rg16`, `r8` (declared with `[[vk::image_format]]` or inferred by DXC from the element type).
- A texture is not sampled and written by the same dispatch (the image is in `GENERAL` while its sampled descriptor
  expects `SHADER_READ_ONLY_OPTIMAL`); two passes ping-pong instead.

## Test

- `repro/ComputeRepro` (`../runtime/repro/ComputeRepro`): *image* (37 × 29 RGBA32F written by a dispatch and sampled by a
  draw in the same frame, both exact), *mip* (mip 1 of a mipmapped storage texture, mip 0 untouched), *indirect*
  (arguments (5, 3, 1) written by a dispatch, exactly those 15 groups run once), *frames in flight* (300 dispatches'
  constants summed by atomics without read-back in between). All pass; with `VK_LAYER_KHRONOS_validation` and
  synchronisation validation (`VK_KHRONOS_VALIDATION_VALIDATE_SYNC=true`, confirmed active by the layer's status
  message) no message.
- A compute check in a game scene: after the opaque scene, a raster copy of the linear depth, a dispatch that reads the
  depth and writes a storage image, and a draw that samples the image; 1 166 400 pixels compared bit for bit,
  0 mismatches, three times; validation and synchronisation validation clean.
- An effect compiler test checks the records the framework reads (stage, storage, thread group).
