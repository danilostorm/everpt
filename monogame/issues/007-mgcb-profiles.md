# 007. The effect processor in the fork: compute passes, storage resources, MGFX 12

Part: effect processor (`../pipeline`), framework (the reader).

## Need

The extensions of issues 005, 006 and 008 need the effect system to carry them from HLSL to the framework:
- compute passes in a technique (`ComputeShader = compile cs_6_0 F();`);
- the storage resources of each shader, with their registers, strides and formats;
- the thread group size and what a shader needs from the device (ray queries);
- the profiles `cs_6_0`, `vs_6_5`, `ps_6_5`, `cs_6_5`.

## Cause

The game's effects were compiled by `MonoGame.Framework.Content.Pipeline` of the `dotnet-mgcb` 3.8.6 tool:
- its `EffectProcessor` knows vertex and pixel shaders at `vs_6_0`/`ps_6_0` only, fixed in `ShaderProfile.Vulkan`;
- a storage buffer or image in a shader is not reflected, so the runtime would get no binding for it;
- MGFX 11 has no field for any of the above.

## Change

**`../pipeline/MonoGame.Effect.Compiler`**: MonoGame v3.8.6's `Tools/MonoGame.Effect.Compiler/Effect` (the code MGCB
and mgfxc compile), Vulkan profile only. Its DirectX, OpenGL/MojoShader, PSSL and Wine files are left out; CppNet, the C
preprocessor, comes from `ThirdParty/CppNet` (the tag's `external/CppNet` submodule). The fork's changes are marked
`Etervale fork`:
- **parser** (`TPGParser/MGFX.tpg` and the generated `Scanner`/`Parser`/`ParseTree`, `PassInfo`): the `ComputeShader`
  pass state;
- **profile** (`ShaderProfile.Vulkan.cs`):
  - the models `vs/ps/cs_6_0` and `vs/ps/cs_6_5` (`DxcArguments`; issue 008 for 6.5);
  - `-fvk-u-shift 64 all`, which places u registers after the textures;
  - a compute pass holds the compute shader only;
  - the storage resources from DXC's `UserTypeGOOGLE` reflection: kind, register, stride (the `ArrayStride` of the
    block's runtime array), the image format mapped to a `SurfaceFormat`;
  - their descriptors in the shader's binding header for the runtime;
  - `LocalSize` → thread group; the `RayQueryKHR` capability → requirement;
- **errors for what the runtime cannot bind**: RW resources outside compute shaders, UAV counters, RW types other than
  `RWTexture2D`/buffers, unknown image formats, registers outside t0–t15/u0–u7, ray queries outside SM 6.5;
- **SPIR-V reflection**: the full `StorageClass` enum, `UserTypeGOOGLE`, `ArrayStride`, `LocalSize`, `OpCapability`,
  `OpTypeAccelerationStructureKHR`;
- **MGFX 12** (`EffectObject.writer.cs`, `ShaderData.writer.cs`):
  - each shader's stage as a byte (vertex 0, pixel 1, compute 2) where MGFX 11 had a vertex flag;
  - after its attributes, the storage list (kind, slot, stride, format, name, parameter index), the thread group size
    and the requirement bits;
  - each pass gains its compute shader index;
  - storage parameters are effect parameters of the new types (class object, no data);
- **`Dxc.cs`**: runs the DXC that ships with the `dotnet-mgcb` tool (inside MGCB, the tool's folder; in the tests, the
  NuGet cache at the version of `.config/dotnet-tools.json`), in place of upstream's `MonoGame.Tool.Dxc` package;
- **`EffectCompiler.cs`**: a public façade (parse, compile, write), so the processor and the tests run the same steps.
  It returns `MemoryStream.ToArray()` where upstream passed `GetBuffer()` on: the stream's unused capacity was written
  as trailing zeros into every `.xnb`.

**`../pipeline/MonoGame.Effect.Processor`**: `SpirvEffectProcessor` (DesktopVK only), MonoGame's `EffectProcessor`
over the compiler above. It is built into `monogame/pipeline/bin/` against the tool's own content-pipeline and framework
assemblies (not copied), so the plugin binds to exactly what MGCB loaded. The game's `Content.mgcb` loads it with
`/reference` and every effect uses `/processor:SpirvEffectProcessor`. The game project builds it first through a
project reference without an assembly reference.

**Framework**: `Effect` reads MGFX 10 to 12; `Shader` reads the stage, the storage list (`StorageKind`, `StorageInfo`),
the thread group size and the requirements (`ShaderRequirements`); `EffectPass` gets its compute shader.

## Test

- **A game's 28 effects compile to the same shaders**: each built with upstream's processor and with the fork's, then
  compared field by field. All 142 shaders are identical:
  SPIR-V, binding header, samplers, constant buffers, attributes, parameters, techniques. Only the version byte differs
  (MGFX 11 → 12). The `.xnb` total went from 5 247 750 to 2 628 787 bytes (the trailing zeros).
- Captures of the game's scenes before and after are equal within the run-to-run noise.
- Effect compiler tests (11): a compute-check effect (compute pass, storage, thread group, MGFX 12 header); strides and
  registers of every buffer kind; SM 6.5 ray queries; the DXC arguments of `vs_6_0`/`ps_6_0` equal to upstream's plus
  the u shift; each compile error above; the tool version against the manifest.
- A constant-buffer test reads MGFX 12 (the storage records are skipped to reach the parameters).
