// The effects of ComputeRepro (Program.cs lists the checks): one technique per check, compiled by the fork's effect
// processor (monogame/pipeline, MGFX 12). Every value written is an integer below 2^24 or a bit pattern, so the program
// compares exactly.
cbuffer Params : register(b0)
{
    float4 Value;             // per dispatch, as each technique says
};

[[vk::image_format("rgba32f")]] RWTexture2D<float4> Image : register(u0);
RWStructuredBuffer<uint2> Pairs : register(u1);
RWByteAddressBuffer Raw : register(u2);
RWStructuredBuffer<uint> Args : register(u3);
RWByteAddressBuffer Counts : register(u4);
RWByteAddressBuffer Sum : register(u5);
StructuredBuffer<uint> Input : register(t0);
Texture2D<float4> Sampled : register(t1);
SamplerState SampledSampler : register(s1) = sampler_state { Texture = (Sampled); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };

uint Hash(uint x, uint y)
{
    uint h = (x * 73856093u) ^ (y * 19349663u);
    h ^= h >> 13;
    h *= 0x5bd1e995u;
    return h ^ (h >> 15);
}

// Image: Value.xy = size of the mip written, z = salt. (x, y, (hash & 0xFFFF) + salt, 1).
[numthreads(8, 8, 1)]
void CS_Image(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Value.x || id.y >= (uint)Value.y)
        return;
    Image[id.xy] = float4(id.x, id.y, (float)(Hash(id.x, id.y) & 0xFFFFu) + Value.z, 1.0);
}

// Buffers: Value.x = elements. Pairs[i] = (i, i²), Raw word i = i ^ 0xA5A5A5A5.
[numthreads(64, 1, 1)]
void CS_Buffers(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Value.x)
        return;
    Pairs[id.x] = uint2(id.x, id.x * id.x);
    Raw.Store(id.x * 4u, id.x ^ 0xA5A5A5A5u);
}

// Indirect, step 1: the group counts (Value.x, Value.y, 1) of the next dispatch.
[numthreads(1, 1, 1)]
void CS_Args(uint3 id : SV_DispatchThreadID)
{
    Args[0] = (uint)Value.x;
    Args[1] = (uint)Value.y;
    Args[2] = 1u;
}

// Indirect, step 2: each group counts itself once at word (group.y · 64 + group.x).
[numthreads(4, 4, 1)]
void CS_Count(uint3 group : SV_GroupID, uint index : SV_GroupIndex)
{
    if (index != 0u)
        return;
    uint previous;
    Counts.InterlockedAdd((group.y * 64u + group.x) * 4u, 1u, previous);
}

// Ordering: Pairs[Value.z + i] = (Input[i], Value.y) for i < Value.x.
[numthreads(64, 1, 1)]
void CS_Copy(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Value.x)
        return;
    Pairs[(uint)Value.z + id.x] = uint2(Input[id.x], (uint)Value.y);
}

// Frames in flight: adds Value.x to word 0.
[numthreads(1, 1, 1)]
void CS_Sum(uint3 id : SV_DispatchThreadID)
{
    uint previous;
    Sum.InterlockedAdd(0u, (uint)Value.x, previous);
}

struct VsIn
{
    float4 Position : POSITION0;
    float2 Uv : TEXCOORD0;
};

struct VsOut
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

VsOut VS_Quad(VsIn i)
{
    VsOut o;
    o.Position = float4(i.Position.xy, 0.0, 1.0);
    o.Uv = i.Uv;
    return o;
}

// Sample: the image written by CS_Image, at the pixel (Value.xy = size).
float4 PS_Sample(VsOut i) : SV_Target0
{
    return Sampled.SampleLevel(SampledSampler, i.Position.xy / Value.xy, 0);
}

technique Image { pass P0 { ComputeShader = compile cs_6_0 CS_Image(); } }
technique Buffers { pass P0 { ComputeShader = compile cs_6_0 CS_Buffers(); } }
technique Args { pass P0 { ComputeShader = compile cs_6_0 CS_Args(); } }
technique Count { pass P0 { ComputeShader = compile cs_6_0 CS_Count(); } }
technique Copy { pass P0 { ComputeShader = compile cs_6_0 CS_Copy(); } }
technique SumUp { pass P0 { ComputeShader = compile cs_6_0 CS_Sum(); } }
technique Sample { pass P0 { VertexShader = compile vs_6_0 VS_Quad(); PixelShader = compile ps_6_0 PS_Sample(); } }
