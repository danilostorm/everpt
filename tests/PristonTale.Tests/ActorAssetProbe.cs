using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ActorAssetProbe
{
    public static void Run(string root, string path)
    {
        var actor = new PristonActorLoader().Load(root, path);
        var animator = new PristonModelAnimator(actor.Skeleton);
        var lookup = actor.Skeleton.Objects.Select((bone, index) => (bone.Name, index)).ToDictionary(pair => pair.Name, pair => pair.index,
            StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"ACTOR {path}; mesh={actor.Mesh.Path}; skeleton={actor.Skeleton.Path}");
        Console.WriteLine($"Texture material slots: {actor.Mesh.Materials.Count} catalog / {PristonMaterialUsage.ForActor(actor).Count()} selected");
        foreach (var frame in new[] { 0f, 1600f, 2400f, 3200f, 5000f })
        {
            var transforms = animator.Evaluate(frame);
            var minimum = new Vector3(float.MaxValue);
            var maximum = new Vector3(float.MinValue);
            var missing = 0;
            foreach (var obj in actor.Mesh.Objects)
            {
                if (actor.RenderObjectNames.Count > 0 && !actor.RenderObjectNames.Contains(obj.Name))
                {
                    continue;
                }

                for (var index = 0; index < obj.Vertices.Count; index++)
                {
                    var bone = 0;
                    if (index >= obj.PhysiqueBoneNames.Count || !lookup.TryGetValue(obj.PhysiqueBoneNames[index], out bone))
                    {
                        missing++;
                    }

                    var position = Vector3.Transform(obj.Vertices[index].Position, transforms[bone]);
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
            }

            Console.WriteLine($"frame={frame}; bounds={minimum}..{maximum}; missing={missing}");
            foreach (var name in new[] { "Bip01", "Bip01 Pelvis", "Bip01 Spine", "Bip01 Neck", "Bip01 Head", "Bip01 L Foot", "Bip01 R Foot" })
            {
                if (lookup.TryGetValue(name, out var index))
                {
                    var bone = actor.Skeleton.Objects[index];
                    Console.WriteLine($"{name}: parent={bone.ParentName}; animated={bone.IsAnimated}; keys={bone.RotationKeys.Count}/{bone.PositionKeys.Count}/{bone.ScaleKeys.Count}; at={transforms[index].Translation}");
                    if (frame == 0)
                    {
                        Console.WriteLine($"bind={bone.Transform}; base={bone.BasePosition}; key0={bone.RotationKeys.FirstOrDefault()}; prev0={bone.PreviousRotationMatrices.FirstOrDefault()}");
                    }
                }
            }
        }
    }
}
