using System.Globalization;

namespace PristonTale.VfxImport;

// The call list of an effect group script: Name(arg, ...) with string and number arguments, "--" comments to the end
// of the line and optional semicolons. Nothing else of the scripting language is used by the game data.
internal static class ScriptCalls
{
    public static IEnumerable<(string Call, List<object> Arguments)> Read(string text, string name)
    {
        var position = 0;
        while (true)
        {
            SkipSpace(text, ref position);
            if (position >= text.Length)
            {
                yield break;
            }

            var start = position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            {
                position++;
            }

            if (position == start)
            {
                throw new InvalidDataException($"Effect script {name} has unexpected '{text[position]}'.");
            }

            var call = text[start..position];
            SkipSpace(text, ref position);
            if (position >= text.Length || text[position] != '(')
            {
                throw new InvalidDataException($"Effect script {name}: {call} is not a call.");
            }

            position++;
            var arguments = new List<object>();
            SkipSpace(text, ref position);
            while (position < text.Length && text[position] != ')')
            {
                if (text[position] == '"')
                {
                    var end = text.IndexOf('"', position + 1);
                    if (end < 0)
                    {
                        throw new InvalidDataException($"Effect script {name} has an unterminated string.");
                    }

                    arguments.Add(text[(position + 1)..end].Replace("\\\\", "\\", StringComparison.Ordinal));
                    position = end + 1;
                }
                else
                {
                    var numberStart = position;
                    while (position < text.Length && text[position] is not (',' or ')') && !char.IsWhiteSpace(text[position]))
                    {
                        position++;
                    }

                    arguments.Add(Evaluate(text[numberStart..position], name));
                }

                SkipSpace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    SkipSpace(text, ref position);
                }
            }

            if (position >= text.Length)
            {
                throw new InvalidDataException($"Effect script {name}: {call} is not closed.");
            }

            position++;
            SkipSpace(text, ref position);
            if (position < text.Length && text[position] == ';')
            {
                position++;
            }

            yield return (call, arguments);
        }
    }

    // A number, or a sum such as "70-10" that the scripting language evaluates.
    private static float Evaluate(string token, string name)
    {
        var total = 0f;
        var start = 0;
        for (var index = 1; index <= token.Length; index++)
        {
            if (index < token.Length && (token[index] is not ('+' or '-') || token[index - 1] is 'e' or 'E'))
            {
                continue;
            }

            if (!float.TryParse(token.AsSpan(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var term) || !float.IsFinite(term))
            {
                throw new InvalidDataException($"Effect script {name}: '{token}' is not a number.");
            }

            total += term;
            start = index;
        }

        return total;
    }

    private static void SkipSpace(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            else if (position + 1 < text.Length && text[position] == '-' && text[position + 1] == '-')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
