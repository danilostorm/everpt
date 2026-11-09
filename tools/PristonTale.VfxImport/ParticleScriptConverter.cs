using System.Globalization;
using PristonTale.Effects.Vfx;

namespace PristonTale.VfxImport;

// Converts an original particle script (Effect/Particle/Script/<name>.part) to an effect with sequence layers. The
// reader follows the original tokenizer and parser, including what they accept and how they fail: words are classified
// by the keyword they contain; a value written without a space after '=' (as in "size =40") is lost and the property
// keeps 0; a malformed sequence makes the whole script fail, and a failed script plays nothing.
internal static class ParticleScriptConverter
{
    public const string Prefix = "particle.";
    public const string TextureFolder = "Effect/Particle/";

    public static VfxDefinition Convert(string name, string text, AssetPaths assets, ICollection<string> report)
    {
        var tokens = Tokenize(text);
        var sequences = new List<VfxSequenceDefinition>();
        try
        {
            var reader = new Reader(tokens, name, report);
            reader.ReadSystem(sequences, assets);
        }
        catch (ScriptRejected rejected)
        {
            report.Add($"{name}: the original rejects this script ({rejected.Message}); it plays nothing");
            sequences.Clear();
        }

        return new VfxDefinition { Name = Prefix + name, Duration = VfxDefinition.MaxDuration, Sequences = [.. sequences] };
    }

    private sealed class ScriptRejected(string message) : Exception(message);

    private enum Kind
    {
        Unknown,
        Number,
        Equals,
        Comma,
        Open,
        Close,
        OpenBrace,
        CloseBrace,
        Quote,
        System,
        Sequence,
        Random,
        Xyz,
        Rgba,
        Fade,
        OutLength,
        InLength,
        Initial,
        Final,
        Texture,
        So,
        At,
        ParticleType,
        TypeMode,
        BlendMode,
        BlendProperty,
        NumericProperty,
        VectorProperty,
        SystemVector,
        EventNumber,
        EventAngle,
        EventVector,
        EventColor
    }

    private readonly record struct Token(Kind Kind, string Text);

    private sealed class Reader(List<Token> tokens, string name, ICollection<string> report)
    {
        private int _at;

        private Token Current => _at < tokens.Count ? tokens[_at] : new Token(Kind.Unknown, "");

        private void Next() => _at++;

        public void ReadSystem(List<VfxSequenceDefinition> sequences, AssetPaths assets)
        {
            Expect(Kind.System, "particlesystem");
            Expect(Kind.Quote, "a name");
            Expect(Kind.Number, "a version");
            if (Current.Kind != Kind.OpenBrace)
            {
                throw new ScriptRejected("no opening brace");
            }

            Next();
            while (Current.Kind != Kind.CloseBrace)
            {
                if (_at >= tokens.Count)
                {
                    throw new ScriptRejected("no closing brace");
                }

                switch (Current.Kind)
                {
                    case Kind.SystemVector:
                        if (sequences.Count > 0)
                        {
                            throw new ScriptRejected("a system property after a sequence");
                        }

                        Next();
                        Expect(Kind.Equals, "=");
                        // The system position is replaced by where the effect starts.
                        ReadVector(out _, out _);
                        break;
                    case Kind.Sequence:
                        sequences.Add(ReadSequence(assets, sequences.Count));
                        break;
                    default:
                        throw new ScriptRejected($"unexpected '{Current.Text}' in the system");
                }
            }
        }

        private VfxSequenceDefinition ReadSequence(AssetPaths assets, int index)
        {
            Next();
            Expect(Kind.Quote, "a sequence name");
            Expect(Kind.OpenBrace, "{");
            var events = new List<VfxSequenceEvent>();
            var blend = "";
            float[] rate = [0f, 0f];
            float[] lifetime = [0f, 0f];
            var delay = 0f;
            var count = 100;
            var loops = -1;
            float[] radiusMin = [0f, 0f, 0f];
            float[] radiusMax = [0f, 0f, 0f];
            float[] gravityMin = [0f, 0f, 0f];
            float[] gravityMax = [0f, 0f, 0f];
            var shape = VfxSequenceShape.Billboard;
            var texture = "";
            var started = false;
            while (Current.Kind != Kind.CloseBrace)
            {
                if (_at >= tokens.Count)
                {
                    throw new ScriptRejected("a sequence is not closed");
                }

                var saved = Current;
                switch (Current.Kind)
                {
                    case Kind.NumericProperty:
                        if (started)
                        {
                            throw new ScriptRejected("a sequence property after its events");
                        }

                        Next();
                        Expect(Kind.Equals, "=");
                        var (low, high) = ReadNumber();
                        var key = saved.Text;
                        if (key.Contains("EMITRATE", StringComparison.Ordinal))
                        {
                            rate = [low, high];
                        }
                        else if (key.Contains("DELAY", StringComparison.Ordinal))
                        {
                            delay = high;
                        }
                        else if (key.Contains("LIFETIME", StringComparison.Ordinal))
                        {
                            lifetime = [low, high];
                        }
                        else if (key.Contains("NUMPARTICLES", StringComparison.Ordinal))
                        {
                            count = (int)high;
                        }
                        else if (key.Contains("LOOPS", StringComparison.Ordinal))
                        {
                            loops = (int)high;
                        }

                        break;
                    case Kind.VectorProperty:
                        Next();
                        Expect(Kind.Equals, "=");
                        ReadVector(out var min, out var max);
                        if (saved.Text.Contains("EMITRADIUS", StringComparison.Ordinal))
                        {
                            (radiusMin, radiusMax) = Flip(min, max);
                        }
                        else if (saved.Text.Contains("GRAVITY", StringComparison.Ordinal))
                        {
                            (gravityMin, gravityMax) = Flip(min, max);
                        }
                        else
                        {
                            report.Add($"{name}: {saved.Text.ToLowerInvariant()} has no effect in the original");
                        }

                        break;
                    case Kind.ParticleType:
                        if (started)
                        {
                            throw new ScriptRejected("a sequence property after its events");
                        }

                        Next();
                        Expect(Kind.Equals, "=");
                        if (Current.Kind == Kind.TypeMode)
                        {
                            shape = Current.Text switch
                            {
                                "TYPE_TWO" => VfxSequenceShape.Ground,
                                "TYPE_THREE" => VfxSequenceShape.Upright,
                                "TYPE_FOUR" => VfxSequenceShape.Trail,
                                _ => VfxSequenceShape.Billboard
                            };
                            Next();
                        }

                        break;
                    case Kind.BlendProperty:
                        if (started)
                        {
                            throw new ScriptRejected("a sequence property after its events");
                        }

                        Next();
                        Expect(Kind.Equals, "=");
                        if (Current.Kind != Kind.BlendMode)
                        {
                            throw new ScriptRejected("no blend mode");
                        }

                        if (saved.Text.Contains("SOURCEBLENDMODE", StringComparison.Ordinal))
                        {
                            blend = Current.Text;
                        }

                        Next();
                        break;
                    case Kind.Texture:
                        if (started)
                        {
                            throw new ScriptRejected("a sequence property after its events");
                        }

                        Next();
                        Expect(Kind.Equals, "=");
                        if (Current.Kind != Kind.Quote)
                        {
                            throw new ScriptRejected("no texture file");
                        }

                        texture = Current.Text.Trim('"');
                        Next();
                        break;
                    case Kind.Fade:
                    case Kind.At:
                    case Kind.Initial:
                    case Kind.Final:
                        started = true;
                        if (ReadEvent(lifetime[1]) is { } entry)
                        {
                            events.Add(entry);
                        }

                        break;
                    default:
                        throw new ScriptRejected($"unexpected '{saved.Text}' in a sequence");
                }
            }

            Next();
            var file = Path.GetFileName(texture.Replace('\\', '/'));
            if (!assets.TryResolve(TextureFolder + file, out var resolved))
            {
                report.Add($"{name}: sequence {index} texture {file} is missing; it draws untextured");
                resolved = TextureFolder + file;
            }

            if (blend.Length == 0)
            {
                report.Add($"{name}: sequence {index} names no blend; lamp is used");
            }

            return new VfxSequenceDefinition
            {
                Texture = resolved,
                Blend = Blend(blend),
                Shape = shape,
                Count = Math.Clamp(count, 1, 4096),
                Loops = Math.Max(0, loops),
                Rate = rate[1] >= rate[0] ? rate : [rate[1], rate[0]],
                Delay = Math.Clamp(delay, 0f, 60f),
                Lifetime = lifetime[1] >= lifetime[0] ? lifetime : [lifetime[1], lifetime[0]],
                RadiusMin = radiusMin,
                RadiusMax = radiusMax,
                GravityMin = gravityMin,
                GravityMax = gravityMax,
                Events = [.. events]
            };
        }

        private VfxSequenceEvent? ReadEvent(float final)
        {
            var fade = false;
            if (Current.Kind == Kind.Fade)
            {
                Next();
                Expect(Kind.So, "so");
                fade = true;
            }

            float time;
            switch (Current.Kind)
            {
                case Kind.At:
                    Next();
                    var (low, high) = ReadNumber();
                    // Random event times are drawn once per script load; the data only uses fixed times.
                    time = low == high ? low : (low + high) * 0.5f;
                    break;
                case Kind.Initial:
                    if (fade)
                    {
                        throw new ScriptRejected("an initial event cannot fade");
                    }

                    Next();
                    time = 0f;
                    break;
                case Kind.Final:
                    Next();
                    time = final;
                    break;
                default:
                    throw new ScriptRejected("no event time");
            }

            if (Current.Kind is not (Kind.EventNumber or Kind.EventVector or Kind.EventColor or Kind.EventAngle))
            {
                throw new ScriptRejected($"'{Current.Text}' is not a particle property");
            }

            var property = Property(Current.Text);
            Next();
            var value = Read(property, out var mode);
            return new VfxSequenceEvent { T = time, Ramp = fade, Set = property, Min = value.Min, Max = value.Max, Mode = mode };
        }

        private (float[] Min, float[] Max) Read(VfxSequenceProperty property, out VfxVelocityMode mode)
        {
            mode = VfxVelocityMode.Vector;
            if (Current.Kind != Kind.Equals)
            {
                report.Add($"{name}: a {property} value is missing and stays 0");
                return Zero(property);
            }

            Next();
            switch (property)
            {
                case VfxSequenceProperty.Color:
                {
                    if (!TryColor(out var min, out var max))
                    {
                        return Zero(property);
                    }

                    return (min, max);
                }
                case VfxSequenceProperty.Velocity:
                {
                    if (Current.Kind is Kind.OutLength or Kind.InLength)
                    {
                        mode = Current.Kind == Kind.OutLength ? VfxVelocityMode.Outward : VfxVelocityMode.Inward;
                        tokens[_at] = new Token(Kind.Xyz, Current.Text);
                    }

                    if (!TryVector(out var min, out var max))
                    {
                        return Zero(property);
                    }

                    return mode == VfxVelocityMode.Vector ? Flip(min, max) : (min, max);
                }
                case VfxSequenceProperty.Angle:
                case VfxSequenceProperty.Orbit:
                {
                    if (!TryVector(out var min, out var max))
                    {
                        return Zero(property);
                    }

                    // Mirroring the scene's Z axis turns rotations about X and Y the other way.
                    return ([-max[0], -max[1], min[2]], [-min[0], -min[1], max[2]]);
                }
                default:
                {
                    if (Current.Kind is not (Kind.Number or Kind.Random))
                    {
                        report.Add($"{name}: a {property} value is unreadable and stays 0");
                        return Zero(property);
                    }

                    var (low, high) = ReadNumber();
                    return property switch
                    {
                        VfxSequenceProperty.VelocityZ => ([-high], [-low]),
                        VfxSequenceProperty.AngleX or VfxSequenceProperty.AngleY or VfxSequenceProperty.OrbitY => ([-high], [-low]),
                        _ => ([low], [high])
                    };
                }
            }
        }

        private static (float[] Min, float[] Max) Zero(VfxSequenceProperty property)
        {
            var width = property switch
            {
                VfxSequenceProperty.Color => 4,
                VfxSequenceProperty.Velocity or VfxSequenceProperty.Angle or VfxSequenceProperty.Orbit => 3,
                _ => 1
            };
            return (new float[width], new float[width]);
        }

        private VfxSequenceProperty Property(string word)
        {
            if (word.Contains("SIZEEXT", StringComparison.Ordinal))
            {
                return VfxSequenceProperty.Stretch;
            }

            if (word.Contains("SIZE", StringComparison.Ordinal))
            {
                return VfxSequenceProperty.Size;
            }

            if (word.Contains("EVENTTIMER", StringComparison.Ordinal))
            {
                return VfxSequenceProperty.Clock;
            }

            (string Word, VfxSequenceProperty Property)[] table =
            [
                ("REDCOLOR", VfxSequenceProperty.Red), ("GREENCOLOR", VfxSequenceProperty.Green), ("BLUECOLOR", VfxSequenceProperty.Blue),
                ("ALPHA", VfxSequenceProperty.Alpha), ("COLOR", VfxSequenceProperty.Color), ("VELOCITYX", VfxSequenceProperty.VelocityX),
                ("VELOCITYY", VfxSequenceProperty.VelocityY), ("VELOCITYZ", VfxSequenceProperty.VelocityZ), ("VELOCITY", VfxSequenceProperty.Velocity),
                ("PARTANGLEX", VfxSequenceProperty.AngleX), ("PARTANGLEY", VfxSequenceProperty.AngleY), ("PARTANGLEZ", VfxSequenceProperty.AngleZ),
                ("PARTANGLE", VfxSequenceProperty.Angle),
                // The original creates the Y event for an X local angle.
                ("LOCALANGLEX", VfxSequenceProperty.OrbitY), ("LOCALANGLEY", VfxSequenceProperty.OrbitY), ("LOCALANGLEZ", VfxSequenceProperty.OrbitZ),
                ("LOCALANGLE", VfxSequenceProperty.Orbit)
            ];
            foreach (var (text, property) in table)
            {
                if (word.Contains(text, StringComparison.Ordinal))
                {
                    if (text == "LOCALANGLEX")
                    {
                        report.Add($"{name}: localanglex sets the Y local angle as in the original");
                    }

                    return property;
                }
            }

            throw new ScriptRejected($"unknown event {word}");
        }

        private (float Low, float High) ReadNumber()
        {
            if (Current.Kind == Kind.Random)
            {
                Next();
                Expect(Kind.Open, "(");
                var low = Number(Current, "a random minimum");
                Next();
                Expect(Kind.Comma, ",");
                var high = Number(Current, "a random maximum");
                Next();
                Expect(Kind.Close, ")");
                return (low, high);
            }

            if (Current.Kind != Kind.Number)
            {
                // The original leaves the property at 0 and resumes at this token.
                report.Add($"{name}: '{Current.Text}' is not a number; the value stays 0");
                return (0f, 0f);
            }

            var value = Number(Current, "a number");
            Next();
            return (value, value);
        }

        private void ReadVector(out float[] min, out float[] max)
        {
            if (!TryVector(out min, out max))
            {
                min = [0f, 0f, 0f];
                max = [0f, 0f, 0f];
            }
        }

        private bool TryVector(out float[] min, out float[] max)
        {
            min = [0f, 0f, 0f];
            max = [0f, 0f, 0f];
            if (Current.Kind != Kind.Xyz)
            {
                report.Add($"{name}: '{Current.Text}' is not a vector; the value stays 0");
                return false;
            }

            Next();
            Expect(Kind.Open, "(");
            for (var axis = 0; axis < 3; axis++)
            {
                (min[axis], max[axis]) = ReadNumber();
                Expect(axis < 2 ? Kind.Comma : Kind.Close, axis < 2 ? "," : ")");
            }

            return true;
        }

        private bool TryColor(out float[] min, out float[] max)
        {
            min = [0f, 0f, 0f, 0f];
            max = [0f, 0f, 0f, 0f];
            if (Current.Kind != Kind.Rgba)
            {
                report.Add($"{name}: '{Current.Text}' is not a colour; the value stays 0");
                return false;
            }

            Next();
            Expect(Kind.Open, "(");
            for (var channel = 0; channel < 4; channel++)
            {
                (min[channel], max[channel]) = ReadNumber();
                Expect(channel < 3 ? Kind.Comma : Kind.Close, channel < 3 ? "," : ")");
            }

            return true;
        }

        private void Expect(Kind kind, string what)
        {
            if (Current.Kind != kind)
            {
                throw new ScriptRejected($"expected {what}, found '{Current.Text}'");
            }

            Next();
        }

        private static float Number(Token token, string what)
        {
            if (token.Kind != Kind.Number)
            {
                throw new ScriptRejected($"expected {what}, found '{token.Text}'");
            }

            return Atof(token.Text);
        }
    }

    private static VfxBlend Blend(string mode) => mode switch
    {
        "BLEND_ALPHA" => VfxBlend.Alpha,
        "BLEND_COLOR" => VfxBlend.Color,
        "BLEND_ADDCOLOR" => VfxBlend.AddColor,
        "BLEND_SHADOW" => VfxBlend.Shadow,
        "BLEND_INVSHADOW" => VfxBlend.InvShadow,
        _ => VfxBlend.Additive
    };

    // Original +Z is effect space -Z: a range's Z bounds swap when negated.
    private static (float[] Min, float[] Max) Flip(float[] min, float[] max) => ([min[0], min[1], -max[2]], [max[0], max[1], -min[2]]);

    // The longest numeric prefix, as the C library reads it (atof).
    private static float Atof(string text)
    {
        var length = text.Length;
        while (length > 0)
        {
            if (float.TryParse(text.AsSpan(0, length), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            length--;
        }

        return 0f;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var builder = new System.Text.StringBuilder();
        var state = 0;
        void Add()
        {
            if (builder.Length > 0)
            {
                var word = builder.ToString();
                tokens.Add(new Token(Classify(word), word.StartsWith('"') ? word : word.ToUpperInvariant()));
                builder.Clear();
            }
        }

        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            switch (state)
            {
                case 0:
                    if (!char.IsWhiteSpace(c))
                    {
                        builder.Append(c);
                        state = c == '/' && index + 1 < text.Length && text[index + 1] == '/' ? 3 : c == '"' ? 2 : 1;
                        if (state == 3)
                        {
                            builder.Clear();
                        }
                    }

                    break;
                case 1:
                    if (char.IsWhiteSpace(c))
                    {
                        Add();
                        state = 0;
                    }
                    else if (c is '(' or ')' or ',' or '"' or '{' or '}' or '/')
                    {
                        if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
                        {
                            Add();
                            state = 3;
                        }
                        else
                        {
                            Add();
                            if (c == '"')
                            {
                                builder.Append(c);
                                state = 2;
                            }
                            else
                            {
                                builder.Append(c);
                                Add();
                            }
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
                case 2:
                    builder.Append(c);
                    if (c == '"' && builder.Length > 1)
                    {
                        Add();
                        state = 0;
                    }

                    break;
                case 3:
                    if (c == '\n')
                    {
                        builder.Clear();
                        state = 0;
                    }

                    break;
            }
        }

        if (state != 3)
        {
            Add();
        }

        return tokens;
    }

    private static Kind Classify(string word)
    {
        var first = word[0];
        if (char.IsDigit(first) || first == '-')
        {
            return Kind.Number;
        }

        switch (first)
        {
            case '=':
                return Kind.Equals;
            case ',':
                return Kind.Comma;
            case '(':
                return Kind.Open;
            case ')':
                return Kind.Close;
            case '{':
                return Kind.OpenBrace;
            case '}':
                return Kind.CloseBrace;
            case '"':
                return Kind.Quote;
        }

        var upper = word.ToUpperInvariant();
        (string Text, Kind Kind)[] keywords =
        [
            ("PARTICLESYSTEM", Kind.System), ("EVENTSEQUENCE", Kind.Sequence), ("RANDOM", Kind.Random), ("XYZ", Kind.Xyz), ("RGBA", Kind.Rgba),
            ("FADE", Kind.Fade), ("OUTLENGTH", Kind.OutLength), ("INLENGTH", Kind.InLength), ("INITIAL", Kind.Initial), ("FINAL", Kind.Final),
            ("TEXTURE", Kind.Texture)
        ];
        foreach (var (text, kind) in keywords)
        {
            if (upper.Contains(text, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        if (upper == "SO")
        {
            return Kind.So;
        }

        if (upper == "AT")
        {
            return Kind.At;
        }

        (string Text, Kind Kind)[] properties =
        [
            ("PARTICLETYPE", Kind.ParticleType), ("TYPE_", Kind.TypeMode), ("BLEND_", Kind.BlendMode), ("SOURCEBLENDMODE", Kind.BlendProperty),
            ("DESTBLENDMODE", Kind.BlendProperty), ("LIFETIME", Kind.NumericProperty), ("EMITRATE", Kind.NumericProperty),
            ("NUMPARTICLES", Kind.NumericProperty), ("LOOPS", Kind.NumericProperty), ("DELAY", Kind.NumericProperty), ("SPAWNDIR", Kind.VectorProperty),
            ("GRAVITY", Kind.VectorProperty), ("EMITRADIUS", Kind.VectorProperty), ("POSITION", Kind.SystemVector), ("SIZE", Kind.EventNumber),
            ("EVENTTIMER", Kind.EventNumber), ("PARTANGLE", Kind.EventAngle), ("LOCALANGLE", Kind.EventAngle), ("VELOCITY", Kind.EventVector),
            ("COLOR", Kind.EventColor)
        ];
        foreach (var (text, kind) in properties)
        {
            if (upper.Contains(text, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return Kind.Unknown;
    }
}
