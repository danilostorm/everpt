# 002. The framework checks the runtime's interface version

Part: runtime and framework.

## Need

With the framework built from source, the C# side and the native library change together: the framework calls the
fork's exports (frame capture, timestamps, compute and storage resources), and an export's arguments can
change. A library from elsewhere fails late and obscurely:
- the official `MonoGame.Runtime` library: at the first missing export (`EntryPointNotFoundException` in the middle of a
  frame);
- a stale build of the fork (a worktree that falls back to the main checkout's library, a Windows build of another
  commit): an export whose arguments changed reads garbage.

## Cause

`DllImport` binds by name at the first call and checks nothing else.

## Change

- Runtime: `MGG_Etervale_RuntimeVersion()` returns `MGVK_ETERVALE_RUNTIME_VERSION` (`vulkan/MGG_Vulkan.cpp`).
- Framework: `MGG.EtervaleRuntimeVersion` is the version it is written for
  (`Platform/Native/Graphics.Interop.Etervale.cs`); `MGG.EnsureRuntimeVersion()` runs first in `NativeGamePlatform`'s
  constructor and throws an `InvalidOperationException` that names the found and the needed version and how to rebuild
  (a library without the export reports version 0).
- Both numbers change together with every change of an exported function. Version 1: the exports of the runtime before
  the framework was forked (changes 1–19). Version 2: compute and storage resources (issues 005, 006). Version 3:
  pipeline statistics (issue 012). Version 4: NoOverwrite uploads in place (issue 013). Version 5: the 32-bit float
  depth format (issue 014). Version 6: depth surfaces (issue 016). Version 7: the maximized state of a window (issue
  017).

## Test

- The game starts with the library built from the same commit.
- With an older library copied in its place, the game stops at once with the message instead of failing in a frame
  (checked by hand).
