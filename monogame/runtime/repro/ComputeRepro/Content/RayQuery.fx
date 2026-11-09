// A cs_6_5 ray query (SM 6.5 profile, SPIR-V 1.5 with SPV_KHR_ray_query): the processor compiles it and records the
// requirement; the framework refuses to load it until the runtime supports acceleration structures.
RaytracingAccelerationStructure Scene : register(t0);
[[vk::image_format("r32f")]] RWTexture2D<float> Hit : register(u0);

[numthreads(8, 8, 1)]
void CS_Trace(uint3 id : SV_DispatchThreadID)
{
    RayDesc ray;
    ray.Origin = float3(id.xy, 0.0);
    ray.Direction = float3(0.0, 0.0, 1.0);
    ray.TMin = 0.0;
    ray.TMax = 1000.0;
    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();
    Hit[id.xy] = q.CommittedStatus() == COMMITTED_TRIANGLE_HIT ? q.CommittedRayT() : -1.0;
}

technique Trace { pass P0 { ComputeShader = compile cs_6_5 CS_Trace(); } }
