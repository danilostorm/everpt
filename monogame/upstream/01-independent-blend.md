> **Issue note for the owner, not a pull request.** MonoGame's CONTRIBUTING.md ("Large Language Models /
> Generative AI") prohibits contributions made with generative AI, and this patch and text were written by an AI
> agent. So they cannot be sent upstream as they are. What can go upstream is an issue describing the problem,
> written by the owner, or a fix the owner (or another human) writes independently. The facts below are the material
> for that.

# [Vulkan] Enable independentBlend when the device supports it

### Problem and the fix in the fork

`MGG_GraphicsDevice_Create` enables only `sampleRateShading`, `occlusionQueryPrecise` and `samplerAnisotropy`. A
`BlendState` with `IndependentBlendEnable = true` and different settings per render target gives a pipeline whose
`VkPipelineColorBlendAttachmentState` entries differ. The Vulkan spec requires the `independentBlend` feature for that
(VUID-VkPipelineColorBlendStateCreateInfo-pAttachments-00605), so the validation layers report it, and the result is
undefined on drivers that do not ignore it.

This change enables `independentBlend` when `vkGetPhysicalDeviceFeatures` reports it, which is every desktop GPU
(NVIDIA, AMD, Intel, MoltenVK).

```cpp
if (device->deviceFeatures.independentBlend)
{
    enabledFeatures.independentBlend = VK_TRUE;
}
```

Repro: bind two render targets, set a `BlendState` with `IndependentBlendEnable = true`, `ColorWriteChannels1 =
ColorWriteChannels.None` and blending on target 0, draw, and run with `VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation`.
