using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Formats;

// Rigid Physique binds each vertex to one bone, and some models carry a stray: a vertex at an elbow bound to the spine
// while every vertex around it follows the arm (the Item Distributor's coat, both elbows). The arm drags its neighbours
// away and the stray stays with the spine, pulling a spike out of the elbow. A vertex takes the bone most of its edge
// neighbours follow when none of them shares its own, that bone is at least FarJoints joints from its own in the
// skeleton, and in the bind pose the vertex lies at least NearerFactor times nearer that bone than its own. Ordinary
// seams (a finger tip on its last joint, a shoulder on the clavicle, a hip on the pelvis) are two or three joints apart.
public static class PristonPhysiqueRepair
{
    private const int FarJoints = 4;
    private const float NearerFactor = 2f;

    public static PristonModel Repair(PristonModel mesh, PristonModel skeleton)
    {
        var bones = new Dictionary<string, PristonModelObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var bone in skeleton.Objects)
        {
            if (bone.Name.Length > 0)
            {
                bones.TryAdd(bone.Name, bone);
            }
        }

        List<PristonModelObject> repaired = null;
        for (var index = 0; index < mesh.Objects.Count; index++)
        {
            var obj = mesh.Objects[index];
            var fixedObject = obj.HasPhysique && obj.PhysiqueBoneNames.Count == obj.Vertices.Count ? RepairObject(obj, bones) : null;
            if (fixedObject is not null)
            {
                repaired ??= [.. mesh.Objects];
                repaired[index] = fixedObject;
            }
        }

        return repaired is null ? mesh : mesh with { Objects = repaired };
    }

    // The object with its strays moved to their neighbours' bone, or null when it has none. A moved vertex keeps its bind
    // position and normal: both are stored in the bone's space, so they are carried into the new bone's.
    private static PristonModelObject RepairObject(PristonModelObject obj, Dictionary<string, PristonModelObject> bones)
    {
        var neighbours = new HashSet<int>[obj.Vertices.Count];
        foreach (var face in obj.Faces)
        {
            if ((uint)face.A >= (uint)obj.Vertices.Count || (uint)face.B >= (uint)obj.Vertices.Count || (uint)face.C >= (uint)obj.Vertices.Count)
            {
                continue;
            }

            Link(neighbours, face.A, face.B);
            Link(neighbours, face.B, face.C);
            Link(neighbours, face.C, face.A);
        }

        string[] names = null;
        PristonModelVertex[] vertices = null;
        for (var vertex = 0; vertex < neighbours.Length; vertex++)
        {
            var own = obj.PhysiqueBoneNames[vertex];
            if (neighbours[vertex] is not { Count: > 0 } around || !bones.TryGetValue(own, out var ownBone) ||
                around.Any(other => string.Equals(obj.PhysiqueBoneNames[other], own, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var majority = around.GroupBy(other => obj.PhysiqueBoneNames[other], StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).First().Key;
            if (!bones.TryGetValue(majority, out var majorityBone) || JointsBetween(bones, ownBone, majorityBone) < FarJoints)
            {
                continue;
            }

            // The file stores the vertex in its bone's space; the bone's transform is its bind pose.
            var bind = Vector3.Transform(obj.Vertices[vertex].Position, ownBone.Transform);
            if (Vector3.Distance(bind, majorityBone.Transform.Translation) * NearerFactor > Vector3.Distance(bind, ownBone.Transform.Translation))
            {
                continue;
            }

            names ??= [.. obj.PhysiqueBoneNames];
            vertices ??= [.. obj.Vertices];
            var toMajority = Matrix.Invert(majorityBone.Transform);
            var normal = Vector3.TransformNormal(Vector3.TransformNormal(obj.Vertices[vertex].Normal, ownBone.Transform), toMajority);
            names[vertex] = majority;
            vertices[vertex] = new PristonModelVertex(Vector3.Transform(bind, toMajority), normal);
        }

        return names is null ? null : obj with { PhysiqueBoneNames = names, Vertices = vertices };
    }

    // Joints on the skeleton path between two bones, through their nearest common ancestor; unrelated bones are far.
    private static int JointsBetween(Dictionary<string, PristonModelObject> bones, PristonModelObject from, PristonModelObject to)
    {
        var depths = new Dictionary<PristonModelObject, int>();
        for (var (bone, depth) = (from, 0); bone is not null && depths.TryAdd(bone, depth); depth++)
        {
            bone = bones.GetValueOrDefault(bone.ParentName);
        }

        for (var (bone, depth) = (to, 0); bone is not null && depth <= bones.Count; depth++)
        {
            if (depths.TryGetValue(bone, out var fromDepth))
            {
                return fromDepth + depth;
            }

            bone = bones.GetValueOrDefault(bone.ParentName);
        }

        return int.MaxValue;
    }

    private static void Link(HashSet<int>[] neighbours, int a, int b)
    {
        if (a != b)
        {
            (neighbours[a] ??= []).Add(b);
            (neighbours[b] ??= []).Add(a);
        }
    }
}
