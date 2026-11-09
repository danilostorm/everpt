namespace PristonTale.Gameplay.UI.Help;

// One page of the game guide: its text (.msg) and picture (.tga) as paths from the asset root; no picture when the page
// has none of its own.
public sealed record LevelGuidePage(string TextPath, string ImagePath);

// The game guide's data. LevelSubj.ini names the subject of each level that has a page ([Level_N] Subj=), and
// LevelUp_Content.ini gives each page's text and picture: the first .msg and the first .tga value of its section, whatever
// their keys. A level's section is CreateCha_<faction> at level 1, Level_N_<faction> where the faction has its own (10
// and 20), else Level_N; Level_1_W and Level_1_D are the pages of a new character's first weapon and armour. The pages of
// levels 9 and 39 lend level 7's picture, which they do not show.
public sealed class LevelGuideBook
{
    public const string Folder = "image/clanImage/Help/AutoHelp/";
    public const string SubjectsFile = Folder + "LevelSubj.ini";
    public const string ContentFile = Folder + "LevelUp_Content.ini";
    public static readonly LevelGuideBook Empty = new([], new Dictionary<string, LevelGuidePage>(StringComparer.OrdinalIgnoreCase));

    private static readonly int[] WithoutPicture = [9, 39];
    private readonly Dictionary<string, LevelGuidePage> _pages;

    private LevelGuideBook(IReadOnlyList<(int Level, string Subject)> subjects, Dictionary<string, LevelGuidePage> pages)
    {
        Subjects = subjects;
        _pages = pages;
    }

    // Every level with a page and its subject, from the lowest.
    public IReadOnlyList<(int Level, string Subject)> Subjects { get; }

    public bool HasPage(int level)
    {
        foreach (var (subjectLevel, _) in Subjects)
        {
            if (subjectLevel == level)
            {
                return true;
            }
        }

        return false;
    }

    // tempskron: the faction of the character's class (Tempskron, else Morion).
    public LevelGuidePage Page(int level, bool tempskron)
    {
        var faction = tempskron ? "Temscron" : "Moraion";
        var page = level == 1 ? Section($"CreateCha_{faction}") : Section($"Level_{level}_{faction}") ?? Section($"Level_{level}");
        return page is not null && Array.IndexOf(WithoutPicture, level) >= 0 ? page with { ImagePath = null } : page;
    }

    public LevelGuidePage StarterPage(bool weapon) => Section(weapon ? "Level_1_W" : "Level_1_D");

    public static LevelGuideBook Parse(string subjects, string content)
    {
        var levels = new List<(int Level, string Subject)>();
        foreach (var (name, values) in ParseIni(subjects))
        {
            if (name.StartsWith("Level_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan("Level_".Length), out var level)
                && values.FirstOrDefault(value => value.Key.Equals("Subj", StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } subject)
            {
                levels.Add((level, subject));
            }
        }

        var pages = new Dictionary<string, LevelGuidePage>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in ParseIni(content))
        {
            var text = values.FirstOrDefault(value => value.Value.EndsWith(".msg", StringComparison.OrdinalIgnoreCase)).Value;
            var image = values.FirstOrDefault(value => value.Value.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)).Value;
            if (text is not null)
            {
                pages[name] = new LevelGuidePage(AssetPath(text), image is null ? null : AssetPath(image));
            }
        }

        levels.Sort((left, right) => left.Level.CompareTo(right.Level));
        return new LevelGuideBook(levels, pages);
    }

    // Sections and their key=value lines in file order; other lines (the files' Korean notes) are skipped.
    internal static List<(string Name, List<(string Key, string Value)> Values)> ParseIni(string text)
    {
        var sections = new List<(string Name, List<(string Key, string Value)> Values)>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                sections.Add((line[1..^1].Trim(), []));
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals > 0 && sections.Count > 0)
            {
                sections[^1].Values.Add((line[..equals].Trim(), line[(equals + 1)..].Trim()));
            }
        }

        return sections;
    }

    private LevelGuidePage Section(string name) => _pages.TryGetValue(name, out var page) ? page : null;

    // The files write their paths with doubled backslashes.
    private static string AssetPath(string value) => value.Replace("\\\\", "/").Replace('\\', '/');
}
