using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ModelNormalTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skinned model normals follow posed faces instead of averaged or inverted authored normals", CubeAsync);
        yield return ("skinned model normals smooth gentle folds and keep hard edges", FoldAsync);
        yield return ("a low-polygon fold creased against every face across its edges shades smooth, not as a flat triangle", LoneFoldAsync);
        yield return ("a low-polygon body keeps the file's smooth normals across bends beyond the crease angle", AuthoredBodyAsync);
    }

    private static Task CubeAsync()
    {
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = new Vector3((i & 1) == 0 ? -10 : 10, (i & 2) == 0 ? -10 : 10, (i & 4) == 0 ? -10 : 10);
        }

        // The original defect: one vertex per corner whose normal averages three perpendicular faces, half of them inverted.
        var vertices = corners.Select((position, i) => new PristonModelVertex(position, Vector3.Normalize(i % 2 == 0 ? position : -position))).ToArray();
        int[][] quads = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
        var faces = new List<PristonModelFace>();
        foreach (var quad in quads)
        {
            faces.Add(Outward(corners, quad[0], quad[1], quad[2]));
            faces.Add(Outward(corners, quad[0], quad[2], quad[3]));
        }

        var pose = Matrix.CreateRotationX(MathHelper.PiOver2) * Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(100, 5, -40);
        var model = Model(vertices, faces);
        var normals = PristonModelNormals.Build(model, (_, _) => pose)[0];
        foreach (var face in faces)
        {
            var expected = PosedFaceNormal(vertices, face, pose);
            foreach (var corner in normals[face])
            {
                var shaded = Vector3.Normalize(Vector3.TransformNormal(corner, pose));
                Assert.True(Vector3.Dot(shaded, expected) > 0.9999f, $"cube corner normal {shaded} does not follow its posed face {expected}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task FoldAsync()
    {
        // Two quads hinged on the x axis: a 20 degree fold is smoothed across the hinge, a 90 degree fold stays hard.
        foreach (var (degrees, smooth) in new[] { (20f, true), (90f, false) })
        {
            var tilt = MathHelper.ToRadians(degrees);
            var far = new Vector3(0, MathF.Sin(tilt) * 10, MathF.Cos(tilt) * 10);
            Vector3[] positions = [new(-10, 0, 0), new(10, 0, 0), new(-10, 0, -10), new(10, 0, -10), new Vector3(-10, 0, 0) + far, new Vector3(10, 0, 0) + far];
            var vertices = positions.Select(position => new PristonModelVertex(position, Vector3.Zero)).ToArray();
            List<PristonModelFace> faces = [Upward(positions, 0, 2, 3), Upward(positions, 0, 3, 1), Upward(positions, 0, 1, 5), Upward(positions, 0, 5, 4)];
            var normals = PristonModelNormals.Build(Model(vertices, faces), (_, _) => Matrix.Identity)[0];
            var flat = normals[faces[0]][0];
            var folded = normals[faces[2]][0];
            if (smooth)
            {
                Assert.True(Vector3.Dot(flat, folded) > 0.9999f, "a gentle fold split its hinge normal");
                Assert.True(Vector3.Dot(flat, Vector3.Up) < 0.999f, "a gentle fold did not blend the hinge normal");
            }
            else
            {
                Assert.True(Vector3.Dot(flat, Vector3.Up) > 0.9999f, "a hard edge leaked into the flat face");
                Assert.True(Vector3.Dot(flat, folded) < 0.1f, "a hard edge was smoothed");
            }
        }

        return Task.CompletedTask;
    }

    private static Task LoneFoldAsync()
    {
        // A triangle on the plane y = 0 with a face across each edge bent 70 degrees down, as a monster's neck folds:
        // every neighbour is beyond the crease angle, yet the triangle and its neighbours share each corner's normal.
        Vector3[] inner = [new(0, 0, 0), new(10, 0, 0), new(5, 0, 8.66f)];
        var centre = (inner[0] + inner[1] + inner[2]) / 3f;
        var tilt = MathHelper.ToRadians(70f);
        var positions = new List<Vector3>(inner);
        List<PristonModelFace> faces = [];
        var below = centre - Vector3.UnitY * 20f;
        faces.Add(Away(inner, 0, 1, 2, below));
        for (var edge = 0; edge < 3; edge++)
        {
            var a = edge;
            var b = (edge + 1) % 3;
            var middle = (inner[a] + inner[b]) / 2f;
            var outward = Vector3.Normalize(middle - centre);
            positions.Add(middle + 8f * (outward * MathF.Cos(tilt) - Vector3.UnitY * MathF.Sin(tilt)));
            faces.Add(Away(positions.ToArray(), a, b, positions.Count - 1, below));
        }

        var vertices = positions.Select(position => new PristonModelVertex(position, Vector3.Zero)).ToArray();
        var normals = PristonModelNormals.Build(Model(vertices, faces), (_, _) => Matrix.Identity)[0];
        var fold = normals[faces[0]][Array.IndexOf([faces[0].A, faces[0].B, faces[0].C], 0)];
        var neighbour = normals[faces[1]][Array.IndexOf([faces[1].A, faces[1].B, faces[1].C], 0)];
        Assert.True(Vector3.Dot(fold, Vector3.Up) < 0.99f, $"the folded triangle kept its flat normal {fold}");
        Assert.True(Vector3.Dot(fold, neighbour) > 0.9999f, $"the fold's corner {fold} differs from its neighbour's {neighbour}");
        return Task.CompletedTask;
    }

    private static Task AuthoredBodyAsync()
    {
        // A pentagonal prism, its sides 72 degrees apart, with the file's radial normals (36 degrees off each side): the
        // sides share each edge's normal, as the original shades a coarse limb, instead of five flat facets.
        var positions = new List<Vector3>();
        for (var side = 0; side < 5; side++)
        {
            var angle = side * MathF.Tau / 5f;
            positions.Add(new Vector3(MathF.Cos(angle) * 10f, 0f, MathF.Sin(angle) * 10f));
            positions.Add(new Vector3(MathF.Cos(angle) * 10f, 20f, MathF.Sin(angle) * 10f));
        }

        var vertices = positions.Select(position => new PristonModelVertex(position, Vector3.Normalize(position with { Y = 0f }))).ToArray();
        var axis = new Vector3(0f, 10f, 0f);
        List<PristonModelFace> faces = [];
        for (var side = 0; side < 5; side++)
        {
            var next = (side + 1) % 5;
            var corners = positions.ToArray();
            faces.Add(Away(corners, side * 2, next * 2, next * 2 + 1, axis));
            faces.Add(Away(corners, side * 2, next * 2 + 1, side * 2 + 1, axis));
        }

        var pose = Matrix.CreateRotationY(0.7f) * Matrix.CreateTranslation(30, 0, -12);
        var normals = PristonModelNormals.Build(Model(vertices, faces), (_, _) => pose)[0];
        foreach (var face in faces)
        {
            var indices = new[] { face.A, face.B, face.C };
            for (var corner = 0; corner < 3; corner++)
            {
                var expected = vertices[indices[corner]].Normal;
                Assert.True(Vector3.Dot(normals[face][corner], expected) > 0.9999f,
                    $"a coarse body's corner {normals[face][corner]} left the file's smooth normal {expected}");
            }
        }

        return Task.CompletedTask;
    }

    private static PristonModel Model(PristonModelVertex[] vertices, List<PristonModelFace> faces)
    {
        var obj = new PristonModelObject("fixture", "", false, Matrix.Identity, Vector3.Zero,
            vertices, faces, [], [], [], [], [], [], [], [], []);
        return new PristonModel("fixture", [], [obj], [], default);
    }

    private static PristonModelFace Outward(Vector3[] positions, int a, int b, int c)
    {
        var centre = (positions[a] + positions[b] + positions[c]) / 3f;
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        return Vector3.Dot(normal, centre) >= 0f ? new PristonModelFace(a, b, c, 0, -1) : new PristonModelFace(a, c, b, 0, -1);
    }

    private static PristonModelFace Away(Vector3[] positions, int a, int b, int c, Vector3 inside)
    {
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        var centre = (positions[a] + positions[b] + positions[c]) / 3f;
        return Vector3.Dot(normal, centre - inside) >= 0f ? new PristonModelFace(a, b, c, 0, -1) : new PristonModelFace(a, c, b, 0, -1);
    }

    private static PristonModelFace Upward(Vector3[] positions, int a, int b, int c)
    {
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        return normal.Y >= 0f ? new PristonModelFace(a, b, c, 0, -1) : new PristonModelFace(a, c, b, 0, -1);
    }

    private static Vector3 PosedFaceNormal(PristonModelVertex[] vertices, PristonModelFace face, Matrix pose)
    {
        var a = Vector3.Transform(vertices[face.A].Position, pose);
        var b = Vector3.Transform(vertices[face.B].Position, pose);
        var c = Vector3.Transform(vertices[face.C].Position, pose);
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }
}
