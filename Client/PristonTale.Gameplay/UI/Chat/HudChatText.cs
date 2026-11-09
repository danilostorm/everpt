namespace PristonTale.Gameplay.UI.Chat;

// Chat wrapping: each line takes as many characters as fit the pixel width (at most maxChars), backs up to the last space
// when it would cut a word, drops the spaces around the cut, and stops after maxLines lines.
public static class HudChatText
{
    public static List<string> Wrap(string text, int maxPixels, int maxLines, int maxChars, Func<string, int> measure)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxPixels <= 0 || maxLines <= 0 || maxChars <= 0)
        {
            return lines;
        }

        var position = 0;
        while (position < text.Length && lines.Count < maxLines)
        {
            var newline = text.IndexOfAny(['\r', '\n'], position);
            var end = newline < 0 ? text.Length : newline;
            if (end == position)
            {
                lines.Add("");
                position = SkipNewline(text, end);
                continue;
            }

            var limit = Math.Min(end - position, maxChars);
            int low = 0;
            int high = limit;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (measure(text.Substring(position, middle)) <= maxPixels)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var count = Math.Max(1, low);
            if (count < end - position)
            {
                var space = text.LastIndexOf(' ', position + count - 1);
                if (space > position && space < position + count)
                {
                    count = space - position;
                }
            }

            lines.Add(text.Substring(position, count).TrimEnd(' ', '\t'));
            position += count;
            while (position < end && text[position] is ' ' or '\t')
            {
                position++;
            }

            if (position == end && newline >= 0)
            {
                position = SkipNewline(text, end);
            }
        }

        return lines;
    }

    private static int SkipNewline(string text, int at) =>
        text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? at + 2 : at + 1;
}

// The wrapped lines of texts drawn every frame (the chat log, speech bubbles), so a frame measures only new texts. The
// font and its scale are fixed, so a text and its line cap decide the lines; the cache starts over past Capacity texts.
public sealed class HudWrapCache(int maxPixels, int maxChars)
{
    private const int Capacity = 256;
    private readonly Dictionary<(string Text, int MaxLines), List<string>> _lines = new();

    public IReadOnlyList<string> Wrap(string text, int maxLines, Func<string, int> measure)
    {
        if (_lines.TryGetValue((text, maxLines), out var lines))
        {
            return lines;
        }

        if (_lines.Count >= Capacity)
        {
            _lines.Clear();
        }

        lines = HudChatText.Wrap(text, maxPixels, maxLines, maxChars, measure);
        _lines[(text, maxLines)] = lines;
        return lines;
    }
}
