// Unlit textured geometry in the HDR scene (PristonUnlitEffect): lamp glows and original mesh effects. The texture is
// an sRGB format the sampler decodes; the vertex colour is decoded exactly here. Tint scales the result into radiance,
// so additive effects add light in linear space. VertexPositionColorTexture input.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"

cbuffer UnlitVertexConstants : register(b0)
{
    float4x4 WorldViewProjection;
};

cbuffer UnlitPixelConstants : register(b0)
{
    float4 Tint;              // rgb radiance scale (linear), a opacity
    float Exposure;           // the scene's exposure: the original effect colours are display-referred
    float DisplayOutput;      // 1: write display-referred sRGB into an 8-bit target; 0: HDR radiance
    float2 UnlitPadding;
};

Texture2D<float4> Texture : register(t0);
// No filter or address here: PristonUnlitEffect sets the owner's sampler state on slot 0 after the pass is applied.
SamplerState TextureSampler : register(s0) = sampler_state { Texture = (Texture); };

struct VSIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

struct VSOut
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

VSOut VS_Unlit(VSIn i)
{
    VSOut o;
    o.Position = mul(float4(i.Position.xyz, 1.0), WorldViewProjection);
    o.Color = float4(SrgbToLinear(i.Color.rgb), i.Color.a);
    o.Uv = i.Uv;
    return o;
}

float4 PS_Unlit(VSOut i) : SV_Target0
{
    float4 texel = Texture.Sample(TextureSampler, i.Uv);
    float3 radiance = min(texel.rgb * i.Color.rgb * Tint.rgb / Exposure, HdrCap);
    float alpha = texel.a * i.Color.a * Tint.a;
    if (DisplayOutput > 0.5)
    {
        return float4(DisplayEncode(radiance, Exposure), alpha);
    }

    return float4(radiance, alpha);
}

technique Unlit { pass P0 { VertexShader = compile vs_6_0 VS_Unlit(); PixelShader = compile ps_6_0 PS_Unlit(); } }
