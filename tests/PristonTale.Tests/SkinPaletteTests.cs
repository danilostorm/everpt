using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class SkinPaletteTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("shared skin palettes preserve affine positions, normals, tails and reuse", PackingAsync);
        yield return ("affine palette bounds match scalar corner expansion", BoundsAsync);
    }

    private static Task BoundsAsync()
    {
        var bind = new BoundingBox(new Vector3(-7, -3, -11), new Vector3(13, 17, 5));
        Matrix[] palette =
        [
            Matrix.CreateScale(1.5f, 0.75f, 2f) * Matrix.CreateRotationY(0.63f) * Matrix.CreateTranslation(120, -30, 44),
            Matrix.CreateScale(0.8f, 1.2f, 0.6f) * Matrix.CreateRotationX(-0.41f) * Matrix.CreateTranslation(-8, 70, 19)
        ];
        var optimized = PristonRenderer.TransformBoundsByPalette(bind, palette);
        var expected = ScalarBounds(bind, palette);
        Assert.Near(expected.Min.X, optimized.Min.X, 0.001f);
        Assert.Near(expected.Min.Y, optimized.Min.Y, 0.001f);
        Assert.Near(expected.Min.Z, optimized.Min.Z, 0.001f);
        Assert.Near(expected.Max.X, optimized.Max.X, 0.001f);
        Assert.Near(expected.Max.Y, optimized.Max.Y, 0.001f);
        Assert.Near(expected.Max.Z, optimized.Max.Z, 0.001f);
        return Task.CompletedTask;
    }

    private static BoundingBox ScalarBounds(BoundingBox bind, IReadOnlyList<Matrix> palette)
    {
        var corners = bind.GetCorners();
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        for (var bone = 0; bone < palette.Count; bone++)
        {
            for (var corner = 0; corner < corners.Length; corner++)
            {
                var transformed = Vector3.Transform(corners[corner], palette[bone]);
                minimum = Vector3.Min(minimum, transformed);
                maximum = Vector3.Max(maximum, transformed);
            }
        }

        return new BoundingBox(minimum, maximum);
    }

    private static Task PackingAsync()
    {
        foreach (var count in new[] { 0, 1, 31, 48, 64 })
        {
            var palette = new PristonSkinPalette(count);
            var rows = palette.ShaderRows;
            Assert.True(rows.Length == count * 3, "palette reserved shader rows for unused bones");
            for (var frame = 0; frame < 3; frame++)
            {
                for (var bone = 0; bone < count; bone++)
                {
                    palette.Matrices[bone] = Matrix.CreateScale(1 + bone * 0.01f)
                        * Matrix.CreateRotationY(frame * 0.4f + bone * 0.1f)
                        * Matrix.CreateTranslation(bone * 100, frame * 50, -bone * 17);
                }

                var previousRevision = palette.Revision;
                palette.Pack();
                Assert.True(palette.Revision == previousRevision + 1, "packing did not advance the palette revision");
                Assert.True(ReferenceEquals(rows, palette.ShaderRows), "packing allocated a replacement shader buffer");
                for (var bone = 0; bone < count; bone++)
                {
                    Verify(new Vector4(3, -5, 7, 1), palette.Matrices[bone], rows, bone);
                    Verify(new Vector4(0, 1, 0, 0), palette.Matrices[bone], rows, bone);
                }
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            palette.Pack();
            Assert.True(GC.GetAllocatedBytesForCurrentThread() == allocated, "pose packing allocated on the frame path");
        }

        return Task.CompletedTask;
    }

    private static void Verify(Vector4 input, Matrix matrix, Vector4[] rows, int bone)
    {
        var reference = Vector4.Transform(input, matrix);
        var row = bone * 3;
        Assert.Near(reference.X, Vector4.Dot(rows[row], input), 0.001f);
        Assert.Near(reference.Y, Vector4.Dot(rows[row + 1], input), 0.001f);
        Assert.Near(reference.Z, Vector4.Dot(rows[row + 2], input), 0.001f);
    }
}
