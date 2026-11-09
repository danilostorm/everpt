using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Units;

namespace PristonTale.Engine3D.Rendering;

internal sealed class AnimatedModelRuntime
{
    // Animation timeline starts one authored frame in (160 ticks), matching the original
    // engine's stage-object loop which floors playback to frame 160.
    private const float BaseFrame = GameUnits.FrameTicks;

    public AnimatedModelRuntime(
        PristonModel model,
        PristonSkinPalette palette,
        int paletteOffset)
    {
        Model = model;
        Animator = new PristonModelAnimator(model);
        PaletteCount = Math.Min(Animator.ObjectCount, PristonShaderLibrary.MaxBones);
        if (paletteOffset < 0 || paletteOffset + PaletteCount > palette.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteOffset));
        }

        Palette = palette;
        PaletteOffset = paletteOffset;
        MaxFrame = model.LastFrame;

        foreach (var material in model.Materials)
        {
            if (material.InUse && material.IsSelfIllum)
            {
                IsGlowEmitter = true;
                break;
            }
        }
    }

    public PristonModel Model { get; }
    public PristonModelAnimator Animator { get; }
    public PristonSkinPalette Palette { get; }
    public int PaletteOffset { get; }
    public int PaletteCount { get; }
    public int MaxFrame { get; }
    public float Frame { get; private set; } = BaseFrame;
    public BoundingBox BindBounds { get; private set; }
    public BoundingBox WorldBounds => _worldBounds;
    private bool _hasBindBounds;
    private BoundingBox _worldBounds;
    private bool _hasWorldBounds;
    private float _paletteFrame = float.NaN;
    private Matrix _paletteWorld;

    public void IncludeBindBounds(BoundingBox bounds)
    {
        BindBounds = _hasBindBounds
            ? BoundingBox.CreateMerged(BindBounds, bounds)
            : bounds;
        _hasBindBounds = true;
    }

    public void SetWorldBounds(BoundingBox bounds)
    {
        _worldBounds = bounds;
        _hasWorldBounds = true;
    }

    public bool PaletteIsCurrent()
    {
        return _paletteFrame == Frame && _paletteWorld == World;
    }

    public void MarkPaletteCurrent()
    {
        _paletteFrame = Frame;
        _paletteWorld = World;
    }

    public Matrix World { get; init; } = Matrix.Identity;

    public bool IsGlowEmitter { get; }

    public Vector3 GlowColor { get; set; } = Vector3.One;

    public Vector3 WorldCenter()
    {
        if (!_hasWorldBounds)
        {
            return Vector3.Zero;
        }

        return (_worldBounds.Min + _worldBounds.Max) * 0.5f;
    }

    // Each prop wraps its own clock to avoid long-session float precision loss.
    public void Advance(float ticks)
    {
        if (MaxFrame <= BaseFrame)
        {
            Frame = BaseFrame;
            return;
        }

        Frame += ticks;
        if (Frame >= MaxFrame)
        {
            Frame = BaseFrame + (Frame - BaseFrame) % (MaxFrame - BaseFrame);
        }
    }

    // Opening props follow the absolute camera timeline and hold their last authored frame.
    public void SetFrame(float ticks)
    {
        Frame = MaxFrame <= BaseFrame ? BaseFrame : MathHelper.Clamp(ticks, BaseFrame, MaxFrame - 1);
    }
}

internal sealed class AnimatedStageGroupRuntime
{
    public AnimatedStageGroupRuntime(int boneCount)
    {
        Palette = new PristonSkinPalette(boneCount);
    }

    public PristonSkinPalette Palette { get; }
    public List<AnimatedModelRuntime> Models { get; } = [];
    public List<AnimatedStageBatchRuntime> Batches { get; } = [];
    public int ColorBatchCount { get; set; }
    public int ShadowAggregateCount { get; set; }
}

internal sealed record AnimatedStageBatchRuntime(
    RenderBatch Batch,
    AnimatedModelRuntime[] Models);
