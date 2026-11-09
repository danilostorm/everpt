> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] The descriptor set cache returns a set by 32-bit hash alone

### Problem

`MGVK_UpdateDescriptors` looks up a cached descriptor set in `MGG_Shader::usedSets`, a `std::map<uint32_t, ...>`
keyed by an FNV-1a hash of the frame index, the uniform and texture slot masks, and the id of the texture and of the
sampler in every used slot. Nothing compares the ids themselves. Two bindings whose hashes collide get the same
descriptor set, so a draw samples another draw's textures for as long as the set stays cached.

This is the same defect as the state caches (note 06). A collision is rare per frame, but a scene with thousands of
textures (terrain tiles, imagery, model materials) produces many distinct bindings per shader, and the cache keeps
them for several frames. The result is a texture briefly showing the wrong image, which cannot be reproduced on demand.

### The fix in the fork (change 10)

`MGVK_DescriptorInfo` keeps the key it was written for (frame index, slot masks, texture and sampler id per used
slot). `usedSets` becomes a `std::multimap`; the hash narrows the search and the full key decides. The key is built on
the stack, and a vector is filled only when a new set is written.
