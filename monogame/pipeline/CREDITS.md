# Credits: monogame/pipeline

| Component | Where | Licence | Source |
|---|---|---|---|
| MonoGame effect compiler (`Tools/MonoGame.Effect.Compiler/Effect` of v3.8.6), Vulkan profile, modified | `MonoGame.Effect.Compiler/Effect` | Microsoft Public License, `../LICENSE-MonoGame.txt` | https://github.com/MonoGame/MonoGame (tag v3.8.6, `c610a529667336f63da323c6057274258727664b`) |
| MonoGame `EffectProcessor` (`MonoGame.Framework.Content.Pipeline/Processors/EffectProcessor.cs` of v3.8.6), as `SpirvEffectProcessor` | `MonoGame.Effect.Processor` | Microsoft Public License, `../LICENSE-MonoGame.txt` | same tag |
| CppNet (C# port of JCPP, the C preprocessor of the effect compiler) | `ThirdParty/CppNet` (vendored, unchanged) | Apache License 2.0, `ThirdParty/CppNet/LICENSE` | https://github.com/MonoGame/CppNet at `3dc3eb2db05be9d80b7cc5d00d5814cbe8f0ded2` (the tag's `external/CppNet` submodule) |

Used at build time, not vendored: the `dotnet-mgcb` 3.8.6 tool (`.config/dotnet-tools.json`; MonoGame, MS-PL), whose
content-pipeline and framework assemblies the processor is compiled against and whose DXC (DirectX Shader Compiler,
its LLVM, MIT and Microsoft licence texts are shipped next to it in the tool) compiles the shaders.
