# monogame/pipeline: the effect processor of Etervale's MonoGame fork

MGCB compiles the client's effects (`Client/PristonTale/Content/Content.mgcb`) with the processor built here instead of
the one inside the `dotnet-mgcb` tool. What it adds to MonoGame v3.8.6's Vulkan effect compiler (`../issues/005` to
`008`):
- **compute passes**: `ComputeShader = compile cs_6_0 F();` in a pass of its own;
- **storage resources**: `StructuredBuffer`, `ByteAddressBuffer` (t registers), `RWStructuredBuffer`,
  `RWByteAddressBuffer`, `RWTexture2D` (u registers), and `RaytracingAccelerationStructure` (SM 6.5);
- **shader model 6.5**: `vs_6_5`, `ps_6_5`, `cs_6_5`, for Vulkan 1.2 (SPIR-V 1.5) with `SPV_KHR_ray_query`;
- **MGFX 12**: each shader's stage, storage list, thread group size and requirements, each pass's compute shader. The
  framework of `../framework` reads MGFX 10 to 12.

A `vs_6_0`/`ps_6_0` effect compiles to the same shaders as with upstream's processor: a game's 28 effects, 142
shaders, were identical byte for byte (issue 007).

| Path | Contents |
|---|---|
| `MonoGame.Effect.Compiler/` | `Effect/`: MonoGame v3.8.6's `Tools/MonoGame.Effect.Compiler/Effect`, Vulkan profile only (the DirectX, OpenGL/MojoShader, PSSL and Wine files are left out), with the fork's changes marked `Etervale fork`; `Dxc.cs` (the DXC of the `dotnet-mgcb` tool) and `EffectCompiler.cs` (the public façade: parse, compile, write) are the fork's own. Library `MonoGame.Effect.Compiler.dll`, net8.0. |
| `MonoGame.Effect.Processor/` | `SpirvEffectProcessor`: MonoGame's `EffectProcessor` over the compiler, DesktopVK only. Built into `bin/` (git-ignored), the folder `Content.mgcb` loads with `/reference`. It is compiled against the content-pipeline and framework assemblies of the `dotnet-mgcb` tool itself (`EtervaleMgcbTool` in `Directory.Build.props`, not copied), so the plugin binds to what MGCB loaded. |
| `ThirdParty/CppNet/` | The C preprocessor (C# port of JCPP), vendored unchanged from the tag's `external/CppNet` submodule; compiled into the compiler as upstream links it. |
| `Directory.Build.props` | net8.0 (MGCB is a net8.0 tool), C# 12, nullable, implicit usings, warnings as errors; MonoGame's settings for this code, not the repository's analyzers. The tool's location: `dotnet tool restore` puts `dotnet-mgcb` in the NuGet cache. |
| `CREDITS.md` | Licences (MS-PL for MonoGame's code, Apache-2.0 for CppNet). |

## How it is built and used

- `Client/PristonTale/PristonTale.csproj` references `MonoGame.Effect.Processor.csproj` without an assembly reference
  (`ReferenceOutputAssembly=false`), so `dotnet build` builds the processor before the content. The processor project
  runs `dotnet tool restore` itself when the tool is not in the NuGet cache.
- `monogame/runtime/repro/ComputeRepro` uses it the same way for its effects.
- Outside MGCB (an effect test, for example), the compiler's `EffectCompiler` façade runs the same steps, and `Dxc.cs`
  finds the same DXC in the NuGet cache at the version of the tool manifest (`.config/dotnet-tools.json`).

## How it was obtained (pinned)

From the MonoGame checkout of tag v3.8.6 (`c610a529667336f63da323c6057274258727664b`, the tag of `../runtime` and
`../framework`):
1. `Tools/MonoGame.Effect.Compiler/Effect/` copied without `ConstantBufferData.{DirectX12,mojo,sharpdx}.cs`,
   `EffectObject.{hlsl,pssl}.cs`, `ShaderData.{mojo,pssl,sharpdx}.cs`, `ShaderProfile.{DirectX11,DirectX12,OpenGL}.cs`
   and `WineHelper.cs`;
2. `MonoGame.Framework.Content.Pipeline/Processors/EffectProcessor.cs` as the base of `SpirvEffectProcessor.cs`;
3. `external/CppNet` at the submodule commit `3dc3eb2db05be9d80b7cc5d00d5814cbe8f0ded2`
   (https://github.com/MonoGame/CppNet), its `.cs` files and `LICENSE`, `README.md`.

The TPG grammar (`Effect/TPGParser/MGFX.tpg`) and the parser generated from it (`Scanner.cs`, `Parser.cs`,
`ParseTree.cs`) were edited together, by hand, for the `ComputeShader` state: TinyPG was not run, and the grammar
is kept in step with the generated code so that it can be.

Updating MonoGame: the same steps on the new tag, the fork's changes reapplied (`Etervale fork` markers, `../issues`),
and the compiled effects and rendered frames compared before and after.
