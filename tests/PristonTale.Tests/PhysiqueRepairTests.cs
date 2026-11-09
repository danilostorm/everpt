using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Tests;

internal static class PhysiqueRepairTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a vertex bound far up the skeleton among its neighbours' bone follows that bone from the same bind position", StrayAsync);
        yield return ("a vertex on a bone next to its neighbours' bone keeps its binding", SeamAsync);
    }

    // A chain Spine -> Spine1 -> Neck -> Clavicle -> UpperArm -> Forearm along x, five units per joint.
    private static readonly string[] Chain = ["Spine", "Spine1", "Neck", "Clavicle", "UpperArm", "Forearm"];

    private static Task StrayAsync()
    {
        // A fan of four forearm vertices around one at the elbow bound to the spine, stored in the spine's space.
        var elbow = new Vector3(26f, 1f, 0f);
        var repaired = Repair(elbow, "Spine", "Forearm");
        var obj = repaired.Objects[0];
        Assert.True(obj.PhysiqueBoneNames[0] == "Forearm", $"the stray kept {obj.PhysiqueBoneNames[0]}");
        var bind = Vector3.Transform(obj.Vertices[0].Position, Bone("Forearm").Transform);
        Assert.True(Vector3.Distance(bind, elbow) < 1e-4f, $"the stray moved from {elbow} to {bind} in the bind pose");
        return Task.CompletedTask;
    }

    private static Task SeamAsync()
    {
        // The same fan on the upper arm, the centre on the clavicle beside it: two joints apart, an ordinary seam.
        var shoulder = new Vector3(21f, 1f, 0f);
        var repaired = Repair(shoulder, "Clavicle", "UpperArm");
        Assert.True(repaired.Objects[0].PhysiqueBoneNames[0] == "Clavicle", "a seam vertex was rebound");
        return Task.CompletedTask;
    }

    private static PristonModel Repair(Vector3 centre, string centreBone, string ringBone)
    {
        var ringTransform = Bone(ringBone).Transform;
        var inverseRing = Matrix.Invert(ringTransform);
        var vertices = new List<PristonModelVertex> { new(Vector3.Transform(centre, Matrix.Invert(Bone(centreBone).Transform)), Vector3.UnitY) };
        for (var index = 0; index < 4; index++)
        {
            var angle = index * MathF.Tau / 4f;
            var around = centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 1.5f;
            vertices.Add(new PristonModelVertex(Vector3.Transform(around, inverseRing), Vector3.UnitY));
        }

        List<PristonModelFace> faces = [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1), new(0, 3, 4, 0, -1), new(0, 4, 1, 0, -1)];
        string[] names = [centreBone, ringBone, ringBone, ringBone, ringBone];
        var mesh = new PristonModel("mesh", [], [Object("body", "", true, Matrix.Identity, vertices, faces, names)], [], default);
        var skeleton = new PristonModel("skeleton", [], Chain.Select(name => Bone(name)).ToArray(), [], default);
        return PristonPhysiqueRepair.Repair(mesh, skeleton);
    }

    private static PristonModelObject Bone(string name)
    {
        var index = Array.IndexOf(Chain, name);
        return Object(name, index > 0 ? Chain[index - 1] : "", false, Matrix.CreateTranslation(index * 5f, 0f, 0f), [], [], []);
    }

    private static PristonModelObject Object(string name, string parent, bool physique, Matrix transform, IReadOnlyList<PristonModelVertex> vertices,
        IReadOnlyList<PristonModelFace> faces, IReadOnlyList<string> bones) =>
        new(name, parent, physique, transform, Vector3.Zero, vertices, faces, [], [], [], [], [], bones, [], [], []);
}
