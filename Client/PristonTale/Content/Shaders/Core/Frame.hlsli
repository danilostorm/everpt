// The frame block: per-frame constants of the lit scene effects, uploaded by PristonShaderLibrary.ApplyFrame. Light
// values are pre-exposed: radiance times the frame's pre-exposure (Lighting/PristonPhotometry.cs), so a white surface
// under the metered light is near 1 at every hour. It comes first in the effect's pixel cbuffer so it has the same
// layout in every effect, ready to become a cbuffer of its own when MonoGame's Vulkan effects support several (MonoGame
// issue #9083).
#ifndef CORE_FRAME_HLSLI
#define CORE_FRAME_HLSLI

#define FRAME_CONSTANTS \
    float3 CameraPosition;  /* world position of the eye */ \
    float Exposure;         /* nominal display scale: display-referred inputs are divided by it (PristonFrameContext) */ \
    float3 SunDirection;    /* unit vector toward the sun (or the moon at night) */ \
    float FrameTime;        /* seconds, wrapped on PristonShaderLibrary.VertexAnimationPeriodSeconds */ \
    float3 SunColor;        /* radiance of a white Lambertian surface facing the sun, shadowing excluded */ \
    float DisplayOutput;    /* 1: write display-referred sRGB (portraits into 8-bit targets); 0: HDR radiance */ \
    float3 SkyAmbient;      /* ambient radiance of upward-facing surfaces */ \
    float DebugView;        /* PristonDebugView ordinal; 0 = shaded */ \
    float3 GroundAmbient;   /* ambient radiance of downward-facing surfaces */ \
    float BlinkPulse;       /* the global [0.35, 1] pulse of blinking materials */ \
    float EmissionScale;    /* radiance of emissive albedo 1: at least 1 / Exposure, brighter at night */ \
    float3 FrameReserved;

#endif
