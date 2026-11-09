namespace PristonTale.Simulation.Skills;

// Flight of skill projectiles: they leave at the motion's event and land after the distance at this speed, on the
// server (damage) and on the client (the projectile effect) alike.
public static class SkillProjectile
{
    public const float UnitsPerSecond = 900f;

    public static float FlightMs(float distance) => distance <= 0f ? 0f : distance / UnitsPerSecond * 1000f;
}
