using PristonTale.Assets;

namespace PristonTale.WorldImport.Parsers;

// Parses one field .spm text file (the monster-spawn config) with the original spawn-table rules.
// Populates the budget knobs + the weighted monster table + boss spawns of the
// supplied ParsedField. Each token accepts a Korean and an ASCII alias.
//
// *출현간격 (interval) is a tick mask: the interval is 1<<n, decremented when above 1
// (so n yields a (1<<n)-1 mask), with an optional 2nd word giving a cooldown that the original
// multiplies by 1000. We store the raw shift result and the ms value separately.
public sealed class SpmParser
{
    public void Parse(string path, ParsedField field)
    {
        foreach (var line in Cp949.ReadLines(path))
        {
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
                continue;
            var t = new Tokenizer(line);
            t.NextWord();

            if (Match(key, KeyMaps.SpmActor))
            {
                var monster = t.NextValueOrQuoted();
                var weight = t.NextInt();
                if (monster.Length > 0)
                    field.MonsterSpawns.Add((monster, weight));
            }
            else if (Match(key, KeyMaps.SpmBossActor))
            {
                var master = t.NextValueOrQuoted();
                var slave = t.NextValueOrQuoted();
                var slaveCount = t.NextInt();
                var hours = new List<int>();
                while (!t.AtEnd && hours.Count < 32)
                {
                    var w = t.NextWord();
                    if (w.Length == 0)
                        break;
                    hours.Add(Tokenizer.Atoi(w));
                }
                if (master.Length > 0)
                    field.Bosses.Add(new ParsedBoss(
                        master, slave.Length == 0 ? null : slave, slaveCount, hours));
            }
            else if (Match(key, KeyMaps.SpmMaxConcurrent))
            {
                field.MaxConcurrentMonsters = t.NextInt();
            }
            else if (Match(key, KeyMaps.SpmInterval))
            {
                var n = t.NextInt();
                var interval = 1 << Math.Clamp(n, 0, 30);
                if (interval > 1)
                    interval--;
                field.SpawnInterval = interval;
                if (!t.AtEnd)
                    field.PerPointCooldownMs = t.NextInt() * 1000;
            }
            else if (Match(key, KeyMaps.SpmPerWave))
            {
                field.MaxPerSpawnPoint = t.NextInt();
            }
        }
    }

    private static bool Match(string key, string[] aliases)
    {
        foreach (var a in aliases)
            if (string.Equals(key, a, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
