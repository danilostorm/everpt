namespace PristonTale.Contracts.Motion;

// The state code of a clip in a model's motion table (the files store these values; the server reads the same tables
// for timing). A model may hold several clips under one code; MotionState picks the code with MotionCodes.Of.
public enum MotionCode : uint
{
    Any = 0,
    Stand = 0x40,
    Walk = 0x50,
    Run = 0x60,
    FallDown = 0x80,
    Attack = 0x100,
    Damage = 0x110,
    Dead = 0x120,
    Sometime = 0x130,
    Eat = 0x140,
    Skill = 0x150,
    FallStand = 0x170,
    FallDamage = 0x180,
    Restart = 0x200,
    Warp = 0x210,
    Yahoo = 0x220,
    Hammer = 0x300,

    TalkAh = 0x400,
    TalkEe = 0x410,
    TalkOh = 0x420,
    TalkEyeBlink = 0x430,
    TalkSmile = 0x440,
    TalkAnger = 0x450,
    TalkSorrow = 0x460,
    TalkStartled = 0x470,
    TalkNeutral = 0x480,
    TalkSpecial = 0x490
}

public static class MotionCodes
{
    // The damage and eat clips play at a frame step of 92 against the 80 of rate 1, on the server's timing and the
    // client's playback alike.
    public const float DamageAndEatRate = 92f / 80f;

    // The clip a motion state plays.
    public static MotionCode Of(MotionState state) => state switch
    {
        MotionState.Walk => MotionCode.Walk,
        MotionState.Run => MotionCode.Run,
        MotionState.Attack => MotionCode.Attack,
        MotionState.Damage => MotionCode.Damage,
        MotionState.Dead => MotionCode.Dead,
        MotionState.Skill => MotionCode.Skill,
        MotionState.Eat => MotionCode.Eat,
        MotionState.Warp => MotionCode.Warp,
        MotionState.Yahoo => MotionCode.Yahoo,
        _ => MotionCode.Stand
    };
}
