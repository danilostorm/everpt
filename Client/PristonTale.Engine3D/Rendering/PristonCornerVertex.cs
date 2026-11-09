using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// One triangle corner before vertex welding: the authored colour, the UV of every blended texture stage and, for stages,
// the normal computed over the whole stage (PristonStageNormals); a zero normal is averaged from the welded faces instead.
internal readonly record struct PristonCornerVertex(Vector3 Position, Color Color, Vector2 Uv0, Vector2 Uv1, Vector2 Uv2, Vector3 Normal = default)
{
    public static void AddTriangle(
        List<PristonCornerVertex> target,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color colorA,
        Color colorB,
        Color colorC,
        IReadOnlyList<PristonTexLink> links,
        int firstLink,
        Vector3 normalA = default,
        Vector3 normalB = default,
        Vector3 normalC = default)
    {
        var stages = PristonStageLinks.Resolve(links, firstLink);
        target.Add(new PristonCornerVertex(a, colorA, stages.Stage0.A, stages.Stage1.A, stages.Stage2.A, normalA));
        target.Add(new PristonCornerVertex(b, colorB, stages.Stage0.B, stages.Stage1.B, stages.Stage2.B, normalB));
        target.Add(new PristonCornerVertex(c, colorC, stages.Stage0.C, stages.Stage1.C, stages.Stage2.C, normalC));
    }
}

// Follows a face's chain of texture link records: one link per blended texture stage, each with its own UVs.
internal readonly record struct PristonStageLinks(PristonTexLink Stage0, PristonTexLink Stage1, PristonTexLink Stage2)
{
    public static PristonStageLinks Resolve(IReadOnlyList<PristonTexLink> links, int firstLink)
    {
        var stage0 = firstLink >= 0 && firstLink < links.Count ? links[firstLink] : PristonTexLink.Empty;
        var stage1 = Next(links, stage0);
        var stage2 = Next(links, stage1);
        return new PristonStageLinks(stage0, stage1, stage2);
    }

    private static PristonTexLink Next(IReadOnlyList<PristonTexLink> links, PristonTexLink link) =>
        link.NextIndex >= 0 && link.NextIndex < links.Count ? links[link.NextIndex] : PristonTexLink.Zero;
}
