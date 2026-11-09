# 017. The maximized state of a window

Parts: runtime, framework.

## Need

Etervale saves where the player left the game window and reopens it there. MonoGame's `GameWindow` reports the
position and the client size but not whether the window is maximized, so the game could neither reopen a maximized
window maximized nor tell a maximized client size from a size the player chose: a maximized window overwrote the saved
windowed size, and the next start opened an unmaximized window as large as the screen.

## Cause

The runtime's SDL window exposes no maximized state, and `GameWindow` has no member for it.

## Change

**Runtime** (`src/sdl/MGP_sdl.cpp`): `MGP_Window_GetIsMaximized` (`SDL_GetWindowFlags` and `SDL_WINDOW_MAXIMIZED`)
and `MGP_Window_Maximize` (`SDL_MaximizeWindow`), on every platform SDL drives.

**Framework** (`GameWindow.cs`, `Platform/Native/GameWindow.Native.cs`, `Platform/Native/Platform.Interop.cs`):
`GameWindow.IsMaximized` and `GameWindow.Maximize()`, virtual with no effect on other platforms and implemented by the
Native window; a maximized window keeps the restored size and position it had before.

Interface version 7 (issue 002).

## Test

Etervale's client on Windows: a 1280 × 720 window at (300, 200) maximized during play saves `windowMaximized` and keeps
1280 × 720 at (300, 200); the next start opens it maximized, and restoring it returns it to 1280 × 720 at (300, 200).
