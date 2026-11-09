> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that. The fault is in SDL2 (libsdl-org/SDL), which MonoGame pins as a submodule; the same rule applies to SDL's
> contribution policy.

# Opening the audio device segfaults on SDL2's ALSA backend

**Where:** SDL 2.32.10 (`5d249570`, MonoGame v3.8.6's submodule), `src/audio/alsa/SDL_alsa_audio.c`,
`ALSA_GetDefaultAudioInfo`; called by FAudio (`FAudio_platform_sdl2.c`, `FAudio_PlatformGetDeviceDetails`) from
`FAudio_CreateMasteringVoice`, which MonoGame's `MGA_System_Create` calls on the first sound.

**Problem:** `SDL_GetDefaultAudioInfo(char **name, SDL_AudioSpec *spec, int iscapture)` documents `name` as optional
("can be NULL"), and FAudio passes NULL. The PulseAudio and PipeWire backends check it; the ALSA backend assigns
`*name = SDL_strdup(...)` unconditionally and crashes. Any MonoGame Native (or FNA) game whose SDL picks ALSA, for
example a build without the PipeWire/PulseAudio backends or a user forcing `SDL_AUDIODRIVER=alsa`, dies when it opens
the audio device. The function also leaks the card name returned by `snd_card_get_name`.

**Fix:** write `*name` only when `name` is not NULL, and `free()` the card name.

**Also worth reporting to MonoGame:** its runtime build configures SDL2 with the defaults, so a machine without the
PipeWire and PulseAudio development headers silently gets an SDL2 with ALSA only.
