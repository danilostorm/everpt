# 009. A streaming voice's free buffers are reused one at a time

Part: runtime (audio).

## Symptom

An application's software mixer streams all of its sound through one `DynamicSoundEffectInstance`: a block of 10 ms
(1 920 bytes of 16-bit stereo) every 10 ms, three blocks queued, from an audio thread. Every block goes through
`MGA_Voice_AppendBuffer`
(`runtime/src/faudio/MGA_faudio.cpp`), which takes a free copy buffer from the system's list (`freeRawBuffers`, filled
by `OnBufferEnd`) or allocates one. When three or more free buffers fit, the function used one and **lost the
others**: they left the free list and were never freed or used again (a native leak of one block per lost buffer, and
new allocations afterwards).

## Cause

The search loop takes a fitting buffer and erases it from the vector but does not stop: the next index skips the
element that moved into the erased slot, and a later fitting buffer overwrites `raw`. With the list `[A, B, C]` it
takes `A`, then `C`, and `A` is gone. The list holds three or more buffers whenever the voice finished that many before
the next append, which an audio thread that sleeps a few milliseconds between appends meets during load hitches.

## Change

`break` after the buffer is taken (`Etervale fork (issue 009)` marker). `regenpatch.sh` now also records
`faudio/MGA_faudio.cpp` in `monogame-v3.8.6.patch`. No exported function changes: the interface version stays 2.

## Test

- By reading: with the `break`, the buffer removed from the list is the one submitted, and `OnBufferEnd` returns it.
- With the audio device open (issue 010): 10 s on SDL's default driver, PipeWire, PulseAudio and ALSA, 1000–1001
  blocks of 10 ms each, 0 underruns, and three minutes on PipeWire and on ALSA. Offline renders (no device) do not
  exercise this path.
