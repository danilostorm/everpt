namespace PristonTale.Effects.Vfx;

public enum VfxSequenceShape
{
    // Camera-facing quad, turned in view space by the particle angle.
    Billboard,
    // Quad flat on the ground plane, turned by the particle angle.
    Ground,
    // A vertical segment turned by the particle angle, drawn as a camera-facing strip of the particle size.
    Upright,
    // A strip through the particle's past positions (Stretch points; 0 draws from its previous position).
    Trail
}

public enum VfxSequenceProperty
{
    Size,
    // The quad height (0 keeps it square); for trails, the number of remembered positions.
    Stretch,
    Color,
    Red,
    Green,
    Blue,
    Alpha,
    Velocity,
    VelocityX,
    VelocityY,
    VelocityZ,
    // Degrees about X, Y and Z of the drawn quad.
    Angle,
    AngleX,
    AngleY,
    AngleZ,
    // Degrees turning the particle's offset from its source.
    Orbit,
    OrbitY,
    OrbitZ,
    // Sets the particle's event clock: the events after that time play again.
    Clock
}

public enum VfxVelocityMode
{
    Vector,
    // Away from the source at the drawn X speed.
    Outward,
    // Towards the source at the drawn X speed.
    Inward
}

// A keyed particle change. Set events draw a value in Min..Max per particle; a ramp event is a target the event before
// moves towards (see VfxComponentEvent). Units: sizes in world units, colours 0..255, velocities in units per second,
// angles in degrees.
public sealed record VfxSequenceEvent
{
    public float T { get; init; }
    public bool Ramp { get; init; }
    public VfxSequenceProperty Set { get; init; }
    public float[] Min { get; init; } = [0f];
    public float[] Max { get; init; } = [0f];
    public VfxVelocityMode Mode { get; init; } = VfxVelocityMode.Vector;
}

// A particle stream: Count live particles at most, Loops x Count in total (0 streams until stopped), born at a rate
// drawn each frame, each with a lifetime and an offset drawn in the Radius box, moved by velocity events and gravity
// (drawn each frame), and keyed by its events on its own clock. Effect space: Y up, -Z forward; particle offsets turn
// with the effect's facing (a stand-alone effect has none).
public sealed record VfxSequenceDefinition
{
    public string Texture { get; init; } = "";
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public VfxSequenceShape Shape { get; init; } = VfxSequenceShape.Billboard;
    public int Count { get; init; } = 100;
    public int Loops { get; init; }
    public float[] Rate { get; init; } = [0f, 0f];
    public float Delay { get; init; }
    public float[] Lifetime { get; init; } = [0f, 0f];
    public float[] RadiusMin { get; init; } = [0f, 0f, 0f];
    public float[] RadiusMax { get; init; } = [0f, 0f, 0f];
    public float[] GravityMin { get; init; } = [0f, 0f, 0f];
    public float[] GravityMax { get; init; } = [0f, 0f, 0f];
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public string Bone { get; init; } = "";
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    // Live particles move with the anchor (their source follows it) instead of staying where they were born.
    public bool Follow { get; init; }
    public VfxSequenceEvent[] Events { get; init; } = [];
}
