using PristonTale.Assets;

namespace PristonTale.WorldImport.Parsers;

public sealed record SourceLine(string Path, int Number, string Text);

public static class SourceTextReader
{
    public static IReadOnlyList<SourceLine> ReadChain(string path)
    {
        var rootPath = Path.GetFullPath(path);
        var baseDirectory = Path.GetDirectoryName(rootPath)
            ?? throw new InvalidDataException($"Source file has no directory: {path}");
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SourceLine>();
        var current = rootPath;

        while (visited.Add(current))
        {
            var lines = Cp949.ReadLines(current);
            string? next = null;

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                result.Add(new SourceLine(current, index + 1, line));

                if (Tokenizer.LeadingKey(line) != KeyMaps.ItemLinkedFile)
                {
                    continue;
                }

                var tokenizer = new Tokenizer(line);
                tokenizer.NextWord();
                var linked = tokenizer.NextValueOrQuoted();
                next = AssetFiles.ResolveFile(baseDirectory, linked);
                break;
            }

            if (next is null)
            {
                break;
            }

            current = next;
        }

        return result;
    }
}
