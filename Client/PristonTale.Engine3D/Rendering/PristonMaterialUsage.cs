using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal static class PristonMaterialUsage
{
    public static IEnumerable<PristonMaterial> ForStage(PristonStage stage)
    {
        return Select(stage.Materials, stage.Faces.Select(face => face.MaterialIndex));
    }

    public static IEnumerable<PristonMaterial> ForModel(PristonModel model)
    {
        return Select(model.Materials, model.Objects.SelectMany(modelObject => modelObject.Faces).Select(face => face.MaterialIndex));
    }

    public static IEnumerable<PristonMaterial> ForActor(PristonActor actor)
    {
        var selectedNames = actor.RenderObjectNames.Count == 0
            ? null : new HashSet<string>(actor.RenderObjectNames, StringComparer.OrdinalIgnoreCase);
        return Select(actor.Mesh.Materials, ActorMaterialIndices(actor.Mesh, selectedNames));
    }

    private static IEnumerable<int> ActorMaterialIndices(PristonModel mesh, HashSet<string> selectedNames)
    {
        foreach (var modelObject in mesh.Objects)
        {
            if (selectedNames is not null && !selectedNames.Contains(modelObject.Name))
            {
                continue;
            }

            foreach (var face in modelObject.Faces)
            {
                yield return face.MaterialIndex;
            }
        }
    }

    private static IEnumerable<PristonMaterial> Select(IReadOnlyList<PristonMaterial> materials, IEnumerable<int> indices)
    {
        var seen = new HashSet<int>();
        foreach (var index in indices)
        {
            if (index < 0 || index >= materials.Count || !seen.Add(index))
            {
                continue;
            }

            var material = materials[index];
            if (!material.IsInvisible && !material.HasNoRenderableTexture)
            {
                yield return material;
            }
        }
    }
}
