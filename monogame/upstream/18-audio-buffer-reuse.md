> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# Streaming voices lose free buffers when several are available

**Where:** `native/monogame/faudio/MGA_faudio.cpp`, `MGA_Voice_AppendBuffer` (MonoGame v3.8.6).

**Problem:** the loop that looks for a free `MGA_RawBuffer` large enough for the new data erases the one it found from
`freeRawBuffers` but keeps iterating. The index then skips the element that moved into the erased slot, and a later
match overwrites `raw`. With three or more fitting free buffers, every match but the last is removed from the list and
never used or freed again: a native memory leak for `DynamicSoundEffectInstance` users that submit buffers after
several have finished (any streaming audio after a hitch).

**Fix:** stop the search at the first fitting buffer (`break;` after the `erase`).
