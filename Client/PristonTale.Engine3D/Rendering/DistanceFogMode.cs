using System.Text.Json;
using System.Text.Json.Serialization;

namespace PristonTale.Engine3D.Rendering;

// Atmospheric fades toward the captured sky with a capped curve. Classic, the original stepped colour clip, is kept so
// existing settings files still load; the HDR renderer draws it as Atmospheric (PristonDistanceFog.For).
[JsonConverter(typeof(DistanceFogModeJsonConverter))]
public enum DistanceFogMode
{
    Off,
    Classic,
    Atmospheric
}

// Settings files written before the modes existed stored a boolean: true meant the fog was on.
internal sealed class DistanceFogModeJsonConverter : JsonConverter<DistanceFogMode>
{
    public override DistanceFogMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return DistanceFogMode.Atmospheric;
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return DistanceFogMode.Off;
        }

        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<DistanceFogMode>(reader.GetString(), ignoreCase: false, out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new JsonException("distanceFog must be Off, Classic, Atmospheric or an older boolean.");
    }

    public override void Write(Utf8JsonWriter writer, DistanceFogMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
