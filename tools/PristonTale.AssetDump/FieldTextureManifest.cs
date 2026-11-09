using System.Security.Cryptography;
using System.Text.Json;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Assets;

namespace PristonTale.AssetDump;

// Exports the exact field texture dependency set using the production readers, selection and resolution rules.
internal static class FieldTextureManifest
{
    private sealed record TextureEntry(
        string Path,
        string Sha256,
        int Width,
        int Height,
        bool HasSourceAlpha,
        string AlphaSha256,
        bool OpacityOnly,
        string Preview,
        string Backup,
        string[] Fields);

    private sealed record Manifest(string Field, TextureEntry[] Textures, string[] UninspectedFields);
    private readonly record struct Reference(string Path, bool Opacity);

    public static void Export(string assetRoot, string fieldId, string output)
    {
        assetRoot = Path.GetFullPath(assetRoot);
        output = Path.GetFullPath(output);
        var catalog = new WorldCatalog();
        var field = catalog.AllFields.Single(candidate => candidate.Id == fieldId);
        var references = ReadReferences(assetRoot, field);
        var textures = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            if (reference.Path.Length == 0)
            {
                throw new InvalidDataException($"Field {fieldId} has an unresolved texture; no complete manifest can be produced.");
            }

            textures[reference.Path] = reference.Opacity && (!textures.TryGetValue(reference.Path, out var previous) || previous);
        }

        var consumers = textures.Keys.ToDictionary(path => path, _ => new HashSet<string> { fieldId }, StringComparer.OrdinalIgnoreCase);
        var uninspected = new List<string>();
        foreach (var candidate in catalog.AllFields)
        {
            if (candidate.Id == fieldId)
            {
                continue;
            }

            try
            {
                foreach (var reference in ReadReferences(assetRoot, candidate))
                {
                    if (consumers.TryGetValue(reference.Path, out var fields))
                    {
                        fields.Add(candidate.Id);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                uninspected.Add(candidate.Id);
                Console.Error.WriteLine($"Cannot inspect field {candidate.Id}: {exception.Message}");
            }
        }

        Directory.CreateDirectory(Path.Combine(output, "originals"));
        Directory.CreateDirectory(Path.Combine(output, "previews"));
        var entries = new List<TextureEntry>();
        foreach (var pair in textures.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var bytes = AssetFiles.ReadAllBytes(pair.Key);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var image = PristonImageDecoder.Decode(bytes, Path.GetExtension(pair.Key));
            if (!image.IsValid)
            {
                throw new InvalidDataException($"Cannot decode texture {pair.Key}.");
            }

            var relative = Path.GetRelativePath(assetRoot, pair.Key).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException($"Texture is outside the supplied asset root: {pair.Key}");
            }

            var backup = "originals/" + relative;
            var backupPath = Path.Combine(output, backup);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            if (File.Exists(backupPath))
            {
                if (!SHA256.HashData(File.ReadAllBytes(backupPath)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
                {
                    throw new InvalidDataException($"Original backup differs from source; refusing to overwrite {backupPath}.");
                }
            }
            else
            {
                File.WriteAllBytes(backupPath, bytes);
            }

            var preview = "previews/" + hash[..16] + "-" + Path.GetFileName(pair.Key) + ".png";
            var rgba = new byte[image.Pixels.Length * 4];
            var alpha = new byte[image.Pixels.Length];
            for (var index = 0; index < image.Pixels.Length; index++)
            {
                var color = image.Pixels[index];
                rgba[index * 4] = color.R;
                rgba[index * 4 + 1] = color.G;
                rgba[index * 4 + 2] = color.B;
                rgba[index * 4 + 3] = color.A;
                alpha[index] = color.A;
            }

            PngWriter.Write(Path.Combine(output, preview), image.Width, image.Height, rgba);
            entries.Add(new TextureEntry(relative, hash, image.Width, image.Height, image.HasSourceAlpha,
                Convert.ToHexString(SHA256.HashData(alpha)), pair.Value, preview, backup, consumers[pair.Key].Order().ToArray()));
        }

        var manifest = new Manifest(fieldId, entries.ToArray(), uninspected.ToArray());
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Exported {entries.Count} textures for {fieldId}; {uninspected.Count} fields could not be inspected. No game assets were replaced.");
    }

    private static List<Reference> ReadReferences(string root, FieldDef field)
    {
        var reader = new PristonSmdReader();
        var references = new List<Reference>();
        var stagePath = AssetFiles.Resolve(Path.Combine(root, field.StageSmd));
        var stage = reader.ReadStage(stagePath);
        Collect(Path.GetDirectoryName(stagePath)!, PristonMaterialUsage.ForStage(stage), references);
        foreach (var model in field.StageObjectSmds)
        {
            var modelPath = AssetFiles.Resolve(Path.Combine(root, model));
            Collect(Path.GetDirectoryName(modelPath)!, PristonMaterialUsage.ForModel(reader.ReadModel(modelPath)), references);
        }

        return references;
    }

    private static void Collect(string directory, IEnumerable<PristonMaterial> materials, List<Reference> references)
    {
        foreach (var request in PristonTextureLoader.TextureRequests(materials))
        {
            references.Add(new Reference(PristonTextureLoader.ResolvePath(directory, request.Name), false));
            if (!string.IsNullOrWhiteSpace(request.Opacity))
            {
                references.Add(new Reference(PristonTextureLoader.ResolvePath(directory, request.Opacity), true));
            }
        }
    }
}
