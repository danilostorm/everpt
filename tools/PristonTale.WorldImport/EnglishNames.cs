namespace PristonTale.WorldImport;

// English names and lines where the supplied data gives them only in Portuguese or Korean, or misspells them, as
// Etervale shows every name and line in English. A monster's or NPC's label is keyed by its file (variants of one
// identity are labelled apart), an item's name by its code. Variants of a monster the original set also has take that
// monster's English label verbatim.
internal static class EnglishNames
{
    private static readonly Dictionary<string, string> Monsters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["38_TP_Boss_GladiadorVermelho.inf"] = "Red Gladiator",
        ["40_ET1_Boss_HoboGolem.inf"] = "Deadly Machine",
        ["43_ET3_BOSS_DevilShire.inf"] = "Demonic Shy",
        ["DVM_LavaGolem.inf"] = "Lava Golem",
        ["Event_Melancia.inf"] = "Wevyn, the Hunter",
        ["Cristal_Cao_Infernal.inf"] = "Hell Hound",
        ["Cristal_DarkSpecter.inf"] = "Dark Specter",
        ["Cristal_Duende_Gelo.inf"] = "icegoblin",
        ["Cristal_HeadCutter.inf"] = "Head Cutter",
        ["Cristal_Hobgoblin.inf"] = "Hobgoblin",
        ["Cristal_Hopy.inf"] = "HOPY",
        ["Cristal_Igolation.inf"] = "Igolation",
        ["Cristal_KingHopy.inf"] = "Hopy King",
        ["Cristal_KingHopy2.inf"] = "Horropion",
        ["Cristal_Valento.inf"] = "Valento",
        ["Cristal_Web2.inf"] = "Web",
        ["30_Leech.inf"] = "Leech",
        ["88_Sadness.inf"] = "Sadness",
        ["S72_HeavyGoblin.inf"] = "Heavy Goblin"
    };

    private static readonly Dictionary<string, string> Items = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BI142"] = "Summon Blacksmith",
        ["BI226"] = "Superior Box (30d)",
        ["DA130"] = "Imperial Armor",
        ["DA230"] = "Imperial Robe",
        ["DB129"] = "Cronus Boots",
        ["DG129"] = "Cronus Gauntlets",
        ["DG131"] = "Hell Gauntlets",
        ["DG132"] = "Guardian Gauntlets",
        ["DS128"] = "Cronus Shield",
        ["DS129"] = "Angel Shield",
        ["DS130"] = "Stygian Shield",
        ["GP114"] = "Mercenary",
        ["GP115"] = "Ricarten Guard",
        ["GP116"] = "Imperial Guard",
        ["OA127"] = "Phantom Amulet",
        ["OA128"] = "Phantom Amulet +1",
        ["OA131"] = "Throne Amulet",
        ["OA132"] = "Enigma Amulet",
        ["OA134"] = "Enigma Amulet +2",
        ["OA135"] = "Enigma Amulet +3",
        ["OA136"] = "Voracious Amulet",
        ["OM127"] = "Rosary Orbital",
        ["OM130"] = "Miranda Orbital",
        ["OS129"] = "Magic Celesto",
        ["OS130"] = "Magic Mirage",
        ["OS131"] = "Magic Inferna",
        ["QW104"] = "Diamond Wing",
        ["OR127"] = "Phantom Ring",
        ["OR128"] = "Phantom Ring +1",
        ["OR130"] = "Phantom Ring +3",
        ["OR131"] = "Throne Ring",
        ["OR132"] = "Enigma Ring",
        ["OR133"] = "Enigma Ring +1",
        ["OR134"] = "Enigma Ring +1",
        ["OR135"] = "Enigma Ring +3",
        ["OR136"] = "Voracious Ring",
        // Its *Name lost its text ("?"); the English name is its B_NAME.
        ["QT105"] = "Mysterious Oil",
        ["WA126"] = "Dragon Axe",
        ["WA131"] = "Deadly Axe",
        ["WC125"] = "Furious Talon",
        ["WC130"] = "Diabolic Talon",
        ["WH127"] = "Dragon Hammer",
        ["WH132"] = "Ruby Hammer",
        ["WM127"] = "Astral Staff",
        ["WM132"] = "Deadly Staff",
        ["WT126"] = "Blessed Javelin",
        ["WT131"] = "Chaotic Javelin",
        ["WS127"] = "Rogue Bow",
        ["WS132"] = "Deadly Bow",
        ["WS133"] = "Sinister Bow",
        ["WS134"] = "Halloween Bow 2M",
        ["WS229"] = "Mirage Sword",
        ["WS234"] = "Deadly Sword",
        ["WS235"] = "Draxos Sword"
    };

    private static readonly Dictionary<string, string> Npcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ribbonhopy.npc"] = "Ribbon Hopy"
    };

    // Authored service texts given only in Korean.
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["공성전 생중계"] = "Siege War Live"
    };

    public static string Text(string text) => Texts.GetValueOrDefault(text, text);

    // English translations of the NPC lines the data gives only in Korean (*대화), by line.
    private static readonly Dictionary<string, string> Dialogs = new(StringComparer.Ordinal)
    {
        ["이번 공성은 어느때보다 치열했던것 같아요"] = "This siege seems to have been fiercer than ever.",
        ["무었이 필요하신가요?"] = "What do you need?",
        ["주둔지의 노점상 자매 중 제가 첫째 랍니다"] = "I'm the eldest of the street vendor sisters at the camp.",
        ["로잘린과 로사 그리고 저의 꿈은 리카르텐 마을에 큰 잡화상을 차리는 거에요"] = "Rozalin, Rossa and I dream of opening a big general store in Ricarten.",
        ["마을로 가시려면 코어를 사용하셔야 한답니다"] = "To go back to town, you'll have to use a core.",
        ["주둔지의 노점상 자매 중 제가 둘째 랍니다"] = "I'm the second of the street vendor sisters at the camp.",
        ["언니 쉘비와 귀여운 동생 로사 그리고 저의 꿈은 리카르텐 마을에 큰 잡화상을 차리는 거에요"] =
            "My big sister Shellbi, our sweet little sister Rossa and I dream of opening a big general store in Ricarten.",
        ["들어오실 땐 워프게이트를 이용하셨지만 나가실 땐 코어를 사용하셔야 한답니다"] = "You came in through the warp gate, but to leave you'll have to use a core.",
        ["발할라의 탑의 광채가 가끔은 이곳에서 보이기도 한답니다"] = "Sometimes you can see the glow of the Tower of Valhalla from here.",
        ["주둔지의 노점상 자매 중 제가 막내 랍니다"] = "I'm the youngest of the street vendor sisters at the camp.",
        ["언니들과 노점상을 운영하는 이유는 큰마을에 잡화상을 차리기 위해서에요"] = "My sisters and I run this stall so we can open a general store in a big town.",
        ["전투에 필요한 물약이 충분히 구비되어 있으니 한번 골라보세요"] = "We have plenty of the potions you need for battle, so take your pick.",
        ["다음에 꼭 들러주세요"] = "Please come by again.",
        ["이것들은 네비스코의 특산품이요 나 말고는 아무도 만들어 낼 수 없는 귀한 것들 뿐이니 둘러 보시구려"] =
            "These are Navisko's specialties, rare pieces no one but me can make. Have a look around.",
        ["이 대륙에서 나보다 강한 무기와 방어구를 생산해 내는 사람은 없으니 잘 골라 보슈"] =
            "No one on this continent forges stronger weapons and armor than I do, so choose well.",
        ["SOD에 참가하는 전사들은 얼마나 강한 장비들을 가지고 있길래 내 가게에는 얼씬도 안 하는지 거참"] =
            "Just how strong is the gear of the warriors in SoD, that they never set foot in my shop? Honestly.",
        ["자 팔 것이 있으면 어서 내놓고 살 것이 있으면 잘 골라 보라고 대륙 최강의 장비들만 팔고 있으니까."] =
            "Come on, if you have something to sell, out with it, and if you're buying, choose well. I only sell the finest gear on the continent.",
        ["이번에 새로 개업한 고덴 입니다. 저희 가게에선 초급장비를 취급하고 있으니 앞으로 잘 부탁 드리겠습니다."] =
            "I'm Goden, and I've just opened my shop. We carry gear for beginners, so I hope you'll come by often.",
        ["자 초급 전사들에게 꼭 필요한 장비들이 준비되어 있습니다."] = "Here we have the gear every beginning warrior needs.",
        ["개업한지 얼마 되지 않았지만 품질은 확실합니다. 자자 둘러 보세요"] = "We haven't been open long, but the quality is guaranteed. Come, have a look around.",
        ["당신이 원하는 장비가 없다면 다리 건너편에 있는 대장장이 거스를 찾아가 보세요."] = "If I don't have the gear you want, try Gus the blacksmith across the bridge.",
        ["초급 전사들에게 필요한 장비들을 준비해 놓고 있습니다. 자주 들려 주세요"] = "I keep the gear beginning warriors need. Drop by often.",
        ["초급 무기에서부터 방어구까지 다양한 장비 들이 있으니 훑어 보세요"] = "I have all sorts of gear, from beginner weapons to armor, so take a look.",
        ["찾으시는 물건이 없다구요? 그럼 다리 건너편에 있는 대장장이 고덴에게 가보십시오"] =
            "Can't find what you're looking for? Then go see Goden the blacksmith across the bridge.",
        ["경비병이 들고 있는 창은 저희 가게에 없습니다. 대신 다른 것들이 많으니 살펴 보세요"] =
            "We don't have the spear the guards carry, but we have plenty of other things, so look around.",
        ["머 대단한 것들은 아니지만 그럭저럭 쓸만할 게요"] = "Nothing grand, but it'll serve you well enough.",
        ["템스크론과 모라이온 모두 사용할 수 있는 다양한 장비들이 있으니 둘러보고 가슈"] =
            "I've got all kinds of gear for both Tempskron and Morion, so have a look before you go.",
        ["그럴싸한 모습은 아니지만 나름대로 쓸만한 장비들을 판매하고 있수"] = "It may not look like much, but the gear I sell is useful in its own way.",
        ["조금씩 돈을 모아 언젠가 이곳에서 큰 마을을 만들어 볼 참이오"] = "I'm saving up bit by bit to build a big town here someday."
    };

    // A *대화 line in English: its translation, the line itself when it is not Korean, or null when it has none.
    public static string? Dialog(string line) =>
        Dialogs.TryGetValue(line, out var english) ? english : Parsers.AuthoredLabels.IsKorean(line) ? null : line;

    public static string Monster(string path, string label) => Monsters.GetValueOrDefault(Path.GetFileName(path), label);

    public static string Item(string code, string name) => Items.GetValueOrDefault(code, name);

    public static string Npc(string path, string label) => Npcs.GetValueOrDefault(Path.GetFileName(path), label);
}
