namespace PristonTale.Contracts.Combat;

// A combat event on the presence timeline: the server emits it, the wire carries the value as a byte and the client plays
// it. Append only.
public enum CombatEventKind : byte
{
    None = 0,
    SwingBegin = 1,
    Hit = 2,
    PotionBegin = 3,
    PotionApplied = 4,
    MonsterSkillImpact = 5,

    // A skill or periodic effect restored life; Damage carries the amount.
    Heal = 6,

    // The target evaded a monster's attack through a skill effect.
    Evade = 7,

    // A dead player rose again (a revive skill); Damage carries the life it returns with.
    Revive = 8,

    // A player blocked a monster's normal attack; no damage reaches them.
    Block = 9,

    // A player's swing missed its target (its weapon's miss sound plays for everyone near).
    Miss = 10,

    // A player gained a level: the level-up burst and chime for everyone near.
    LevelUp = 11,

    // A player trained a skill rank: the skill-learn light and sound for everyone near.
    SkillLearned = 12
}
