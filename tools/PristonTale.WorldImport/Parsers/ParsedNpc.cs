using PristonTale.Simulation.World.Npcs;

namespace PristonTale.WorldImport.Parsers;

// Importer DTO for one NPC `.npc` (NpcParser) and its linked name file. Services are the typed services in click
// order (the `npc_services` rows). Shop item references are raw 5-character codes, resolved by the Importer.
public sealed class ParsedNpc
{
    // The authored identity, shared by the files of one NPC; a file without an English name takes another's.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name { get; init; } = "";
    // The definition file's name, which is the NPC's identity (field placements name it).
    public string File { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public int PortraitOffsetY { get; set; }
    public int PortraitOffsetZ { get; set; }
    public string Model { get; set; } = "";
    public string LinkedFile { get; set; } = "";
    public int SoundCode { get; set; }
    public float ModelScale { get; set; } = 1f;
    public int MoveRange { get; set; } = 64;
    // *속성 적 makes the character hostile; *레벨 its level; *크기 its shadow size (-1 none); *계급 its class, a quest
    // code from 1000. Null when the file does not set them (NpcParser.TakePlacement).
    public bool Hostile { get; set; }
    public int Level { get; set; }
    public int? SizeLevel { get; set; }
    public int? Class { get; set; }
    // *출현간격 slot, cycle: the NPC appears only when the field's draw modulo the cycle equals the slot (cycle 0: always).
    public int AppearSlot { get; set; }
    public int AppearCycle { get; set; }

    // Keys of the file no rule reads; the importer reports them.
    public List<string> UnknownKeys { get; } = [];

    // The English lines players read (at most 20), and the Korean lines left out for want of a translation.
    public List<string> Dialog { get; } = [];
    public List<string> UntranslatedDialog { get; } = [];

    public List<NpcService> Services { get; } = [];

    // Shop stock: (tab, ordinal, raw item code). tab 0=weapon,1=armour,2=misc.
    public List<(int Tab, int Ordinal, string Code)> Shop { get; } = [];
}
