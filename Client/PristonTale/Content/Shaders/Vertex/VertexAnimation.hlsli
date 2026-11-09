// Water ripple and wind displacement over VERTEX_ANIMATION_CONSTANTS (Vertex/VertexAnimationDecl.hlsli).
#ifndef VERTEX_VERTEX_ANIMATION_HLSLI
#define VERTEX_VERTEX_ANIMATION_HLSLI

static const float WaterRippleAmplitude = 9.0;

float3 VertexAnimOffsetAt(float3 worldPosition, float t)
{
    float3 offset = 0.0;
    [branch]
    if (WaterRipple != 0.0)
    {
        float dx = sin(worldPosition.x * 0.025 + worldPosition.z * 0.013 + t * 1.7);
        float dz = cos(worldPosition.z * 0.025 + worldPosition.x * 0.013 + t * 1.9);
        offset += float3(dx, 0.0, dz) * (WaterRippleAmplitude * WaterRipple);
    }

    [branch]
    if (WindParams.x != 0.0 || WindParams.y != 0.0)
    {
        // Anchored at the mesh bottom: weight 0 there, 1 at the top. Slow, spatially coherent sway.
        float weight = saturate((worldPosition.y - WindParams.z) * WindParams.w);
        float sx = sin(t * 0.45 + worldPosition.z * 0.006);
        float sz = sin(t * 0.50 + worldPosition.x * 0.006);
        offset += float3(WindParams.x * sx, 0.0, WindParams.y * sz) * weight;
    }

    return offset;
}

float3 VertexAnimOffset(float3 worldPosition)
{
    return VertexAnimOffsetAt(worldPosition, VertexAnimTime);
}

#endif
