// Octahedral mapping of directions (Y up) to [0, 1]^2 (Cigolle et al. 2014): the upper hemisphere fills the inner
// diamond, the lower one the four corners.
#ifndef LIGHTING_OCTAHEDRAL_HLSLI
#define LIGHTING_OCTAHEDRAL_HLSLI

float2 SignNotZero(float2 v)
{
    return float2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

float2 OctahedralEncode(float3 d)
{
    d /= max(abs(d.x) + abs(d.y) + abs(d.z), 1.0e-6);
    float2 p = d.xz;
    if (d.y < 0.0)
    {
        p = (1.0 - abs(p.yx)) * SignNotZero(p);
    }

    return p * 0.5 + 0.5;
}

float3 OctahedralDecode(float2 uv)
{
    float2 p = uv * 2.0 - 1.0;
    float3 d = float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y);
    if (d.y < 0.0)
    {
        d.xz = (1.0 - abs(d.zx)) * SignNotZero(d.xz);
    }

    return normalize(d);
}

#endif
