// The material model: metallic-roughness inputs from the adapted legacy textures. Base colour and the second layer are
// sRGB textures the sampler decodes before filtering; vertex colours arrive decoded. Requires MATERIAL_CONSTANTS and
// MATERIAL_RESOURCES.
#ifndef MATERIALS_MATERIAL_HLSLI
#define MATERIALS_MATERIAL_HLSLI

#include "../Core/Color.hlsli"

struct Surface
{
    float3 Albedo;      // linear base colour
    float Alpha;        // opacity (blended) or coverage (masked)
    float Roughness;    // perceptual
    float Metallic;
    float3 F0;
    float3 Normal;      // unit, facing the viewer
    float3 Reflection;  // legacy sphere-map shine, added to the specular radiance
};

// Base colour and coverage only (shadow casters, alpha test).
float4 SampleBaseColor(float2 uv, float4 vertexColour)
{
    float4 texel = BaseColorMap.Sample(BaseColorSampler, uv);
    return BaseColorFactor * float4(texel.rgb * vertexColour.rgb, texel.a * vertexColour.a);
}

void AlphaTest(float coverage)
{
    if (AlphaCutoff >= 0.0)
    {
        clip(coverage - AlphaCutoff);
    }
}

// The visible side of the triangle under the pixel, from the screen derivatives of the world position: independent of
// the winding and of the vertex normal. Evaluate it in uniform control flow.
float3 VisibleFaceNormal(float3 worldPosition, float3 view)
{
    float3 face = SafeNormalize(cross(ddx(worldPosition), ddy(worldPosition)));
    return dot(face, view) < 0.0 ? -face : face;
}

// Two-sided faces are seen from either side, so the side being seen is shaded. Only a vertex normal on the far side of
// the visible face flips; a smooth normal that merely leans away from the viewer near a silhouette is kept, or that edge
// would be lit as if it faced the opposite way.
float3 OrientNormal(float3 normal, float3 visibleFace)
{
    float3 n = SafeNormalize(normal);
    return dot(n, visibleFace) < 0.0 ? -n : n;
}

// uv0: base layer; layerUv: second layer (already the reflection uv for sphere-mapped layers); vertexColour linear;
// visibleFace from VisibleFaceNormal. Gradients of the layer are taken here, in uniform control flow, so the layer may
// be sampled under a branch.
Surface SampleSurface(float2 uv0, float2 layerUv, float4 vertexColour, float3 normal, float3 visibleFace)
{
    Surface s;
    float4 base = SampleBaseColor(uv0, vertexColour);
    float2 layerDx = ddx(layerUv);
    float2 layerDy = ddy(layerUv);
    s.Albedo = base.rgb;
    s.Alpha = base.a;
    s.Reflection = 0.0;
    [branch]
    if (LayerMode > 0.5)
    {
        float3 layer = LayerMap.SampleGrad(LayerSampler, layerUv, layerDx, layerDy).rgb * LayerStrength;
        if (LayerMode < 1.5)
        {
            s.Albedo *= layer;
        }
        else if (LayerMode < 2.5)
        {
            s.Albedo = saturate(s.Albedo + layer);
        }
        else
        {
            s.Reflection = layer;
        }
    }

    s.Normal = OrientNormal(normal, visibleFace);
    s.Roughness = Roughness;
    s.Metallic = Metallic;
    s.F0 = lerp(Reflectance.xxx, s.Albedo, Metallic);
    return s;
}

#endif
