// Rigid skinning functions over BonePalette and PreviousBonePalette (Vertex/SkinningDecl.hlsli).
#ifndef VERTEX_SKINNING_HLSLI
#define VERTEX_SKINNING_HLSLI

// Row dot products reconstruct (position * boneMatrix).xyz including the translation.
float3 SkinPosition(float4 position, uint bone)
{
    uint b = bone * 3;
    return float3(dot(BonePalette[b], position), dot(BonePalette[b + 1], position), dot(BonePalette[b + 2], position));
}

float3 PreviousSkinPosition(float4 position, uint bone)
{
    uint b = bone * 3;
    return float3(dot(PreviousBonePalette[b], position), dot(PreviousBonePalette[b + 1], position),
                  dot(PreviousBonePalette[b + 2], position));
}

// The rotation part only: the legacy bones have uniform scale, so no inverse transpose is needed.
float3 SkinNormal(float3 normal, uint bone)
{
    uint b = bone * 3;
    return float3(dot(BonePalette[b].xyz, normal), dot(BonePalette[b + 1].xyz, normal), dot(BonePalette[b + 2].xyz, normal));
}

#endif
