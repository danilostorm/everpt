namespace PristonTale.WorldImport;

// The shared line/token reader for the *token value... text format of the authored config files.
// Format rules:
//
// - Words: skip leading space (0x20) and tab (0x09), copy bytes until the next space, tab or CR/LF, i.e.
//   whitespace-split with no comment character.
// - Quoted strings: scan to the first ", copy until the closing ". Names are read as one word; if it began
//   with ", it is re-parsed from the saved start as a quoted string (so names with spaces survive).
//
// There is no comment marker in the format: a // line simply has a leading token that matches
// nothing and is ignored. We reproduce that by only acting on recognised */** keys.
//
// A Tokenizer wraps a single line; NextWord walks words left-to-right and
// QuotedFrom re-reads a quoted string spanning embedded spaces.
public sealed class Tokenizer
{
    private readonly string _line;
    private int _pos;

    public Tokenizer(string line)
    {
        _line = line ?? string.Empty;
        _pos = 0;
    }

    // True once the cursor has consumed the whole line.
    public bool AtEnd
    {
        get
        {
            var p = _pos;
            while (p < _line.Length && IsSpace(_line[p]))
                p++;
            return p >= _line.Length;
        }
    }

    // The leading word (the */** key) without disturbing the cursor for re-reads.
    public static string LeadingKey(string line)
    {
        var t = new Tokenizer(line);
        return t.NextWord();
    }

    // Read the next whitespace-delimited word. Returns the empty string at end of line.
    public string NextWord()
    {
        while (_pos < _line.Length && IsSpace(_line[_pos]))
            _pos++;
        var start = _pos;
        while (_pos < _line.Length && !IsSpace(_line[_pos]))
            _pos++;
        return _line[start.._pos];
    }

    // Read a value that may be a quoted string: peek the next word; if it starts with ", re-read
    // the quoted span (which may contain spaces). Otherwise return the plain word.
    public string NextValueOrQuoted()
    {
        // Skip leading whitespace, remember the start so a quoted re-read sees the opening quote.
        while (_pos < _line.Length && IsSpace(_line[_pos]))
            _pos++;
        if (_pos >= _line.Length)
            return string.Empty;

        if (_line[_pos] == '"')
            return ReadQuoted();

        var start = _pos;
        while (_pos < _line.Length && !IsSpace(_line[_pos]))
            _pos++;
        return _line[start.._pos];
    }

    // Read a quoted string from the cursor (cursor must be at the opening quote or whitespace).
    private string ReadQuoted()
    {
        // Find opening quote.
        while (_pos < _line.Length && _line[_pos] != '"')
            _pos++;
        if (_pos >= _line.Length)
            return string.Empty;
        _pos++; // skip opening quote
        var start = _pos;
        while (_pos < _line.Length && _line[_pos] != '"')
            _pos++;
        var value = _line[start.._pos];
        if (_pos < _line.Length)
            _pos++; // skip closing quote
        return value;
    }

    // Read the next word as an integer (atoi semantics: empty/invalid → 0).
    public int NextInt() => Atoi(NextWord());

    // Read the next word as a non-negative 64-bit value (monster experience passes 2^31), saturating instead of wrapping.
    public ulong NextUInt64() => AtoUInt64(NextWord());

    // Read the next word as a float (atof semantics: empty/invalid → 0).
    public float NextFloat() => Atof(NextWord());

    // Collect every remaining whitespace-delimited word (used for code lists, job names).
    public IReadOnlyList<string> RemainingWords()
    {
        var words = new List<string>();
        while (true)
        {
            var w = NextWord();
            if (w.Length == 0)
                break;
            words.Add(w);
        }
        return words;
    }

    public IReadOnlyList<string> RemainingValues()
    {
        var values = new List<string>();
        while (!AtEnd)
        {
            values.Add(NextValueOrQuoted());
        }

        return values;
    }

    // atoi/atof tolerate trailing garbage and leading sign/whitespace and return 0 on no-leading-digit.
    public static int Atoi(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return 0;
        var i = 0;
        var sign = 1;
        if (s[0] is '+' or '-')
        {
            if (s[0] == '-') sign = -1;
            i++;
        }
        long value = 0;
        var any = false;
        for (; i < s.Length && char.IsDigit(s[i]); i++)
        {
            value = value * 10 + (s[i] - '0');
            any = true;
        }
        return any ? (int)(sign * value) : 0;
    }

    public static ulong AtoUInt64(string s)
    {
        s = s.Trim();
        var i = s.Length > 0 && s[0] == '+' ? 1 : 0;
        UInt128 value = 0;
        for (; i < s.Length && char.IsDigit(s[i]); i++)
        {
            value = UInt128.Min(value * 10 + (uint)(s[i] - '0'), ulong.MaxValue);
        }

        return (ulong)value;
    }

    public static float Atof(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return 0f;
        // Read the leading numeric prefix (C's atof tolerates trailing non-numerics).
        var end = 0;
        var seenDot = false;
        if (end < s.Length && (s[end] == '+' || s[end] == '-'))
            end++;
        while (end < s.Length && (char.IsDigit(s[end]) || (s[end] == '.' && !seenDot)))
        {
            if (s[end] == '.') seenDot = true;
            end++;
        }
        var prefix = s[..end];
        return float.TryParse(prefix, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';
}
