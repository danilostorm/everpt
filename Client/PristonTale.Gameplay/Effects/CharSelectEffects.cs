using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// The green flames over the character-select braziers: nine a second rise at 60 units a second within 0.06 rad of the
// vertical from 4 units around the brazier, the 8-frame flame flipbook at 9 frames a second, growing from 24 to 34
// units across and fading out over their 1.8 to 2.4 s. Sizes are full extents.
public static class CharSelectEffects
{
    public const string GreenBeam = "vfx.charselect.beam";
    // Seconds of flames one beam effect emits; CharSelectFlames starts the next as it ends, so the flames never stop.
    public const float BeamPeriod = 2f;

    private const float Rise = 60f;
    private const float Lean = Rise * 0.06f;
    private const float Rate = 9f;

    public static VfxDefinition GreenBeamDefinition() => new()
    {
        Name = GreenBeam,
        Duration = BeamPeriod + 2.4f,
        Parts =
        [
            new VfxPartDefinition
            {
                Count = (int)(BeamPeriod * Rate),
                DelayStep = 1f / Rate,
                Lifetime = [1.8f, 2.4f],
                Texture = "image/flame.bmp",
                Frames = 8,
                FrameTime = 1f / Rate,
                Loop = true,
                Color = [80f, 235f, 110f, 115f],
                Size = [24f, 24f],
                Growth = 10f / 2.1f,
                Fade = VfxPartFade.InOut,
                FadeOut = 2.4f,
                Shell = [4f, 4f, 4f],
                VelocityMin = [-Lean, Rise, -Lean],
                VelocityMax = [Lean, Rise, Lean]
            }
        ]
    };
}

// Keeps the flames burning: a beam effect at every brazier each BeamPeriod seconds, the first on the first update.
public sealed class CharSelectFlames(VfxSystem system, CompiledVfx beam, IReadOnlyList<Vector3> braziers)
{
    private float _clock = CharSelectEffects.BeamPeriod;

    public void Update(float seconds)
    {
        system.Update(seconds);
        _clock += seconds;
        while (_clock >= CharSelectEffects.BeamPeriod)
        {
            _clock -= CharSelectEffects.BeamPeriod;
            foreach (var brazier in braziers)
            {
                system.Spawn(beam, new VfxSpawn(0, 0, brazier, 0f));
            }
        }
    }
}
