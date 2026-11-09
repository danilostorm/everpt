// Declarations of the lighting inputs (Lighting/Shading.hlsli): LIGHTING_CONSTANTS inside the pixel cbuffer,
// LIGHTING_RESOURCES(slot, occlusionSlot, lampSlot) at file scope: the cascaded shadow atlas, the screen-space occlusion
// (ambient occlusion, contact shadows), the lamps' shadow atlas (six cube faces per lamp, one row per lamp) and the
// tiles' local-light masks (Passes/LightCull.fx); the image-based lighting: the prefiltered specular atlas, the
// irradiance as spherical harmonics (Passes/Environment.fx) and the split-sum BRDF table.
#ifndef LIGHTING_DECL_HLSLI
#define LIGHTING_DECL_HLSLI

// Local lights: the stage lamps and the transient combat lights nearest the camera focus (PristonRenderer selects
// them on the CPU).
#define MAX_LOCAL_LIGHTS 32
#define MAX_CASCADES 4

// The sun's cascades (Lighting/PristonShadowCascades.cs): world -> light clip per cascade, the tile of each in the
// atlas, the far view depth of each, the world size of one texel and the light-space depth range.
#define LIGHTING_CONSTANTS \
    float4x4 CascadeViewProjection[MAX_CASCADES]; \
    float4 CascadeAtlas[MAX_CASCADES];       /* xy offset, zw size of the tile (atlas uv) */ \
    float4 CascadeSplits;                    /* far view depth of each cascade */ \
    float4 CascadeTexelWorld;                /* world size of one shadow texel per cascade */ \
    float4 CascadeDepthRange;                /* light-space depth range per cascade (world units) */ \
    float4 ShadowParams;                     /* x cascade count, y filter (0 PCF, 1 PCSS), z tan of the light's radius, w strength (0 = off) */ \
    float4 ShadowTexel;                      /* xy 1 / atlas size, z PCF radius (texels), w 1 = screen-space occlusion bound */ \
    float4 OcclusionParams;                  /* xy 1 / occlusion size, z contact shadow strength, w ambient occlusion strength */ \
    float4 LampShadowParams;                 /* xy 1 / lamp shadow atlas size, z face size (px), w lamp rows */ \
    float4 TileParams;                       /* x tiles across, y 1 / tile size (px), z 1 = tile light masks bound */ \
    float4 EnvironmentParams;                /* x 1 = image-based lighting, y base size (px), z atlas width (px), w levels */ \
    float LocalLightCount; \
    float TransientLightCount;               /* the first lights are combat lights, radial and normal-independent */ \
    float2 NoiseParams;                      /* x frame offset of the per-pixel noise (0 without TAA) */ \
    float4 LocalLightPosition[MAX_LOCAL_LIGHTS]; /* xyz world position, w radius */ \
    float4 LocalLightColor[MAX_LOCAL_LIGHTS];    /* rgb colour * intensity, already scaled by the lamp factor; w lamp shadow row + 1 (0: none) */

#define LIGHTING_RESOURCES(slot, occlusionSlot, lampSlot, tileSlot, environmentSlot, shSlot, brdfSlot) \
    Texture2D<float4> ShadowMap : register(t##slot); \
    SamplerState ShadowSampler : register(s##slot) = sampler_state \
    { \
        Texture = (ShadowMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; \
    }; \
    Texture2D<float4> OcclusionMap : register(t##occlusionSlot); \
    SamplerState OcclusionSampler : register(s##occlusionSlot) = sampler_state \
    { \
        Texture = (OcclusionMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; \
    }; \
    StructuredBuffer<uint> TileLightMasks : register(t##tileSlot); \
    StructuredBuffer<float4> EnvironmentSh : register(t##shSlot); \
    Texture2D<float4> EnvironmentMap : register(t##environmentSlot); \
    SamplerState EnvironmentSampler : register(s##environmentSlot) = sampler_state \
    { \
        Texture = (EnvironmentMap); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; \
    }; \
    Texture2D<float4> BrdfLut : register(t##brdfSlot); \
    SamplerState BrdfLutSampler : register(s##brdfSlot) = sampler_state \
    { \
        Texture = (BrdfLut); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; \
    }; \
    Texture2D<float4> LampShadowMap : register(t##lampSlot); \
    SamplerState LampShadowSampler : register(s##lampSlot) = sampler_state \
    { \
        Texture = (LampShadowMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; \
    };

#endif
