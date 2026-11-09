namespace PristonTale.Gameplay.Audio;

// The sound an item makes when it enters, leaves or moves in the pack (image/Sinimage/Sound), by catalog family: the three
// characters of its key first, then its first two, with the items whose sound differs from their family's. Gold rings
// the coin.
public static class ItemSoundRules
{
    public const string Directory = "image/Sinimage/Sound/";
    public const string Coin = "Coin.wav";
    private const string Fallback = "Armlet.wav";

    private static readonly Dictionary<string, string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WA1"] = "Axes.wav",
        ["WC1"] = "Claws.wav",
        ["WH1"] = "Hammer.wav",
        ["WM1"] = "Magicial_weapon.wav",
        ["WP1"] = "Poles.wav",
        ["WS1"] = "Shooters.wav",
        ["WS2"] = "Swords.wav",
        ["WT1"] = "Throwing.wav",
        ["WD1"] = "Swords.wav",
        ["WN1"] = "Magicial_weapon.wav",
        ["WV1"] = "Claws.wav",
        ["DA"] = "Armor.wav",
        ["DB"] = "Boots.wav",
        ["DG"] = "Gloves.wav",
        ["DS"] = "Shields.wav",
        ["OA1"] = "Amulet.wav",
        ["OA2"] = "Armlet.wav",
        ["OM"] = "Magicial_Stuffs.wav",
        ["OR"] = "Ring.wav",
        ["OS"] = "Sheltom.wav",
        ["FO"] = "Sheltom.wav",
        ["PR"] = "Sheltom.wav",
        ["SE"] = "Sheltom.wav",
        ["BS"] = "Sheltom.wav",
        ["WR"] = "Sheltom.wav",
        ["DR"] = "Sheltom.wav",
        ["BC"] = "Sheltom.wav",
        ["BI1"] = "Potion.wav",
        ["BI2"] = "Sheltom.wav",
        ["PL"] = "Potion.wav",
        ["PM"] = "Potion.wav",
        ["PS"] = "Potion.wav",
        ["MA1"] = "Magicial_Stuffs.wav",
        ["MA2"] = "Potion.wav",
        ["GP"] = "Magicial_Stuffs.wav",
        ["QW"] = "Magicial_Stuffs.wav",
        ["GG"] = Coin,
    };

    private static readonly Dictionary<string, string> Items = Keys(
        ("Magicial_Stuffs.wav", ["GG102", "QT104", "QT105", "QT107", "QT108"]),
        ("Sheltom.wav", ["QT106", "QT109", "QT110", "QT111", "QT114", "BI101", "BI102", "BI103", "BI104", "BI105", "BI106", "BI107",
            "BI108", "BI110", "BI111", "BI112", "BI114", "BI145", "BI146", "BI147", "BI148", "BI149", "BI160", "BI161", "BI178",
            "BI185", "BI189", "BI193", "BI194"]),
        ("Ring.wav", ["QT113", "QT116"]),
        ("Potion.wav", ["GF102", "BI213", "BI214", "BI215", "BI216", "BI217", "BI218", "BI219", "BI220", "BI221", "BI222"]),
        ("Armlet.wav", ["BC101", "BC102", "BC103", "BC104", "BC105"]));

    public static IEnumerable<string> Files =>
        Families.Values.Concat(Items.Values).Append(Fallback).Distinct(StringComparer.OrdinalIgnoreCase);

    public static string For(string catalogKey)
    {
        if (string.IsNullOrEmpty(catalogKey))
        {
            return Fallback;
        }

        if (Items.TryGetValue(catalogKey, out var file))
        {
            return file;
        }

        return catalogKey.Length >= 3 && Families.TryGetValue(catalogKey[..3], out file)
            || catalogKey.Length >= 2 && Families.TryGetValue(catalogKey[..2], out file)
            ? file
            : Fallback;
    }

    private static Dictionary<string, string> Keys(params (string File, string[] Keys)[] groups)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, keys) in groups)
        {
            foreach (var key in keys)
            {
                result.Add(key, file);
            }
        }

        return result;
    }
}
