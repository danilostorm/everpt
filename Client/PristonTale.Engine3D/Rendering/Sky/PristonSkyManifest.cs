using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.IO;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Rendering;

public sealed record PristonSkyLayer(string ModelPath, Color Color, bool DelayedFade);

public static class PristonSkyManifest
{
    public static IReadOnlyList<PristonSkyLayer[]> Read(string path)
    {
        using var stream = AssetFiles.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.Latin1);
        return Parse(reader);
    }

    public static IReadOnlyList<PristonSkyLayer[]> Parse(TextReader reader)
    {
        var profiles = new List<PristonSkyLayer[]>();
        List<PristonSkyLayer> layers = null;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains('{'))
            {
                if (layers != null)
                {
                    throw new InvalidDataException("Nested sky profile.");
                }

                layers = [];
            }
            else if (line.Contains('}'))
            {
                if (layers == null)
                {
                    throw new InvalidDataException("Unexpected end of sky profile.");
                }

                profiles.Add(layers.ToArray());
                layers = null;
            }
            else if (line.TrimStart().StartsWith("FileName:", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (layers == null || tokens.Length != 9
                    || !tokens[2].Equals("FadeDelay:", StringComparison.OrdinalIgnoreCase)
                    || !tokens[4].Equals("RGBA:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid sky layer: {line}");
                }

                var components = new byte[4];
                for (var index = 0; index < components.Length; index++)
                {
                    components[index] = byte.Parse(tokens[index + 5], CultureInfo.InvariantCulture);
                }

                var model = string.Join('/', tokens[1].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries));
                layers.Add(new PristonSkyLayer(
                    Path.ChangeExtension(model, ".smd"),
                    new Color(components[0], components[1], components[2], components[3]),
                    tokens[3] == "1"));
            }
        }

        if (layers != null || profiles.Count == 0)
        {
            throw new InvalidDataException("Incomplete sky manifest.");
        }

        return profiles;
    }
}
