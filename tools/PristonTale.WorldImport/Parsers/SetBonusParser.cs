namespace PristonTale.WorldImport.Parsers;

public sealed record ParsedSetBonus(int Life, float Absorb, float Block, float LifeRegen, float ManaRegen, float StaminaRegen);

// The bonus of an item set as its files state it, in the comment under the set-effect header: terms separated by
// commas, each "<stat>+<value>"; a term without a value takes the next value written ("흡수력, 블럭율+2.0"). 체력 is life,
// 흡수력 absorption, 블럭율 block; a term ending in 재생 is regeneration of the pools its letters name (체/생 life, 근
// stamina, 기 mana), or of the terms before it when it is bare ("체, 근, 기, 재생+2.0"; "근기생 재생+30").
public static class SetBonusParser
{
    public static bool TryParse(string text, out ParsedSetBonus bonus)
    {
        bonus = new ParsedSetBonus(0, 0f, 0f, 0f, 0f, 0f);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var pending = new List<string>();
        var any = false;
        foreach (var raw in text.Split(','))
        {
            var term = raw.Trim();
            var plus = term.IndexOf('+');
            if (plus < 0)
            {
                pending.Add(term);
                continue;
            }

            if (!float.TryParse(term[(plus + 1)..].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            var name = term[..plus].Trim();
            var names = new List<string>(pending) { name };
            pending.Clear();
            if (name == "재생")
            {
                names.RemoveAt(names.Count - 1);
                if (!Regen(string.Concat(names), value, ref bonus))
                {
                    return false;
                }

                any = true;
                continue;
            }

            foreach (var each in names)
            {
                if (!Apply(each, value, ref bonus))
                {
                    return false;
                }
            }

            any = true;
        }

        return any && pending.Count == 0;
    }

    private static bool Apply(string name, float value, ref ParsedSetBonus bonus)
    {
        if (name.EndsWith("재생", StringComparison.Ordinal))
        {
            return Regen(name[..^2].Replace(" ", "", StringComparison.Ordinal), value, ref bonus);
        }

        switch (name)
        {
            case "체력":
            case "생명력":
                bonus = bonus with { Life = bonus.Life + (int)value };
                return true;
            case "흡수력":
                bonus = bonus with { Absorb = bonus.Absorb + value };
                return true;
            case "블럭율":
            case "블록율":
                bonus = bonus with { Block = bonus.Block + value };
                return true;
            default:
                return false;
        }
    }

    private static bool Regen(string pools, float value, ref ParsedSetBonus bonus)
    {
        if (pools.Length == 0)
        {
            return false;
        }

        foreach (var pool in pools)
        {
            switch (pool)
            {
                case '체':
                case '생':
                    bonus = bonus with { LifeRegen = bonus.LifeRegen + value };
                    break;
                case '근':
                    bonus = bonus with { StaminaRegen = bonus.StaminaRegen + value };
                    break;
                case '기':
                    bonus = bonus with { ManaRegen = bonus.ManaRegen + value };
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
