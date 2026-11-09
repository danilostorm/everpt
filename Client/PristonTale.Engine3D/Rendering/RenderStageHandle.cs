using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public sealed class RenderStageHandle
{
    internal RenderStageHandle(
        PristonStage source,
        float lampIntensity,
        float lampRangeScale,
        PristonPointLight[] stageLights,
        PristonPointLight[] glowLights,
        float glowIntensity,
        float glowScale)
    {
        Source = source;
        LampIntensity = lampIntensity;
        LampRangeScale = lampRangeScale;
        StageLights = stageLights;
        GlowLights = glowLights;
        GlowIntensity = glowIntensity;
        GlowScale = glowScale;
    }

    internal PristonStage Source { get; }

    internal float LampIntensity { get; }
    internal float LampRangeScale { get; }

    internal float GlowIntensity { get; }
    internal float GlowScale { get; }

    internal PristonPointLight[] StageLights { get; }

    internal PristonPointLight[] GlowLights { get; }

    internal List<RenderBatch> Batches { get; } = [];

    internal List<RenderBatch> ShadowBatches { get; } = [];

    internal List<AnimatedModelRuntime> AnimatedModels { get; } = [];

    internal List<AnimatedStageGroupRuntime> AnimatedGroups { get; } = [];

    internal BoundingBox Bounds { get; set; }

    // The floors' authored light for the actors standing on them (null until built).
    internal Lighting.PristonStageLightField LightField { get; set; }

    internal void DisposeGpu()
    {
        foreach (var batch in Batches)
        {
            batch.Mesh?.Dispose();
            batch.DynamicBuffer?.Dispose();
            batch.SkinnedBuffer?.Dispose();
            batch.SkinnedIndexBuffer?.Dispose();
        }

        foreach (var batch in ShadowBatches)
        {
            batch.Mesh?.Dispose();
            batch.DynamicBuffer?.Dispose();
            batch.SkinnedBuffer?.Dispose();
            batch.SkinnedIndexBuffer?.Dispose();
        }
    }
}
