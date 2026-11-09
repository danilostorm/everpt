using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.World.Motion;

// The attack clip of each class with each weapon, read from the shipped motion tables into Data/PlayerEquipmentTiming.json
// (one entry per class and clip with the weapon keys that share it). The equipment asset probe's
// --export-equipment-timings writes the file; do not edit timings by hand.
public static class PlayerEquipmentTiming
{
    private const string Resource = "PristonTale.Simulation.World.Motion.Data.PlayerEquipmentTiming.json";

    private static readonly FrozenDictionary<(PlayerClass Class, string Key), MotionClip> Clips = Load();

    public static int Count => Clips.Count;

    public static bool TryGet(PlayerClass playerClass, string key, out MotionClip clip) => Clips.TryGetValue((playerClass, key), out clip);

    private static FrozenDictionary<(PlayerClass, string), MotionClip> Load()
    {
        using var stream = typeof(PlayerEquipmentTiming).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException($"Missing embedded resource {Resource}.");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        var groups = JsonSerializer.Deserialize<Group[]>(stream, options) ?? [];
        var clips = new Dictionary<(PlayerClass, string), MotionClip>();
        foreach (var group in groups)
        {
            var clip = new MotionClip(group.Start, group.End, group.Hit, 0, 0, 0, false);
            foreach (var key in group.Keys)
            {
                clips.Add((group.Class, key), clip);
            }
        }

        return clips.ToFrozenDictionary();
    }

    private sealed record Group(PlayerClass Class, int Start, int End, uint Hit, string[] Keys);
}
