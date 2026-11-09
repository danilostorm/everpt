using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Client.Net.WorldCatalog;

// PortraitOffsetY and PortraitOffsetZ are the authored height and distance correction of the target-frame camera.
public sealed record MonsterRenderInfo(
    uint TemplateId,
    string ModelInxPath,
    float ModelScale,
    string Name,
    int Level,
    MonsterBrood Brood = MonsterBrood.Normal,
    int PortraitOffsetY = 0,
    int PortraitOffsetZ = 0,
    int SoundCode = 0,
    int Class = 0) : ModelRenderInfo(TemplateId, ModelInxPath, ModelScale, Name);
