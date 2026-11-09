using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class PlayerTimingProbe
{
    public static void Run(string root)
    {
        var classes = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            var path = classes.Get(playerClass).Definition.DefaultVisuals.Body.InxPath;
            var actor = loader.Load(root, Path.Combine(root, path));
            foreach (var state in new[] { MotionCode.Attack, MotionCode.Damage, MotionCode.Eat })
            {
                var authored = actor.Info.Motions.Skip(PristonFormatConstants.CharacterMotionBase)
                    .First(motion => (MotionCode)motion.State == state && motion.EndFrame > motion.StartFrame);
                var fallback = PlayerMotionFallback.GetClip(playerClass, state);
                Assert.True(fallback.Frames == authored.EndFrame - authored.StartFrame && fallback.Event0 == authored.EventFrames[0],
                    $"{playerClass}/{state:X}: fallback differs from {actor.Info.Path}");
            }

            Console.WriteLine($"{playerClass}: Attack / Damage / EAT match {actor.Info.Path}");
        }
    }
}
