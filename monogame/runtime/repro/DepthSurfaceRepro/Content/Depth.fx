// DepthSurfaceRepro (monogame/issues/016): triangles drawn into a depth surface at a set depth, one of them cut by
// clip, and the surface read back by a pixel shader with Load and GatherRed into a float colour target.

cbuffer Constants : register(b0)
{
    float4 Params;   // x depth written, y 1 to clip the left half
};

Texture2D<float4> Surface : register(t0);
SamplerState SurfaceSampler : register(s0) = sampler_state { Texture = (Surface); MinFilter = Point; MagFilter = Point; MipFilter = None; AddressU = Clamp; AddressV = Clamp; };

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
    o.Position = float4(i.Position.xy, Params.x, 1.0);
    o.Uv = i.Uv;
    return o;
}

// The colour has no attachment to go to: the pass writes depth only.
float4 PS_Depth(VSOut i) : SV_Target0
{
    if (Params.y > 0.5)
        clip(i.Uv.x - 0.5);
    return 0.0;
}

float4 PS_Load(VSOut i) : SV_Target0
{
    return Surface.Load(int3(i.Position.xy, 0)).rrrr;
}

// The texel's own value through a gather at its bottom-right corner (the 2 x 2 footprint's top-left texel is .w).
float4 PS_Gather(VSOut i) : SV_Target0
{
    float2 size;
    Surface.GetDimensions(size.x, size.y);
    return Surface.GatherRed(SurfaceSampler, (floor(i.Position.xy) + 1.0) / size).wwww;
}

technique Depth { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Depth(); } }
technique Load { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Load(); } }
technique Gather { pass P0 { VertexShader = compile vs_6_0 VS_Full(); PixelShader = compile ps_6_0 PS_Gather(); } }
