namespace PristonTale.Contracts.World;

// What an entity is, in the world view, the presence descriptions (the values are the wire byte), the client and entity
// collision. None stands for an entity not described yet. Append only.
public enum EntityKind : byte
{
    None = 0,
    Player = 1,
    Monster = 2,
    Npc = 3
}
