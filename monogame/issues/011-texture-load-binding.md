# 011. A texture read only with `Load` is bound

Part: effect processor (`../pipeline`).

## Symptom

A pixel shader summed 64 slices of an optical-depth texture with `Texture2D.Load`: it read zeros (the sun's disc
behind a dense volume came out with transmittance 1). The same texture read with `SampleLevel` at the texel centres
gave the right values.

## Cause

Two defects of the upstream code in `ShaderProfile.Vulkan.cs`, found one after the other by the repro:
- A texture that no sampler samples in a shader (one read with `.Load`) is registered as a sampler entry with no sampler
  slot (-1), so the effect binds it, except when any `sampler_state` of the effect names the texture
  (`Texture = (Name)`): that case was skipped on the assumption that the sampled path registers it. When the shader that
  loads the texture never samples it, nothing registered it: the effect had no parameter for it (or, when another shader
  samples it, a parameter not bound for this shader).
- The descriptor bindings of a sampler entry whose texture and sampler slots differ were written only for slots `> 0`: a
  texture loaded at t0 (or a sampler at s0 sampling another register) had no binding, and the shader read an unbound
  descriptor (zeros).

## Change

The skip is removed (a texture a shader reads with `.Load` is registered for that shader whether or not a `sampler_state`
names it), and the bindings are written for every slot `>= 0` (-1 is "no sampler").

## Test

`../runtime/repro/TextureLoadRepro`: a 7 × 5 RGBA32F texture copied by a pixel shader into a render target and read back
exactly, (1) with `Load` at t0 and a `sampler_state` naming the texture, (2) with `Load` at t1 and no `sampler_state`, (3)
with `SampleLevel` (control). Before the change (1) failed (no parameter for the texture); with only the skip removed it
read zeros (no binding at slot 0); with both changes all three pass, exactly. The shader of the symptom reads its
optical-depth slices with `Load` again and gets the right values.
