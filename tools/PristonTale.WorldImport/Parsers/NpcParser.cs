namespace PristonTale.WorldImport.Parsers;

using PristonTale.Simulation.World.Npcs;

// Parses one NPC `.npc` and the name file it links to into a ParsedNpc. Every service key the original reads becomes
// a typed NpcService; keys that share one original setting keep the last value read. Sell lists are at most 32 codes
// per tab. The English name is *E_NAME, then *B_NAME, then *Name; the English dialog is the *E_CHAT lines, else the
// *B_CHAT lines, else the Korean *대화 lines in their English translation (EnglishNames.Dialog). The authored identity
// (`*이름`) is read only to lend a name to a file of the same NPC that has none.
public sealed class NpcParser
{
    private const int MaxDialogLines = 20;
    private const int FirstQuestTriggerCode = 1000;

    public ParsedNpc Parse(string path)
    {
        var lines = SourceTextReader.ReadChain(path);
        var npc = new ParsedNpc { Name = FindName(lines), File = Path.GetFileName(path) };
        var facts = new Facts();
        ApplyLines(lines, npc, facts);
        npc.DisplayName = EnglishNames.Npc(path, Normalize(AuthoredLabels.First(facts.Labels, KeyMaps.NpcNameLocales)));
        var english = KeyMaps.NpcDialogLocales.Select(KeyMaps.StoredKey).Select(key => facts.Dialog.GetValueOrDefault(key))
            .FirstOrDefault(static dialog => dialog is { Count: > 0 });
        npc.Dialog.AddRange(english ?? facts.TranslatedDialog);
        if (english is null)
        {
            npc.UntranslatedDialog.AddRange(facts.Untranslated);
        }
        BuildServices(npc, facts);
        return npc;
    }

    // The original opens a placed NPC over the character record of its placement, so a file that sets no class (*계급)
    // or size (*크기) keeps the placement's: a quest code there makes the NPC a quest giver, first in its click order.
    public static void TakePlacement(ParsedNpc npc, int placedClass, int placedSizeLevel)
    {
        if (npc.Class is null)
        {
            npc.Class = placedClass;
            if (placedClass >= FirstQuestTriggerCode)
            {
                npc.Services.Insert(0, new NpcService(NpcServiceKind.QuestGiver, placedClass));
            }
        }

        npc.SizeLevel ??= placedSizeLevel;
    }

    // A file without an English name takes the first one among the files of its authored identity, in list order;
    // returns the files left without one.
    public static IReadOnlyList<ParsedNpc> ShareNames(IReadOnlyList<ParsedNpc> npcs)
    {
        var unnamed = new List<ParsedNpc>();
        foreach (var npc in npcs.Where(static npc => npc.DisplayName.Length == 0))
        {
            npc.DisplayName = npc.Name.Length == 0
                ? ""
                : npcs.FirstOrDefault(other => other.Name == npc.Name && other.DisplayName.Length > 0)?.DisplayName ?? "";
            if (npc.DisplayName.Length == 0)
            {
                unnamed.Add(npc);
            }
        }

        return unnamed;
    }

    private sealed class Facts
    {
        public readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Dialog = new(StringComparer.Ordinal);
        public readonly List<string> TranslatedDialog = [];
        public readonly List<string> Untranslated = [];
        public bool SvcWarehouse;
        public bool SvcShop;
        public bool SvcSkillMaster;
        public bool SvcJobChange;
        public bool SvcTeleport;
        public bool SvcItemAging;
        public bool SvcSmelting;
        public bool SvcManufacture;
        public bool SvcMixtureReset;
        public bool SvcClan;
        public bool SvcBlessCastle;
        public bool SvcQuest;
        public bool SvcEventBooth;
        public bool SvcCollectMoney;
        public bool SvcGiveMoney;
        public int JobChangeCode;
        public int TeleportCode;
        public int QuestCode;
        public int QuestParam;
        public readonly int[] ShopOrdinals = new int[3];
        public bool ShopHasStock;
        public int ItemMix;
        public int WingQuest;
        public bool WingQuestIsEvent;
        public int StarPoint;
        public int EventBooth;
        public bool EventGift;
        public bool GiftExpress;
        public int BlessCastle;
        public int Polling;
        public string MediaTitle = "";
        public string MediaPath = "";
        public int EventCode;
        public int EventInfo;
        public readonly List<string> Unported = [];
    }

    private static void ApplyLines(IReadOnlyList<SourceLine> lines, ParsedNpc npc, Facts facts)
    {
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
            {
                continue;
            }

            var t = new Tokenizer(line);
            t.NextWord();
            if (ApplyLocale(key, t, facts) || ApplyAppearance(key, t, npc) || ApplyService(key, t, npc, facts))
            {
                continue;
            }

            var unported = false;
            foreach (var (token, label) in KeyMaps.NpcUnportedServices)
            {
                if (key == token)
                {
                    unported = true;
                    if (!facts.Unported.Contains(label))
                    {
                        facts.Unported.Add(label);
                    }
                }
            }

            if (!unported)
            {
                npc.UnknownKeys.Add(key);
            }
        }
    }

    private static bool ApplyLocale(string key, Tokenizer t, Facts facts)
    {
        if (AuthoredLabels.TryRead(key, t, facts.Labels))
        {
            return true;
        }

        if (KeyMaps.DialogKey(key) is { } dialogKey)
        {
            if (!facts.Dialog.TryGetValue(dialogKey, out var lines))
            {
                facts.Dialog[dialogKey] = lines = [];
            }

            AddLine(lines, t.NextValueOrQuoted());
            return true;
        }

        if (key == KeyMaps.CharDialog)
        {
            var line = Normalize(t.NextValueOrQuoted());
            if (EnglishNames.Dialog(line) is { } translated)
            {
                AddLine(facts.TranslatedDialog, translated);
            }
            else if (line.Length > 0)
            {
                facts.Untranslated.Add(line);
            }

            return true;
        }

        // The identity is read by FindName.
        return key == KeyMaps.CharName;
    }

    private static bool ApplyAppearance(string key, Tokenizer t, ParsedNpc npc)
    {
        if (KeyMaps.CharSound.Contains(key, StringComparer.Ordinal))
        {
            npc.SoundCode = CharacterSoundCodes.Resolve(t.NextWord());
            return true;
        }

        switch (key)
        {
            case KeyMaps.MonMoveRange:
                npc.MoveRange = Math.Max(0, (int)t.NextFloat());
                return true;
            case KeyMaps.CharAttribute:
                npc.Hostile = t.NextValueOrQuoted() == KeyMaps.HostileAttribute;
                return true;
            case KeyMaps.MonLevel:
                npc.Level = t.NextInt();
                return true;
            case KeyMaps.CharSize:
                npc.SizeLevel = KeyMaps.ParseSizeLevel(t.NextWord());
                return true;
            case var appear when KeyMaps.SpmInterval.Contains(appear, StringComparer.OrdinalIgnoreCase):
                npc.AppearSlot = t.NextInt();
                npc.AppearCycle = t.NextInt();
                return true;
            case KeyMaps.CharModel:
                npc.Model = t.NextValueOrQuoted();
                return true;
            case "*모델크기":
                npc.ModelScale = t.NextFloat();
                return true;
            case KeyMaps.NpcLinkedFile:
                npc.LinkedFile = t.NextValueOrQuoted();
                return true;
            case KeyMaps.CharPortrait:
                npc.PortraitOffsetY = t.NextInt();
                npc.PortraitOffsetZ = t.NextInt();
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyService(string key, Tokenizer t, ParsedNpc npc, Facts facts)
    {
        switch (key)
        {
            case KeyMaps.NpcWarehouse:
                facts.SvcWarehouse = true;
                return true;
            case KeyMaps.NpcSkillMaster:
                facts.SvcSkillMaster = true;
                return true;
            case KeyMaps.NpcJobChange:
                facts.SvcJobChange = true;
                facts.JobChangeCode = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcTeleport:
                facts.SvcTeleport = true;
                facts.TeleportCode = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcItemMix:
                facts.ItemMix = 1;
                return true;
            case KeyMaps.NpcItemAlchemy:
                facts.ItemMix = 200;
                return true;
            case KeyMaps.NpcItemAging:
                facts.SvcItemAging = true;
                return true;
            case KeyMaps.NpcSmelting:
                facts.SvcSmelting = true;
                return true;
            case KeyMaps.NpcManufacture:
                facts.SvcManufacture = true;
                return true;
            case KeyMaps.NpcMixtureReset:
                facts.SvcMixtureReset = true;
                return true;
            case KeyMaps.NpcClan:
                facts.SvcClan = true;
                return true;
            case KeyMaps.NpcBlessCastle:
                facts.SvcBlessCastle = true;
                facts.BlessCastle = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcEventBooth:
                facts.SvcEventBooth = true;
                facts.EventBooth = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcCollectMoney:
                facts.SvcCollectMoney = true;
                return true;
            case KeyMaps.NpcGiveMoney:
                facts.SvcGiveMoney = true;
                return true;
            case KeyMaps.NpcEventGift:
                facts.EventGift = true;
                return true;
            case KeyMaps.NpcGiftExpress:
                facts.GiftExpress = true;
                return true;
            case KeyMaps.NpcWingQuest:
                facts.WingQuest = OptionalInt(t, 1);
                facts.WingQuestIsEvent = false;
                return true;
            case KeyMaps.NpcQuestEvent:
                facts.WingQuest = OptionalInt(t, 2);
                facts.WingQuestIsEvent = true;
                return true;
            case KeyMaps.NpcStarPoint:
                facts.StarPoint = OptionalInt(t, 20);
                return true;
            case KeyMaps.NpcPolling:
                facts.Polling = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcMediaTitle:
                facts.MediaTitle = EnglishNames.Text(t.NextValueOrQuoted());
                return true;
            case KeyMaps.NpcMediaPath:
                facts.MediaPath = t.NextValueOrQuoted();
                return true;
            case KeyMaps.NpcEventCode:
                facts.EventCode = t.NextInt();
                return true;
            case KeyMaps.NpcEventInfo:
                facts.EventInfo = t.NextInt();
                return true;
            case KeyMaps.MonClass:
                npc.Class = t.NextInt();
                return true;
            case KeyMaps.NpcQuestCode:
                facts.SvcQuest = true;
                facts.QuestCode = t.NextInt();
                facts.QuestParam = t.NextInt();
                return true;
            case KeyMaps.NpcSellWeapon:
                AddShop(npc, 0, facts, t);
                return true;
            case KeyMaps.NpcSellDefence:
                AddShop(npc, 1, facts, t);
                return true;
            case KeyMaps.NpcSellEtc:
                AddShop(npc, 2, facts, t);
                return true;
            default:
                return false;
        }
    }

    // The order in which the original answers a click: quest hooks first, then the shop and the windows it opens.
    private static void BuildServices(ParsedNpc npc, Facts facts)
    {
        var services = npc.Services;
        facts.SvcShop = facts.ShopHasStock;
        if (npc.Class >= FirstQuestTriggerCode)
        {
            services.Add(new NpcService(NpcServiceKind.QuestGiver, npc.Class.Value));
        }

        AddIf(services, facts.SvcQuest, NpcServiceKind.QuestScript, facts.QuestCode, facts.QuestParam);
        AddIf(services, facts.SvcShop, NpcServiceKind.Shop);
        AddIf(services, facts.SvcSkillMaster, NpcServiceKind.SkillMaster, facts.SvcJobChange ? facts.JobChangeCode : 0);
        AddIf(services, facts.SvcWarehouse, NpcServiceKind.Warehouse);
        AddIf(services, facts.ItemMix != 0, NpcServiceKind.ItemMix, facts.ItemMix);
        AddIf(services, facts.SvcSmelting, NpcServiceKind.Smelting);
        AddIf(services, facts.SvcManufacture, NpcServiceKind.Manufacture);
        AddIf(services, facts.SvcCollectMoney, NpcServiceKind.CollectMoney);
        AddIf(services, facts.EventGift, NpcServiceKind.EventGift);
        AddIf(services, facts.GiftExpress, NpcServiceKind.GiftExpress);
        AddIf(services, facts.WingQuest != 0, facts.WingQuestIsEvent ? NpcServiceKind.QuestEvent : NpcServiceKind.WarpGate, facts.WingQuest);
        AddIf(services, facts.SvcTeleport, NpcServiceKind.Teleporter, facts.TeleportCode);
        AddIf(services, facts.StarPoint != 0, NpcServiceKind.StarPoint, facts.StarPoint);
        AddIf(services, facts.SvcGiveMoney, NpcServiceKind.GiveMoney);
        AddIf(services, facts.SvcEventBooth, NpcServiceKind.EventBooth, facts.EventBooth);
        AddIf(services, facts.SvcItemAging, NpcServiceKind.ItemAging);
        AddIf(services, facts.SvcClan, NpcServiceKind.Clan);
        AddIf(services, facts.SvcBlessCastle, NpcServiceKind.BlessCastle, facts.BlessCastle);
        AddIf(services, facts.Polling != 0, NpcServiceKind.Polling, facts.Polling);
        if (facts.MediaTitle.Length > 0 && facts.MediaPath.Length > 0)
        {
            services.Add(new NpcService(NpcServiceKind.Media, Text: facts.MediaPath + "\n" + facts.MediaTitle));
        }

        AddIf(services, facts.SvcMixtureReset, NpcServiceKind.MixtureReset);
        AddIf(services, facts.EventCode != 0 || facts.EventInfo != 0, NpcServiceKind.EventMonster, facts.EventCode, facts.EventInfo);
        foreach (var label in facts.Unported)
        {
            services.Add(new NpcService(NpcServiceKind.Unported, Text: label));
        }
    }

    private static void AddIf(List<NpcService> services, bool present, NpcServiceKind kind, int value = 0, int value2 = 0)
    {
        if (present)
        {
            services.Add(new NpcService(kind, value, value2));
        }
    }

    // Name files wrap their lines at a fixed width with runs of spaces; display text keeps single spaces.
    private static void AddLine(List<string> lines, string text)
    {
        var line = Normalize(text);
        if (line.Length > 0 && !AuthoredLabels.IsKorean(line) && lines.Count < MaxDialogLines)
        {
            lines.Add(line);
        }
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static int OptionalInt(Tokenizer t, int fallback)
    {
        var word = t.NextWord();
        return word.Length == 0 ? fallback : Tokenizer.Atoi(word);
    }

    private static void AddShop(ParsedNpc npc, int tab, Facts facts, Tokenizer t)
    {
        foreach (var code in t.RemainingWords())
        {
            var clean = code.Trim('"');
            if (clean.Length == 0)
            {
                continue;
            }

            if (facts.ShopOrdinals[tab] >= 32)
            {
                break;
            }

            facts.ShopHasStock |= clean != KeyMaps.DropNone;
            npc.Shop.Add((tab, facts.ShopOrdinals[tab]++, clean));
        }
    }

    private static string FindName(IReadOnlyList<SourceLine> lines)
    {
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            if (Tokenizer.LeadingKey(line) == KeyMaps.CharName)
            {
                var t = new Tokenizer(line);
                t.NextWord();
                return t.NextValueOrQuoted();
            }
        }

        return "";
    }
}
