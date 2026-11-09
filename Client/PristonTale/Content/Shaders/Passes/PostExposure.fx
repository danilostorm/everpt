// Automatic exposure on the GPU without read-back (PristonAutoExposure), two compute passes per frame:
//  - Histogram: the log2 luminance (cd/m^2) of every 2x2 block of the HDR scene, centre-weighted, into 128 bins from
//    2^-12 to 2^20 (a quarter stop each): per-group bins in shared memory, then added to the frame's histogram;
//  - Adapt: one group reads the histogram (and clears it for the next frame), averages the bins between the
//    LowPercentile and HighPercentile of the weight (the darkest corners and the lamps and sky highlights do not drive
//    it), blends that reflected reading (K = 12.5) with the light model's incident reading, and moves the adapted EV100
//    toward the target with asymmetric time constants (light adaptation fast, dark adaptation slow). The light model's
//    own change since the last frame (the hour of a compressed day, a dusk falling several stops a second) is applied at
//    once; only the scene's departure from the meter adapts, or the picture sinks at dusk and recovers later. It stores the
//    display multiplier of the final pass, compensation included (Lighting/PristonPhotometry.cs CompensationStops).
// State[0] = (adapted EV100, display multiplier of the pre-exposed scene, reflected EV100, 1 once written).
#include "../Core/Common.hlsli"

cbuffer PostExposureConstants : register(b0)
{
    float4 SourceSize;     // xy source size (px), zw 1 / size
    float4 Meter;          // x 1 / pre-exposure, y incident EV100, z incident weight, w seconds since the last frame
    float4 Adaptation;     // x time constant toward brighter (s), y toward darker (s), z 1 = snap, w fixed EV100 (> -100)
    float4 Compensation;   // x user stops, y day key stops, z 1 = underground (no night curve), w pre-exposure
    float4 NightCurve;     // x start EV100 of the dimming, y its maximum stops, z its scale (EV), w aspect ratio
    float4 PreviousMeter;  // x incident EV100 of the previous frame
};

Texture2D<float4> Source : register(t0);
SamplerState SourceSampler : register(s0) = sampler_state
{
    Texture = (Source); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

RWByteAddressBuffer Histogram : register(u0);
RWStructuredBuffer<float4> State : register(u1);

#define BINS 128
static const float MinLog2 = -12.0;
static const float MaxLog2 = 20.0;
static const float LowPercentile = 0.45;
static const float HighPercentile = 0.95;
static const float MinEv = -4.0;
static const float MaxEv = 17.0;
static const float WeightScale = 64.0;

groupshared uint Bins[BINS];

[numthreads(16, 16, 1)]
void CS_Histogram(uint3 id : SV_DispatchThreadID, uint index : SV_GroupIndex)
{
    if (index < BINS)
    {
        Bins[index] = 0;
    }

    GroupMemoryBarrierWithGroupSync();
    uint2 blocks = uint2(SourceSize.xy) / 2;
    if (id.x < blocks.x && id.y < blocks.y)
    {
        // The bilinear tap at the shared corner of a 2x2 block averages its four pixels.
        float2 uv = (float2(id.xy) * 2.0 + 1.0) * SourceSize.zw;
        float3 c = clamp(Source.SampleLevel(SourceSampler, uv, 0).rgb, 0.0, HdrCap);
        float logLuminance = log2(max(Luminance(c) * Meter.x, 1.0e-6));
        float t = saturate((logLuminance - MinLog2) / (MaxLog2 - MinLog2));
        uint bin = min((uint)(t * BINS), BINS - 1);
        float2 d = (uv - 0.5) * float2(NightCurve.w, 1.0);
        float weight = exp(-dot(d, d) / (2.0 * 0.35 * 0.35));
        InterlockedAdd(Bins[bin], (uint)(weight * WeightScale + 0.5));
    }

    GroupMemoryBarrierWithGroupSync();
    if (index < BINS && Bins[index] != 0)
    {
        uint unused;
        Histogram.InterlockedAdd(index * 4, Bins[index], unused);
    }
}

float CompensationStops(float ev)
{
    float dim = Compensation.z > 0.5 ? 0.0 : NightCurve.y * (1.0 - exp(-max(NightCurve.x - ev, 0.0) / NightCurve.z));
    return Compensation.x + Compensation.y - dim;
}

[numthreads(BINS, 1, 1)]
void CS_Adapt(uint index : SV_GroupIndex)
{
    Bins[index] = Histogram.Load(index * 4);
    Histogram.Store(index * 4, 0);
    GroupMemoryBarrierWithGroupSync();
    if (index != 0)
    {
        return;
    }

    float total = 0.0;
    for (uint k = 0; k < BINS; k++)
    {
        total += (float)Bins[k];
    }

    float low = total * LowPercentile;
    float high = total * HighPercentile;
    float cumulative = 0.0;
    float sum = 0.0;
    float count = 0.0;
    for (uint b = 0; b < BINS; b++)
    {
        float n = (float)Bins[b];
        float take = clamp(cumulative + n, low, high) - clamp(cumulative, low, high);
        float centre = MinLog2 + (b + 0.5) * (MaxLog2 - MinLog2) / BINS;
        sum += take * centre;
        count += take;
        cumulative += n;
    }

    float reflected = count > 0.0 ? sum / count + log2(100.0 / 12.5) : Meter.y;
    float target = clamp(lerp(reflected, Meter.y, Meter.z), MinEv, MaxEv);
    float4 previousState = State[0];
    bool snap = Adaptation.z > 0.5 || previousState.w < 0.5;
    float previous = previousState.x + (Meter.y - PreviousMeter.x);
    float tau = target > previous ? Adaptation.x : Adaptation.y;
    float k = snap ? 1.0 : 1.0 - exp(-Meter.w / max(tau, 1.0e-3));
    float ev = Adaptation.w > -100.0 ? Adaptation.w : previous + (target - previous) * k;
    float multiplier = exp2(CompensationStops(ev)) * (PI * 0.4) / (exp2(ev) * Compensation.w);
    State[0] = float4(ev, multiplier, reflected, 1.0);
}

technique Histogram { pass P0 { ComputeShader = compile cs_6_0 CS_Histogram(); } }
technique Adapt { pass P0 { ComputeShader = compile cs_6_0 CS_Adapt(); } }
