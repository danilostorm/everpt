using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World;

// The local player's fall (Falling): a drop of more than 32 shows the fall motion, and landing after more than 200
// shows the hard landing instead of the soft one, as the original does.
public static class GroundFall
{
    public const float SnapDistance = Falling.SnapDistance;
    public const float MotionDistance = 32f;
    public const float HardLandingDistance = 200f;

    public static bool IsAirborne(float feet, float floor) => Falling.IsAirborne(feet, floor);

    public static float Step(float feet, float floor, float dt) => Falling.Step(feet, floor, dt);

    public static MotionCode Landing(float fallen) =>
        fallen > HardLandingDistance ? MotionCode.FallDamage : MotionCode.FallStand;
}
