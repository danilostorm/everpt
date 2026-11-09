// Declarations of the distance fog (Atmosphere/Fog.hlsli): FOG_CONSTANTS inside the pixel cbuffer, FOG_RESOURCES(slot) at
// file scope with the sky capture on register slot.
#ifndef ATMOSPHERE_FOG_DECL_HLSLI
#define ATMOSPHERE_FOG_DECL_HLSLI

// FogParams: x = enabled (0/1), y = start distance, z = density per unit, w = maximum opacity.
// FogSkyBand: x/y = sampled tangent range, z = elevation scale, w = capture lower tangent; FogSkyBand2.x =
// 1 / (upper - lower capture tangent), y = 1 when FogSky holds a capture. FogPhaseK = Schlick phase k of the sun.
#define FOG_CONSTANTS \
    float4 FogParams; \
    float4 FogSkyBand; \
    float4 FogSkyBand2; \
    float3 FogColor;            /* flat HDR colour used without a sky capture */ \
    float FogPhaseK; \
    float3 FogInScatter;        /* sun in-scatter radiance, already gated by the sun's elevation */ \
    float FogPadding;

#define FOG_RESOURCES(slot) \
    Texture2D<float4> FogSky : register(t##slot); \
    SamplerState FogSkySampler : register(s##slot) = sampler_state \
    { \
        Texture = (FogSky); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Wrap; AddressV = Clamp; \
    };

#endif
