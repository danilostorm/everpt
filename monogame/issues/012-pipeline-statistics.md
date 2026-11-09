# 012. Pipeline statistics (registers, spills) from the driver

Parts: runtime, framework.

## Need

A ray-marching shader cost twice its predecessor per texel. GPU timestamps say how long a pass takes, not why: the
number of registers a shader needs decides how many warps hide a texture fetch (occupancy), and a shader that needs more
than the driver gives it spills to memory. Without these numbers each change to the shader was a guess.

## Cause

MonoGame has no access to the driver's view of a compiled pipeline. Vulkan exposes it through
`VK_KHR_pipeline_executable_properties`: per executable (shader stage) of a pipeline, the driver's statistics (NVIDIA:
register count, binary size, stack size, local memory, inputs and outputs), when the pipeline was created with
`VK_PIPELINE_CREATE_CAPTURE_STATISTICS_BIT_KHR` and the device feature `pipelineExecutableInfo` is enabled.

## Change

**Runtime** (`src/vulkan/MGG_Vulkan.cpp`):
- With `MG_PIPELINE_STATISTICS=1` in the environment at device creation and the extension present, the extension and its
  feature are enabled and every graphics and compute pipeline is created with the capture flag (off by default: capturing
  may slow pipeline creation). The last bound graphics and compute pipelines are kept.
- `MGG_GraphicsDevice_GetPipelineStatistics(device, compute, buffer, capacity)`: the statistics of the last bound graphics
  (`compute` 0) or compute (1) pipeline as UTF-8 lines `executable<TAB>statistic<TAB>value`; returns the length, 0 before
  any pipeline, -1 when the statistics are off.
- Interface version 3 (`MGG_Etervale_RuntimeVersion`, issue 002).

**Framework:** `GraphicsDevice.GetPipelineStatistics(bool compute)` returns the list of `PipelineStatistic` (executable,
name, value), or null when off (`Graphics/GraphicsDevice.PipelineStatistics.cs`).

**Application:** a diagnostic option can set the variable and print, once per technique, the statistics of the passes
it wants to inspect.

## Test

GTX 1050 Ti, driver 580.178: the passes of a ray-marching effect report their statistics (the march's fragment shader:
`Register Count = 124`, `Stack Size = 1536`; a reconstruction pass 56 registers, a slice pass 40). They guided the
march's changes: a per-segment table lookup held 28 registers at the march's peak (124 → 96 once a per-ray term is
computed once per ray).
Without the variable the runtime creates its pipelines as before and the call returns -1.
