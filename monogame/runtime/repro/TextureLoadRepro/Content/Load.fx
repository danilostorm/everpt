// TextureLoadRepro (monogame/issues/011): a texture a pixel shader reads only with Load, with and without a sampler_state
// that names it, and one it samples; each pass copies its texture into a render target the program reads back.

cbuffer Constants : register(b0)
{
    float4 Size;   // xy texture size
};

// Read only with Load, and named by a sampler_state that this shader never uses (the case cloud shaders hit).
Texture2D<float4> Named : register(t0);
SamplerState NamedSampler : register(s0) = sampler_state { Texture = (Named); MinFilter = Point; MagFilter = Point; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };

// Read only with Load, no sampler_state.
Texture2D<float4> Bare : register(t1);

// Sampled.
Texture2D<float4> Sampled : register(t2);
SamplerState SampledSampler : register(s2) = sampler_state { Texture = (Sampled); MinFilter = Point; MagFilter = Point; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };

struct VSIn
{
    float4 Position : POSITION0;
    float2 Uv : TEXCOORD0;
};

struct VSOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

VSOut VS_Full(VSIn i)
{
    VSOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    return o;
}

float4 PS_Named(VSOut i) : SV_Target0
{
    return Named.Load(int3(i.Position.xy, 0));
}

float4 PS_Bare(VSOut i) : SV_Target0
{
    return Bare.Load(int3(i.Position.xy, 0));
}

float4 PS_Sampled(VSOut i) : SV_Target0
{
    return Sampled.SampleLevel(SampledSampler, (floor(i.Position.xy) + 0.5) / Size.xy, 0);
}

technique LoadNamed { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Named(); } }
technique LoadBare { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Bare(); } }
technique Sample { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Sampled(); } }
