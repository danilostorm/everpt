using System.Text.Json;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.Saves;

// Character captures storage could not take when the server stopped, kept as files in one directory until the next start
// stores them (CharacterSaveQueue.ReplayAsync). Files are read in the order they were written.
public sealed class SaveJournal(string directory)
{
    public string Directory { get; } = directory;

    public void Write(IReadOnlyList<CharacterRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.json");
        // Written aside, flushed to the disk and renamed, so a file the next start reads is complete; a ".partial" file is a
        // write that never finished, whose waiters were answered false.
        var partial = path + PartialExtension;
        using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write))
        {
            JsonSerializer.Serialize(stream, records);
            stream.Flush(flushToDisk: true);
        }

        File.Move(partial, path);
    }

    private const string PartialExtension = ".partial";

    // The journal files, oldest first; unfinished writes are removed.
    public IReadOnlyList<string> Files()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        foreach (var partial in System.IO.Directory.GetFiles(Directory, "*" + PartialExtension))
        {
            File.Delete(partial);
        }

        return System.IO.Directory.GetFiles(Directory, "*.json").Order(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<CharacterRecord> Read(string file) =>
        JsonSerializer.Deserialize<CharacterRecord[]>(File.ReadAllText(file)) ?? throw new InvalidDataException($"Empty save journal {file}.");
}
