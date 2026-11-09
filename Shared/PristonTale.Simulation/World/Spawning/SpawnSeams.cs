using System.Numerics;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.World.Spawning;

public interface IPlayerProximity
{
    bool AnyPlayerNear(Vector3 worldPosition);
}

public interface ISpawnSink
{
    MonsterInstance Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing);
}
