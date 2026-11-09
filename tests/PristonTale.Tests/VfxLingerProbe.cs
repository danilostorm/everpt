using Microsoft.Xna.Framework;
using PristonTale.Effects.Vfx;

namespace PristonTale.Tests;

// Diagnostic: plays every effect with a caster and a target that keep moving and lists the effects still alive long
// after their own duration.
internal static class VfxLingerProbe
{
    public static int Run(string assets)
    {
        var catalog = VfxCatalog.Load(assets);
        var library = new VfxLibrary(_ => null, null!);
        foreach (var definition in catalog.Definitions)
        {
            library.Register(definition);
        }

        var anchors = new MovingAnchors();
        var lingering = 0;
        foreach (var name in library.Names.Order(StringComparer.Ordinal))
        {
            library.TryGet(name, out var effect);
            if (effect.Definition.Duration > 60f)
            {
                continue;
            }

            var system = new VfxSystem(anchors, library: library);
            anchors.Time = 0f;
            system.Spawn(effect, new VfxSpawn(1, 2, new Vector3(0, 0, 0), 0f));
            var limit = effect.Definition.Duration + 30f;
            var time = 0f;
            while (time < limit && system.Instances.Count > 0)
            {
                system.Update(0.05f);
                anchors.Time += 0.05f;
                time += 0.05f;
            }

            if (system.Instances.Count > 0)
            {
                lingering++;
                var alive = string.Join(", ", system.Instances.Select(instance => $"{instance.Effect.Definition.Name}@{instance.Age:F0}s"));
                Console.WriteLine($"{name} (duration {effect.Definition.Duration}s) still alive after {limit:F0}s: {alive}");
            }
        }

        Console.WriteLine($"{lingering} of {library.Count} effects outlive their duration");
        return 0;
    }

    private sealed class MovingAnchors : IVfxAnchors
    {
        public float Time;

        public bool TryGetPosition(ulong entityId, out Vector3 position)
        {
            position = new Vector3(entityId * 100f + Time * 80f, 0, 0);
            return true;
        }

        public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
        {
            TryGetPosition(entityId, out position);
            position.Y += 50f;
            return true;
        }
    }
}
