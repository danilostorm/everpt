using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Tests;

// A flat dark floor for the effect captures: the lit untextured fixture reads almost white once exposed, which hides
// additive light. The floor samples one texel of a small dark image written next to the captures.
internal static class CaptureFloor
{
    public static PristonStage Create(string directory, float halfSize, Color tone)
    {
        Directory.CreateDirectory(directory);
        var texture = Path.Combine(directory, "capture-floor.bmp");
        File.WriteAllBytes(texture, Bitmap(tone));
        var material = new PristonMaterial(true, 1, 0, 0, true, Vector3.One, 0, 1, PristonMaterialSurfaceFlags.None, "capture-floor.bmp", "", [], [],
            0, 0, 0);
        var floor = tone;
        return new PristonStage(Path.Combine(directory, "capture-floor.smd"), [material],
            [new(new Vector3(-halfSize, 0, -halfSize), floor), new(new Vector3(-halfSize, 0, halfSize), floor), new(new Vector3(halfSize, 0, halfSize), floor),
             new(new Vector3(halfSize, 0, -halfSize), floor)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [],
            new BoundingBox(new Vector3(-halfSize, 0, -halfSize), new Vector3(halfSize, 0, halfSize)));
    }

    // A 2 x 2 24-bit bitmap of one colour.
    private static byte[] Bitmap(Color tone)
    {
        const int width = 2;
        const int height = 2;
        const int row = 8;
        var bytes = new byte[54 + row * height];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(row * height).CopyTo(bytes, 34);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = 54 + y * row + x * 3;
                bytes[at] = tone.B;
                bytes[at + 1] = tone.G;
                bytes[at + 2] = tone.R;
            }
        }

        return bytes;
    }
}
