// Declarations of the material (Materials/Material.hlsli): MATERIAL_CONSTANTS inside the pixel cbuffer and
// MATERIAL_RESOURCES (t0 base colour, t1 second layer) at file scope. PristonMaterialAdapter fills them from a legacy
// .smd material at load time (PristonMaterialDescription).
#ifndef MATERIALS_MATERIAL_DECL_HLSLI
#define MATERIALS_MATERIAL_DECL_HLSLI

// ShadingModel: 0 lit (metallic-roughness), 1 emissive (self-illuminated or additive: the albedo is emitted radiance),
// 2 colour factor (multiplicative blends write the albedo itself as the blend factor).
// LayerMode: 0 none, 1 multiply the albedo (detail or light map), 2 add to the albedo, 3 sphere-map reflection added to
// the specular term (the legacy reflection-mapped shine).
#define MATERIAL_CONSTANTS \
    float4 BaseColorFactor;     /* linear tint, a = opacity */ \
    float Roughness;            /* perceptual roughness */ \
    float Metallic; \
    float Reflectance;          /* F0 of dielectrics */ \
    float AlphaCutoff;          /* masked: discard below this coverage; < 0 = no test */ \
    float ShadingModel; \
    float EmissiveStrength;     /* radiance scale of emissive albedo */ \
    float LayerMode; \
    float LayerStrength; \
    float3 RimColor;            /* selection highlight (gameplay colour add) as a view-dependent rim */ \
    float MaterialBlink;        /* 1 = multiply the emission by BlinkPulse */ \
    float Translucency;         /* share of the sun passing through a thin surface lit from behind (foliage) */ \
    float ActorReceiver;        /* 1 = a character or monster: self-illumination at the display level, no lamp shadows */ \
    float2 MaterialReserved; \
    float4 ActorTint;          /* actors: rgb signed light added by gameplay (1 = a white light), a opacity after the alpha test */

#define MATERIAL_RESOURCES \
    Texture2D<float4> BaseColorMap : register(t0); \
    SamplerState BaseColorSampler : register(s0) = sampler_state \
    { \
        Texture = (BaseColorMap); MinFilter = Anisotropic; MagFilter = Linear; MipFilter = Linear; MaxAnisotropy = 16; \
        AddressU = Wrap; AddressV = Wrap; \
    }; \
    Texture2D<float4> LayerMap : register(t1); \
    SamplerState LayerSampler : register(s1) = sampler_state \
    { \
        Texture = (LayerMap); MinFilter = Anisotropic; MagFilter = Linear; MipFilter = Linear; MaxAnisotropy = 16; \
        AddressU = Wrap; AddressV = Wrap; \
    };

#endif
