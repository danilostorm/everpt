> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# Enum conversions fall off the end of the function in release builds

**Where:** `native/monogame/vulkan/MGG_Vulkan.cpp` (MonoGame v3.8.6). The functions are `ToVkFormat` (both
overloads), `ToVkPrimitiveTopology`, `ToVkImageViewType`, `ToVkImageType`, `ToUsage`, `ToVkBlendFactor`,
`ToVkBlendOp`, `ToVkCompareOp`, `ToVkStencilOp`, `ToVkPolygonMode`, `ToVkCullModeFlags` and
`ToVkSamplerAddressMode`.

**Problem:** each function is a `switch` that returns in every `case` and has `default: assert(0);`. With `NDEBUG`
the assert disappears and control falls off the end of a value-returning function. That is undefined behaviour.

Compilers report it:
- MSVC: C4715, "not all control paths return a value". It was an error in a Windows build (188 errors together
  with the C# warning settings that leaked into the C++ project).
- GCC/Clang: `-Wreturn-type`.

**Fix:** after the assert, tell the compiler the default cannot be reached: `__assume(0)` on MSVC,
`__builtin_unreachable()` elsewhere, through one macro. Returning a fallback value (for example
`VK_FORMAT_UNDEFINED`) would also work, but would hide a bad enum value in release builds.
