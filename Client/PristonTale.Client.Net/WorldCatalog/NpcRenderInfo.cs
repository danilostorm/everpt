using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.WorldCatalog;

// PortraitOffsetY and PortraitOffsetZ are the authored height and distance correction of the target-frame camera.
public sealed record NpcRenderInfo(
    uint TemplateId,
    string ModelInxPath,
    float ModelScale,
    string Name,
    NpcServiceSet Services,
    int SoundCode = 0,
    int PortraitOffsetY = 0,
    int PortraitOffsetZ = 0)
    : ModelRenderInfo(TemplateId, ModelInxPath, ModelScale, Name);
