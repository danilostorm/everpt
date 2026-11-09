namespace PristonTale.Effects.Vfx;

public enum VfxComponentKind
{
    // An effect model (SMD; an ".ase" name resolves to the ".smd" beside it), turned with the effect's facing.
    Model,
    // A camera-facing quad.
    Billboard,
    // A quad lying flat on the ground plane.
    Ground,
    // A quad from BeamFrom to BeamTo, turned towards the camera around that axis.
    Beam,
    // A pool of short-lived billboards or beams.
    Particles
}

public enum VfxComponentEventKind
{
    Color,
    Size
}

// Where a component's offset points: unturned world axes, turned with the effect's facing, or the opposite way.
public enum VfxOffsetTurn
{
    None,
    Facing,
    Reverse
}

public enum VfxSpawnShape
{
    Point,
    // Uniform in the SpawnMin..SpawnMax box.
    Box,
    // At a distance in SpawnMin.X..SpawnMax.X, turned by angles drawn in 0..4 radians about X, Y and Z.
    Sphere,
    // A cube of jitter SpawnMin.Y..SpawnMax.Y pushed out by a radius SpawnMin.X..SpawnMax.X, turned 0..8 radians about Y.
    Doughnut
}

// A keyed change of a component value at a time on its clock. Set events replace the value; a ramp event is a target:
// the event before it moves the value linearly so the target is reached at the ramp's time, and a ramp that is reached
// changes nothing by itself. Values: colour [r, g, b, a] 0..255 (display encoded); size [x, y, z] in world units.
public sealed record VfxComponentEvent(float T, VfxComponentEventKind Kind, bool Ramp, float[] Value);

// One element of an effect group: a model, a quad or a particle pool, with its own clock, loops and keyed events.
// Positions are effect space (world units, Y up, -Z forward); colours are 0..255 display encoded like the original
// materials and converted to linear when drawn; times in seconds.
public sealed record VfxComponentDefinition
{
    public VfxComponentKind Kind { get; init; } = VfxComponentKind.Billboard;
    public VfxAnchor Anchor { get; init; } = VfxAnchor.Point;
    public string Bone { get; init; } = "";
    // Moves with the anchor while alive instead of staying where the effect started.
    public bool Follow { get; init; }
    public float[] Offset { get; init; } = [0f, 0f, 0f];
    public VfxOffsetTurn OffsetTurn { get; init; } = VfxOffsetTurn.None;
    public string Texture { get; init; } = "";
    // Numbered texture animation: "<stem>1.<ext>" .. "<stem><n>.<ext>" from Texture "<stem>.<ext>", FrameTime seconds each.
    public int Frames { get; init; }
    public float FrameTime { get; init; } = 0.5f;
    public string Model { get; init; } = "";
    public string Skeleton { get; init; } = "";
    // Model clip length in frames at 30 per second; a loop ends when it is reached.
    public float ModelFrames { get; init; }
    // Loops before the component ends; 0 repeats until the effect is stopped.
    public int Loop { get; init; }
    public float Delay { get; init; }
    // Quads: seconds per loop. Particles: lifetime range of each particle.
    public float[] EndTime { get; init; } = [0f, 0f];
    public float[] Color { get; init; } = [255f, 255f, 255f, 255f];
    // Full width and height; particles draw each in SizeMin..SizeMax.
    public float[] SizeMin { get; init; } = [20f, 20f];
    public float[] SizeMax { get; init; } = [20f, 20f];
    public VfxBlend Blend { get; init; } = VfxBlend.Additive;
    public float[] BeamFrom { get; init; } = [0f, 0f, 0f];
    public float[] BeamTo { get; init; } = [0f, 100f, 0f];
    public VfxSpawnShape Spawn { get; init; } = VfxSpawnShape.Point;
    public float[] SpawnMin { get; init; } = [0f, 0f, 0f];
    public float[] SpawnMax { get; init; } = [0f, 0f, 0f];
    // Particles per loop; the pool holds at most 100 live particles.
    public float Count { get; init; } = 50f;
    public float Rate { get; init; } = 30f;
    public float[] VelocityMin { get; init; } = [-10f, -10f, -10f];
    public float[] VelocityMax { get; init; } = [10f, 10f, 10f];
    // Particles move outwards from the source at a speed drawn in VelocityMin.X..VelocityMax.X.
    public bool Outward { get; init; }
    // Particles draw as beams along a direction drawn like the velocity, SizeMin/Max Y long.
    public bool Beams { get; init; }
    public VfxComponentEvent[] Events { get; init; } = [];
}
