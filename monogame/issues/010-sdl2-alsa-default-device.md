# 010. The audio device crashes on ALSA, and the runtime's SDL2 knew only ALSA

Part: runtime (SDL2, fetched and patched; `build.sh`).

## Symptom

The owner's normal game run (the audio device opens; automation runs skip it) ended at start with a segmentation fault
(exit 139). gdb on the release runtime: `?? (libmgruntime.so)` ← `FAudio_PlatformGetDeviceDetails` ←
`FAudio_CreateMasteringVoice` ← `MGA_System_Create`. With `SDL_AUDIODRIVER=pipewire` or `pulseaudio` SDL answered
"Audio target not available" and the game ran silent.

## Cause

Two faults, found with a debug build of the pinned SDL2 (2.32.10, `5d24957`) and FAudio (`8a87fdc`) and a C program
that does what `MGA_System_Create` does (`FAudioCreate`, `FAudio_CreateMasteringVoice`):

1. **SDL2's ALSA backend writes through a NULL pointer.** `SDL_GetDefaultAudioInfo(char **name, …)` documents `name`
   as optional ("can be NULL"); FAudio's SDL2 platform layer (`FAudio_platform_sdl2.c`,
   `FAudio_PlatformGetDeviceDetails`) passes `NULL` for the default device. The PulseAudio and PipeWire backends check
   it; `ALSA_GetDefaultAudioInfo` (`src/audio/alsa/SDL_alsa_audio.c:513–518`) assigns `*name` unconditionally. The
   symbolised frame: `ALSA_GetDefaultAudioInfo (name=0x0, …) at SDL_alsa_audio.c:517`, `*name = SDL_strdup(dev_name
   ? dev_name : "Unknown ALSA Device")`, `dev_name = "alsa_playback.repro"` (PipeWire's ALSA plugin). The same
   function also leaked the card name `snd_card_get_name` allocates.
2. **SDL2 was built with only the ALSA backend.** `build.sh` configured SDL2 with its defaults; SDL's configure
   silently leaves out a backend whose development headers are missing, and the build machine had neither
   `pipewire-devel` nor `pulseaudio-libs-devel`. So the game's only sound server was ALSA, where fault 1 crashed it.
3. Found on the way: after rebuilding SDL2, `make` did not relink `libmgruntime.so`: the static `libSDL2.a` and
   `libFAudio.a` enter through premake `linkoptions`, which are not make dependencies, so the old library (without
   PipeWire) stayed in `bin/`.

## Change

- `runtime/patches/sdl2/0001-alsa-default-info-null-name.patch`: `*name` written only when `name` is not NULL, the
  card name freed (`Etervale fork (monogame/issues/010)` marker). `build.sh` applies `patches/<name>/*.patch` to each
  fetched dependency after the checkout (skipping one already applied), and rebuilds a dependency whenever its patches
  or its configuration change (a stamp in its build folder).
- `build.sh` configures SDL2 on Linux with the ALSA, PulseAudio and PipeWire backends, each loaded at run time
  (`SDL_*_SHARED`, as SDL does: the library runs on a machine without one of the servers); it refuses to build when a
  backend's headers are missing, with the install command (Fedora `alsa-lib-devel pulseaudio-libs-devel
  pipewire-devel`, Debian/Ubuntu `libasound2-dev libpulse-dev libpipewire-0.3-dev`), checks after configuring that
  the three were compiled in, and prints the backends (`SDL2 audio backends: ALSA DISK DUMMY OSS PIPEWIRE PULSEAUDIO`).
- `build.sh` always relinks the runtime (the objects are kept; the link takes seconds).
- A Linux CI build of the runtime needs `libpipewire-0.3-dev` installed too.
- Outside the fork, the application's audio thread should survive a device that fails while playing, so the game goes
  on silent, and a device check should exercise the real device path.

## Test

- The C repro on the debug build: before, `SDL_AUDIODRIVER=alsa` → SIGSEGV; after, all three drivers create the
  mastering voice (pipewire: 4 devices, pulseaudio: 4, alsa: 3).
- A game with the device open for 10 s, on the rebuilt release runtime: SDL's default driver (PipeWire), `pipewire`,
  `pulseaudio` and `alsa` all passed: 1000–1001 blocks of 10 ms in 10 s (≥ 99.9 % of real time), 0 underruns, mix
  0.27–0.34 ms mean per block, peak −23 dBFS.
- Three minutes each on PipeWire and ALSA.
