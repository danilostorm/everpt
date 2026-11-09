// Declarations of the rigid skinning (Vertex/Skinning.hlsli): one bone per vertex (the legacy physique), each affine
// bone stored as three transposed rows of its matrix (PristonSkinPalette.ShaderRows; C# uploads columns 1-3).
// PreviousBonePalette holds the previous frame's pose for the motion vectors (PristonSkinPalette.PreviousRows).
// SKINNING_CONSTANTS goes in the vertex cbuffer, before Skinning.hlsli is included.
#ifndef VERTEX_SKINNING_DECL_HLSLI
#define VERTEX_SKINNING_DECL_HLSLI

#define MAX_BONES 64

#define SKINNING_CONSTANTS \
    float4 BonePalette[MAX_BONES * 3]; \
    float4 PreviousBonePalette[MAX_BONES * 3];

#endif
