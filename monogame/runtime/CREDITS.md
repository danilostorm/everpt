# Credits: monogame/runtime

| Component | Where | Licence | Source |
|---|---|---|---|
| MonoGame native runtime (`native/monogame` of v3.8.6), modified | `src/` (except `src/external`), `bin/` | Microsoft Public License, `../LICENSE-MonoGame.txt` | https://github.com/MonoGame/MonoGame (tag v3.8.6) |
| volk | `src/external/volk` (vendored) | MIT, `src/external/volk/LICENSE.md` | https://github.com/zeux/volk |
| Vulkan Memory Allocator | `src/external/vma` (vendored) | MIT, `src/external/vma/LICENSE.txt` | https://github.com/GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator |
| Vulkan-Headers (C headers) | `src/external/vulkan-headers` (vendored) | Apache-2.0 or MIT, `src/external/vulkan-headers/LICENSE.md` | https://github.com/KhronosGroup/Vulkan-Headers |
| stb_image, stb_image_write | `src/external/stb` (vendored) | MIT or public domain, `src/external/stb/LICENSE` | https://github.com/nothings/stb |
| SDL2 | fetched by `build.sh`, linked statically into `bin/` | zlib | https://github.com/libsdl-org/SDL |
| FAudio | fetched by `build.sh`, linked statically into `bin/` | zlib | https://github.com/FNA-XNA/FAudio |
| MoltenVK (macOS library only) | linked from the Vulkan SDK | Apache-2.0 | https://github.com/KhronosGroup/MoltenVK |

Commits: `externals.txt`.
