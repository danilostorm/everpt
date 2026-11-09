// Colour encodings (linear Rec.709/sRGB primaries, D65).
#ifndef CORE_COLOR_HLSLI
#define CORE_COLOR_HLSLI

// Exact sRGB OETF (IEC 61966-2-1).
float3 LinearToSrgb(float3 c)
{
    c = saturate(c);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(max(c, 0.0031308), 1.0 / 2.4) - 0.055;
    return select(c <= 0.0031308, lo, hi);
}

// Exact sRGB decoding. The legacy textures and vertex colours are sRGB-encoded 8-bit values.
float3 SrgbToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 1.0e-6), 2.4);
    return select(c <= 0.04045, lo, hi);
}

// Khronos PBR Neutral (2024): identity below 0.76, a soft shoulder to 1 above it with a slight desaturation.
float3 PbrNeutral(float3 c)
{
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    float x = min(c.r, min(c.g, c.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    c -= offset;
    float peak = max(c.r, max(c.g, c.b));
    if (peak < startCompression)
        return c;
    const float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    c *= newPeak / peak;
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(c, newPeak.xxx, g);
}

// Scene radiance to the 8-bit sRGB display value: exposure, PBR Neutral, sRGB OETF.
float3 DisplayEncode(float3 radiance, float exposure)
{
    return LinearToSrgb(PbrNeutral(max(radiance * exposure, 0.0)));
}

#endif
