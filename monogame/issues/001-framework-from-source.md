# 001. MonoGame.Framework from source

Part: framework (`monogame/framework`), the runtime moved into `monogame/runtime`.

## Need

- Only the native half of MonoGame was forked. `MonoGame.Framework` (C#) came from the NuGet package
  `MonoGame.Framework.Native` 3.8.6.
- What the fork had added to the runtime reached C# from outside: the application's frame-capture and timestamp helpers
  read the internal `GraphicsDevice.Handle` by reflection and called `MGG_FrameCapture_*` and `MGG_TimestampPool_*`
  through `NativeLibrary`.
- That held for two functions. Compute shaders, storage images and structured buffers (needed for ray tracing) need new
  types and techniques in the effect system and new profiles in the content pipeline: a fork of the C# half.

## Cause

The package is a compiled assembly: its internals can only be reached by reflection, and its effect reader and
`EffectPass` cannot be extended.

## Change

- The native runtime moved to `monogame/runtime` (build script, patch flow and repro unchanged apart from the paths);
  the upstream notes to `monogame/upstream`, MonoGame's licence to `monogame/LICENSE-MonoGame.txt`.
- `monogame/framework/MonoGame.Framework`: the 479 files that v3.8.6's `MonoGame.Framework.Native.csproj` compiles, taken
  from the tag (`framework/README.md` "How it was obtained"), with the fork's own project file. StbImageSharp and
  StbImageWriteSharp at the submodule commits of the tag in `framework/ThirdParty`.
- The application's projects reference the framework project instead of the package. The application no longer takes
  the `MonoGame.Runtime.*.Vulkan` packages: the fork's library is copied next to the executable (in Etervale,
  `EtervaleRuntimeCopy`, and `EtervaleRuntimePublish` for published builds, in `Client/MonoGameRuntime.targets`), where
  the framework's `DllImport("mgruntime")` finds it.
- The framework does not import the repository's C# settings (its analyzers and nullable context: upstream code was not
  written for them) but keeps every warning an error; the 27 upstream warnings (and NETSDK1210 below) were fixed at
  their cause (`framework/README.md`). `IsAotCompatible` was dropped: a netstandard2.1 library cannot carry it
  (NETSDK1210).
- `monogame/runtime/repro`: the repro programs build with the repository's C# settings again
  (`repro/Directory.Build.props`; the runtime folder's empty props, added for the Windows C++ build, had left them
  without a target framework), on the fork's framework, or on the official packages with `EtervaleFork=false`.

## Test

- Built unchanged, the assembly is byte for byte the size of the package's (762 880 bytes): the same compile set.
- The application builds in Debug and Release with 0 warnings and its test suite passes.
- Captures of the application's scenes before and after the switch are equal within the run-to-run noise.
