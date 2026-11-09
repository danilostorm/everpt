# 008. Shader model 6.5 profiles and ray queries

Parts: effect processor, framework. The runtime side (acceleration structures, `VK_KHR_ray_query`) is not done yet.

## Need

A hardware ray-tracing tier traces rays from compute and pixel shaders with inline ray queries (DXR 1.1 `RayQuery`,
`TraceRayInline`) against an acceleration structure (`RaytracingAccelerationStructure`). In HLSL they need shader model
6.5; in Vulkan, `SPV_KHR_ray_query` in SPIR-V 1.4 or later (`VK_KHR_ray_query` depends on `VK_KHR_spirv_1_4`). The
profiles and their compilation come first, so the shaders can be built and tested before the runtime binds acceleration
structures.

## Cause

The processor compiled every shader with `-T vs_6_0`/`ps_6_0` for DXC's default Vulkan 1.0 environment (SPIR-V 1.0).
DXC 1.9 (the one in `dotnet-mgcb` 3.8.6) does not refuse `RayQuery` at `cs_6_0`: it emits `OpCapability RayQueryKHR`
in SPIR-V 1.0 (measured on `RayQuery.fx` of `../runtime/repro/ComputeRepro`), a module no Vulkan driver should accept.

## Change

**Processor** (`ShaderProfile.Vulkan.cs`):
- the models `vs_6_5`, `ps_6_5` and `cs_6_5` next to the `_6_0` ones;
- a `_6_5` model adds `-fspv-target-env=vulkan1.2` to DXC's arguments (SPIR-V 1.5); the `_6_0` arguments are
  upstream's, plus the u-register shift of issue 005;
- `RaytracingAccelerationStructure` is a storage resource of kind `AccelerationStructure` on its t register (descriptor
  `ACCELERATION_STRUCTURE_KHR` in the binding header); reflection reads `OpTypeAccelerationStructureKHR`;
- a shader with the `RayQueryKHR` capability or an acceleration structure records the requirement `RayQuery`
  (MGFX 12, issue 007);
- the same in a `_6_0` model is a compile error ("ray queries and acceleration structures need a shader model 6.5
  profile").

**Framework** (`Graphics/Shader/Shader.cs`): `ShaderRequirements.RayQuery`. Until the runtime enables ray queries and
binds acceleration structures, loading such a shader throws `NotSupportedException` that says so, instead of a device
error when its pipeline is created.

## Test

- An effect compiler test: a `cs_6_5` compute shader with
  `RayQuery<RAY_FLAG_FORCE_OPAQUE>` and `TraceRayInline` compiles. Its SPIR-V is version 1.5 with
  `OpCapability RayQueryKHR`, the acceleration structure is recorded on t0, and the requirement is set. The same source at
  `cs_6_0` is refused with the message above.
- Another checks the exact DXC arguments of `vs_6_0`, `ps_6_0` and `cs_6_5`.
- `../runtime/repro/ComputeRepro`, check *ray query*: `Content.Load<Effect>("RayQuery")` throws
  `NotSupportedException`.
