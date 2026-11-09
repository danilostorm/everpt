namespace PristonTale.Contracts.Motion;

// What an entity is doing, as the server decides it and every layer reads it: monster and player state, the world view,
// the presence wire byte (the values) and the client's playback. Append only. MotionCodes maps it to a model's motion
// table code.
public enum MotionState : byte
{
    Stand = 0,
    Walk = 1,
    Run = 2,
    Attack = 3,
    Damage = 4,
    Dead = 5,
    Skill = 6,

    // Drinking a potion.
    Eat = 7,

    // A monster's transformation clip before it turns into another.
    Warp = 8,

    // The cheer (the chat's yahoo word, firecrackers).
    Yahoo = 9
}
