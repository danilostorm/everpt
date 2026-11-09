# 014. A 32-bit float depth format

Parts: runtime, framework.

## Need

Etervale's scene drew with a 24-bit depth buffer, its near plane at 1 unit and the far plane at 20000. Depth precision
then falls with the square of the distance: at 200 units two surfaces closer than about 0.0024 units resolve to the same
depth. The original models layer faces closer than that (a tier-four Mechanician helmet carries two gold sheets 0.0017
apart; town floors carry decals on the ground), so they fought in the depth test, and the temporal jitter changed the
winner each frame: textures, gold trims and floor patches flickered.

A 32-bit float buffer with a reversed range (1 at the near plane, 0 at the far plane) puts the float format's densest
values at the distance, where the projection's are sparsest; precision is then nearly even from near to far (about
0.00004 units at 300) and the near plane can stay close to the camera.

## Cause

`DepthFormat` offered `Depth16`, `Depth24` and `Depth24Stencil8` only, and the runtime mapped the last two to
`VK_FORMAT_D24_UNORM_S8_UINT`.

## Change

**Framework** (`Graphics/States/DepthFormat.cs`, `Platform/Native/GraphicsDeviceManager.Native.cs`):
`DepthFormat.Depth32Float` (value 4), a depth buffer without stencil; the back-buffer request maps it to 32 depth bits
and no stencil.

**Runtime** (`src/include/api_enums.h`, `src/vulkan/MGG_Vulkan.cpp`): `MGDepthFormat::Depth32Float` = 4 maps to
`VK_FORMAT_D32_SFLOAT`, which every desktop Vulkan device supports as a depth attachment. The image aspect, view,
barrier and clear code already handled the format (`DetermineAspectMask`); a clear that asks for stencil on it clears
depth only. Interface version 5 (issue 002).

The reversed range itself is the caller's: a projection that maps the near plane to 1, depth cleared to 0 and
`CompareFunction.GreaterEqual` (Etervale's `PristonDepth`).

## Test

Etervale's regression runner checks the reversed projection (near plane 1, far plane 0, both planes recovered, x and y
unchanged). The shadow capture (`--capture-shadows`) of Ricarten with a tier-four Mechanician counts the pixels that
change between still frames before and after.
