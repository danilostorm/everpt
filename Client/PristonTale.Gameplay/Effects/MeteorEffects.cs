using Microsoft.Xna.Framework;
using PristonTale.Effects;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Effects;

public sealed class MeteorEffects
{
    public const int Capacity = 16;
    public const float Speed = 8f * GameUnits.TicksPerSecond;
    private readonly Projectile[] _projectiles = new Projectile[Capacity];
    private readonly IScriptEffects _effects;
    private readonly PristonTransientLights _lights;
    private readonly IPristonCollisionQuery _collision;
    public int Count { get; private set; }

    public MeteorEffects(IScriptEffects effects, PristonTransientLights lights, IPristonCollisionQuery collision)
    {
        _effects = effects;
        _lights = lights;
        _collision = collision;
    }

    public static (Vector3 Start, Vector3 Velocity) Trajectory(Vector3 target)
    {
        // Fire Worm meteor launch: fixed-point offsets, converted once to scene coordinates.
        var destination = target + new Vector3(0, 0, -1000f / 256f);
        var offset = new Vector3(0, 130000f / 256f, -50000f / 256f);
        return (destination + offset, Vector3.Normalize(-offset) * Speed);
    }

    public bool Spawn(Vector3 target)
    {
        var trajectory = Trajectory(target);
        return SpawnVelocity(trajectory.Start, trajectory.Velocity);
    }

    public bool SpawnTrajectory(Vector3 start, Vector3 destination)
    {
        var delta = destination - start;
        if (!IsFinite(start) || !IsFinite(destination) || delta.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        return SpawnVelocity(start, Vector3.Normalize(delta) * Speed);
    }

    private bool SpawnVelocity(Vector3 start, Vector3 velocity)
    {
        if (Count == Capacity)
        {
            return false;
        }

        var effect = _effects.Spawn("TerrainFire", start, Matrix.Identity);
        _projectiles[Count++] = new Projectile(effect, start, velocity, 0);
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    public void Update(float seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        int index = 0;
        while (index < Count)
        {
            var projectile = _projectiles[index];
            float remaining = MathF.Max(0, 10 - projectile.Age);
            var next = projectile.Position + projectile.Velocity * MathF.Min(seconds, remaining);
            bool hit = _collision.TryTraceSegment(projectile.Position, next, out var contact, out _);
            if (hit || seconds >= remaining)
            {
                projectile.Effect?.Stop();
                _projectiles[index] = _projectiles[--Count];
                _projectiles[Count] = default;
                if (hit)
                {
                    // A small lift keeps the TYPE_TWO ground rings above the depth-tested surface.
                    contact.Y += .1f;
                    _effects.Spawn("FireBigHit1", contact, Matrix.Identity);

                    _lights.Add(contact, new Color(255, 200, 100), 250, 2);
                }

                continue;
            }

            projectile.Effect?.MoveTo(next);
            _projectiles[index++] = projectile with { Position = next, Age = projectile.Age + seconds };
        }
    }

    public void Clear()
    {
        for (int index = 0; index < Count; index++)
        {
            _projectiles[index].Effect?.Stop();
            _projectiles[index] = default;
        }

        Count = 0;
    }

    private readonly record struct Projectile(IScriptEffect Effect, Vector3 Position, Vector3 Velocity, float Age);
}
