using PristonTale.Engine3D.Rendering;

namespace PristonTale.GltfExport;

internal readonly record struct DecodedImage(int Width, int Height, byte[] Rgba, bool HasSourceAlpha)
{
    public bool IsValid => Width > 0 && Height > 0 && Rgba.Length == Width * Height * 4;
}

internal static class ImageDecode
{
    public static DecodedImage Decode(byte[] bytes, string extension)
    {
        var source = PristonImageDecoder.Decode(bytes, extension);
        if (!source.IsValid)
        {
            return default;
        }

        var rgba = GC.AllocateUninitializedArray<byte>(checked(source.Pixels.Length * 4));
        for (var index = 0; index < source.Pixels.Length; index++)
        {
            var color = source.Pixels[index];
            var outputIndex = index * 4;
            rgba[outputIndex] = color.R;
            rgba[outputIndex + 1] = color.G;
            rgba[outputIndex + 2] = color.B;
            rgba[outputIndex + 3] = color.A;
        }

        return new DecodedImage(
            source.Width,
            source.Height,
            rgba,
            source.HasSourceAlpha);
    }
}
