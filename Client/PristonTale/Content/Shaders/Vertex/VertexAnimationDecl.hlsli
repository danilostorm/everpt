// Declarations of the water ripple and wind displacement of static geometry (Vertex/VertexAnimation.hlsli), shared by
// the lit and the shadow-caster passes so displaced surfaces and their shadows stay aligned. VERTEX_ANIMATION_CONSTANTS
// goes in the vertex cbuffer, before VertexAnimation.hlsli is included. WindParams packs (amplitudeX, amplitudeZ,
// bottomY, inverseHeight); VertexAnimTime wraps on a period that is a whole number of cycles of every angular frequency
// of VertexAnimation.hlsli (PristonShaderLibrary.VertexAnimationPeriodSeconds).
#ifndef VERTEX_VERTEX_ANIMATION_DECL_HLSLI
#define VERTEX_VERTEX_ANIMATION_DECL_HLSLI

#define VERTEX_ANIMATION_CONSTANTS \
    float4 WindParams; \
    float WaterRipple; \
    float VertexAnimTime; \
    float2 VertexAnimationPadding;

#endif
