namespace PristonTale.Engine3D.Rendering;

public readonly record struct ActorHandle(int Slot, int Generation)
{
    public static readonly ActorHandle None = new(-1, 0);

    public bool IsValid => Slot >= 0;
}
