namespace PristonTale.WorldImport.Parsers;

// The name texts a definition carries, by stored key (KeyMaps.LabelKeys without the asterisk); the last non-empty value
// of a key wins. Korean text is not kept, nor text saved through another code page (it decodes as Korean syllables).
public static class AuthoredLabels
{
    public static bool TryRead(string key, Tokenizer t, Dictionary<string, string> labels)
    {
        if (KeyMaps.LabelKey(key) is not { } stored)
        {
            return false;
        }

        var text = t.NextValueOrQuoted().Trim();
        if (text.Length > 0 && !IsKorean(text))
        {
            labels[stored] = text;
        }

        return true;
    }

    // Hangul syllables, jamo or compatibility jamo anywhere in the text.
    public static bool IsKorean(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\uAC00' and <= '\uD7A3' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F')
            {
                return true;
            }
        }

        return false;
    }

    // The first label present among the keys, in the order given ("*E_NAME", "*NAME"...); empty when none is.
    public static string First(IReadOnlyDictionary<string, string> labels, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (labels.TryGetValue(KeyMaps.StoredKey(key), out var text))
            {
                return text;
            }
        }

        return "";
    }
}
