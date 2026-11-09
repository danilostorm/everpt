using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Tests;

internal static class MonsterSkillTimingProbe
{
    public static void Run(string root)
    {
        var models = new (int Sound, string Path, int Frames, uint Event)[]
        {
            (0x1840, "char/monster/Darkphalanx/darkphalanx.inx", 80, 6720),
            (0x1890, "char/monster/Darkguard/skullac.inx", 65, 4000),
            (0x18A0, "char/monster/Darkmage/skullma.inx", 60, 4640),
            (0x18E0, "char/monster/MonKingspider/ks.inx", 50, 3840),
            (0x19F0, "char/monster/Hest/Hest.inx", 69, 6240)
        };
        var loader = new PristonActorLoader();
        foreach (var model in models)
        {
            var path = AssetFiles.Resolve(Path.Combine(root, model.Path));
            var actor = loader.Load(root, path);
            var motion = actor.Info.Motions.Skip(PristonFormatConstants.CharacterMotionBase)
                .First(value => (MotionCode)value.State == MotionCode.Skill && value.EndFrame > value.StartFrame);
            var clip = new MotionClip(checked((int)motion.StartFrame), checked((int)motion.EndFrame),
                motion.EventFrames[0], motion.EventFrames[1], motion.EventFrames[2], motion.EventFrames[3], motion.Repeat != 0);
            Assert.True(clip.Frames == model.Frames && clip.Event0 == model.Event, $"unexpected source clip in {model.Path}");
            var tuning = new MonsterAiTuning(MonsterSkillTests.Definition(model.Sound), MotionTimingSet.Build([new((int)MotionCode.Skill, clip)]));
            Assert.True(tuning.CanCastOffensiveSkill, $"real model could not cast: {model.Path}");
            Assert.Near(MotionTiming.EventOffsetMs(model.Event, 1), tuning.SkillWindupMs);
            Console.WriteLine($"{model.Path}: SKILL impact={tuning.SkillWindupMs:F1} ms, duration={tuning.SkillDurationMs:F1} ms");
        }
    }
}
