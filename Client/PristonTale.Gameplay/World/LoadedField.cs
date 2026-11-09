using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

internal sealed class LoadedField
{
    public LoadedField(
        FieldDef definition,
        PristonStage stage,
        PristonCollisionWorld collision,
        RenderStageHandle renderHandle)
    {
        Definition = definition;
        Stage = stage;
        Collision = collision;
        RenderHandle = renderHandle;
    }

    public FieldDef Definition { get; }

    public PristonStage Stage { get; }

    public PristonCollisionWorld Collision { get; }

    public RenderStageHandle RenderHandle { get; }
}
