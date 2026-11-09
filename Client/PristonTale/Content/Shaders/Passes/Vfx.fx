// Skill and combat effects in the HDR scene (PristonVfxEffect, Client/PristonTale.Effects/Vfx). Vertex colours are
// linear, already multiplied by the layer intensity, and relative to the scene exposure, so 1 displays the authored
// colour and higher values reach the bloom. Textures are sRGB formats the sampler decodes. With the depth pre-pass the
// sprites fade where they meet the scene (soft particles): the fade is the distance to the opaque surface behind, over
// Softness.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"

cbuffer VfxVertexConstants : register(b0)
{
    float4x4 ViewProjection;
};

cbuffer VfxPixelConstants : register(b0)
{
    float Exposure;           // the scene's exposure
    float DisplayOutput;      // 1: write display-referred sRGB into an 8-bit target; 0: HDR radiance
    float SoftParticles;      // 1 while DepthMap holds this frame's linear view depth
    float Mode;               // 0 radiance; 1 multiplier (darkening); 2 inverse multiplier; 3 radiance x colour
};

Texture2D<float4> Texture : register(t0);
SamplerState TextureSampler : register(s0) = sampler_state { Texture = (Texture); };
// R32F linear view depth of the opaque scene (sky: 60000 or more); read per pixel, no sampler.
Texture2D<float4> DepthMap : register(t1);

struct VSIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;       // linear rgb x intensity, opacity
    float2 Uv : TEXCOORD0;
    float2 Params : TEXCOORD1;   // softness in world units, unused
};

struct VSOut
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
    float2 Depth : TEXCOORD1;    // view depth, softness
};

VSOut VS_Vfx(VSIn i)
{
    VSOut o;
    o.Position = mul(float4(i.Position.xyz, 1.0), ViewProjection);
    o.Color = i.Color;
    o.Uv = i.Uv;
    o.Depth = float2(o.Position.w, i.Params.x);
    return o;
}

float4 PS_Vfx(VSOut i) : SV_Target0
{
    float4 texel = Texture.Sample(TextureSampler, i.Uv);
    float alpha = texel.a * i.Color.a;
    if (SoftParticles > 0.5 && i.Depth.y > 0.0)
    {
        float scene = DepthMap.Load(int3(i.Position.xy, 0)).r;
        alpha *= saturate((scene - i.Depth.x) / i.Depth.y);
    }

    float3 factor = saturate(texel.rgb * i.Color.rgb);
    if (Mode > 0.5 && Mode < 1.5)
    {
        // Destination x source: the soft-particle fade blends towards no change.
        float fade = alpha / max(texel.a * i.Color.a, 1e-4);
        return float4(lerp(float3(1.0, 1.0, 1.0), factor, saturate(fade)), 1.0);
    }

    if (Mode > 1.5 && Mode < 2.5)
    {
        float fade = alpha / max(texel.a * i.Color.a, 1e-4);
        return float4(factor * saturate(fade), 1.0);
    }

    float3 radiance = min(texel.rgb * i.Color.rgb / Exposure, HdrCap);
    if (Mode > 2.5)
    {
        // Source-colour weighted light: the colour factor scales the added radiance; opacity is not used.
        radiance *= factor;
        alpha = 1.0;
    }

    if (DisplayOutput > 0.5)
    {
        return float4(DisplayEncode(radiance, Exposure), alpha);
    }

    return float4(radiance, alpha);
}

technique Vfx { pass P0 { VertexShader = compile vs_6_0 VS_Vfx(); PixelShader = compile ps_6_0 PS_Vfx(); } }
