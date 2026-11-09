// Full-screen passes: the renderer draws one oversized triangle (PristonFullscreenTriangle) whose vertices carry the
// clip-space position and the target uv, (0, 0) top-left and (1, 1) bottom-right.
#ifndef CORE_FULLSCREEN_HLSLI
#define CORE_FULLSCREEN_HLSLI

struct FullscreenIn
{
    float4 Position : POSITION0;
    float2 Uv : TEXCOORD0;
};

struct FullscreenOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

FullscreenOut VS_Fullscreen(FullscreenIn i)
{
    FullscreenOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    return o;
}

#endif
