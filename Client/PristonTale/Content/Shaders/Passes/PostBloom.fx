// Bloom as the point-spread function of the lens (PristonPostProcessor): a dual-filter pyramid without threshold
// (Bjorge 2015, "Bandwidth-Efficient Rendering"; Jimenez 2014, "Next Generation Post Processing in Call of Duty").
// The first downsample weights its taps by 1 / (1 + luma) (Karis 2014) so single bright pixels do not flicker.
// Upsampling blends each level into the next larger one as a convex combination (dst = (1 - w) dst + w tent(src)), so
// the top level is a weighted average of every blur radius: bounded and energy-preserving.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

cbuffer PostBloomConstants : register(b0)
{
    float2 SourceTexel;   // 1 / source size
    float Weight;         // upsample: weight of the wider level in the convex combination
    float BloomPadding;
};

Texture2D<float4> Source : register(t0);
SamplerState SourceSampler : register(s0) = sampler_state
{
    Texture = (Source); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

float3 Tap(float2 uv)
{
    return min(Source.SampleLevel(SourceSampler, uv, 0).rgb, HdrCap);
}

// Five bilinear taps (centre x4 and the diagonal corners): a 4x4 texel footprint around the destination pixel.
float4 PS_Downsample(FullscreenOut i) : SV_Target0
{
    float3 c = Tap(i.Uv) * 4.0;
    c += Tap(i.Uv + SourceTexel * float2(-1, -1));
    c += Tap(i.Uv + SourceTexel * float2(1, -1));
    c += Tap(i.Uv + SourceTexel * float2(-1, 1));
    c += Tap(i.Uv + SourceTexel * float2(1, 1));
    return float4(c / 8.0, 1.0);
}

float4 PS_DownsampleKaris(FullscreenOut i) : SV_Target0
{
    static const float2 offsets[5] = { float2(0, 0), float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };
    float3 sum = 0.0;
    float weights = 0.0;
    [unroll]
    for (uint k = 0; k < 5; k++)
    {
        float3 c = Tap(i.Uv + SourceTexel * offsets[k]);
        float w = (k == 0 ? 4.0 : 1.0) / (1.0 + Luminance(c));
        sum += c * w;
        weights += w;
    }

    return float4(sum / weights, 1.0);
}

// Eight-tap tent from the smaller level, alpha-blended into the larger one with alpha = Weight.
float4 PS_Upsample(FullscreenOut i) : SV_Target0
{
    float2 h = SourceTexel;
    float3 c = Tap(i.Uv + float2(-2.0 * h.x, 0.0));
    c += Tap(i.Uv + float2(-h.x, h.y)) * 2.0;
    c += Tap(i.Uv + float2(0.0, 2.0 * h.y));
    c += Tap(i.Uv + float2(h.x, h.y)) * 2.0;
    c += Tap(i.Uv + float2(2.0 * h.x, 0.0));
    c += Tap(i.Uv + float2(h.x, -h.y)) * 2.0;
    c += Tap(i.Uv + float2(0.0, -2.0 * h.y));
    c += Tap(i.Uv + float2(-h.x, -h.y)) * 2.0;
    return float4(c / 12.0, Weight);
}

technique DownsampleKaris
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_DownsampleKaris(); }
}

technique Downsample
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Downsample(); }
}

technique Upsample
{
    pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Upsample(); }
}
