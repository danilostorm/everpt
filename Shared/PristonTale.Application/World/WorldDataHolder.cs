namespace PristonTale.Application.World;

public sealed class WorldDataHolder
{
    private volatile WorldData _current = WorldData.Empty;

    public WorldData Current
    {
        get => _current;
        set => _current = value;
    }
}
