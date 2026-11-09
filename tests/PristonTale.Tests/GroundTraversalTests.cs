using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Tests;

internal static class GroundTraversalTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("ground traversal rejects overhead roofs and stays below stacked floors", OverheadAsync);
        yield return ("ground traversal climbs connected ramps and blocks unsupported steps", ContinuityAsync);
        yield return ("collision DDA ray and segment queries do not allocate per call", CollisionQueriesDoNotAllocateAsync);
        yield return ("player visual position restores the original two-unit floor offset", PlayerVisualPositionAsync);
        yield return ("a player above its floor falls eight units a frame and lands with the original motions", FallAsync);
    }

    private static Task FallAsync()
    {
        Assert.True(!GroundFall.IsAirborne(108, 100), "eight units above the floor counted as a fall");
        Assert.True(GroundFall.IsAirborne(108.5f, 100), "more than eight units above the floor did not fall");
        Assert.Near(92, GroundFall.Step(100, 0, 1f / 70f));
        Assert.Near(0, GroundFall.Step(5, 0, 1f / 70f));

        var feet = 613f + 400f;
        var frames = 0;
        while (GroundFall.IsAirborne(feet, 613f))
        {
            feet = GroundFall.Step(feet, 613f, 1f / 70f);
            frames++;
        }

        Assert.True(frames is 49 or 50, $"a 400-unit fall took {frames} frames at 70 Hz");
        Assert.True(feet >= 613f && feet - 613f <= GroundFall.SnapDistance, "fall stopped away from the floor");
        Assert.True(GroundFall.Landing(200) == MotionCode.FallStand, "a 200-unit fall landed hard");
        Assert.True(GroundFall.Landing(201) == MotionCode.FallDamage, "a fall over 200 units landed soft");

        var motion = new ActorMotionController();
        motion.ApplyAuthoritative(MotionCode.FallDown);
        Assert.True(motion.ApplyLocomotion(MotionCode.Walk).Kind == MotionCommandKind.None, "walking replaced the fall motion");
        Assert.True(!motion.LocomotionFrozen, "a fall stopped the player from walking");
        foreach (var landing in new[] { MotionCode.FallStand, MotionCode.FallDamage })
        {
            // A landing holds the player until its clip ends and the actor is back to Stand.
            motion.ApplyAuthoritative(landing);
            Assert.True(motion.LocomotionFrozen, $"{landing} let the player walk");
            Assert.True(motion.ApplyLocomotion(MotionCode.Walk).Kind == MotionCommandKind.None, $"walking replaced {landing}");
            motion.ApplyAuthoritative(MotionCode.Stand);
            Assert.True(motion.ApplyLocomotion(MotionCode.Walk).Motion == MotionCode.Walk, "walking did not follow the landing");
        }

        // A steep slope crossed in one long frame is followed; a ledge between two samples is a fall.
        GroundFloorQuery slope = (Vector3 position, float reference, out float height) =>
        {
            height = 100f - position.X * 1.5f;
            return true;
        };
        Assert.True(GroundTraversal.TryFollow(new Vector3(0f, 100f, 0f), new Vector2(40f, 0f), slope, out var down, out var slopeDrop)
            && slopeDrop <= GroundFall.SnapDistance && MathF.Abs(down.Y - 40f) < 0.01f, $"a slope dropped {slopeDrop} between samples");
        GroundFloorQuery ledge = (Vector3 position, float reference, out float height) =>
        {
            height = position.X < 20f ? 100f : 80f;
            return true;
        };
        Assert.True(GroundTraversal.TryFollow(new Vector3(0f, 100f, 0f), new Vector2(40f, 0f), ledge, out _, out var ledgeDrop)
            && ledgeDrop > GroundFall.SnapDistance, $"a 20-unit ledge dropped only {ledgeDrop}");
        return Task.CompletedTask;
    }

    public static void InspectRicarten(string assetRoot)
    {
        var relativePath = RicartenField.Definition.StageSmd.Replace('\\', Path.DirectorySeparatorChar);
        var path = AssetFiles.Resolve(Path.Combine(assetRoot, relativePath));
        var world = new PristonCollisionWorld(new PristonSmdReader().ReadStage(path));
        bool Floor(Vector3 point, float reference, out float height)
        {
            return world.TryGetFloorHeight(point, reference, GroundTraversal.StepUp, out height);
        }

        var gate = Forest1Field.Definition.Gates.Single(value => value.DestinationFieldId == "village-2");
        var checkedColumns = 0;
        var occludedCameras = 0;
        var cameraSettings = new PristonCameraSettings { DefaultPitch = 1f, DefaultDistance = 420 };
        for (var x = gate.X - 400; x <= gate.X + 400; x += 10)
        {
            for (var z = -gate.Z - 400; z <= -gate.Z + 400; z += 10)
            {
                var ray = new Ray(new Vector3(x, 10000, z), Vector3.Down);
                if (!world.TryRaycastFloor(ray, out var roof) || !Floor(roof, roof.Y - 30, out var lower))
                {
                    continue;
                }

                var feet = new Vector3(x, lower, z);
                Assert.True(!GroundTraversal.CanReachSurface(feet, roof, Floor), "Ricarten overhead surface accepted from the lower floor");
                Assert.True(GroundTraversal.TryFollow(feet, new Vector2(x, z), Floor, out var reached), "Ricarten lower layer lost its support");
                Assert.True(reached.Y <= lower + GroundTraversal.StepUp && reached.Y < roof.Y - 0.1f,
                    "Ricarten floor tracking exceeded the step band or snapped to the roof");
                checkedColumns++;
                var camera = new PristonCamera(feet, Vector3.Forward, cameraSettings);
                var expected = camera.Position;
                if (world.TryTraceSegment(camera.Target, expected, out _, out _)
                    && (!world.TryGetFloorHeight(expected, expected.Y, 0f, out var cameraFloor)
                        || expected.Y >= cameraFloor + cameraSettings.CollisionFloorPad))
                {
                    for (var frame = 0; frame < 16; frame++)
                    {
                        camera.UpdateFollow(feet, 1f / 120, world);
                        Assert.True(Vector3.Distance(expected, camera.Position) < 0.001f, "Ricarten occlusion pulled a stationary high camera toward the player");
                    }

                    occludedCameras++;
                }
            }
        }

        Assert.True(checkedColumns > 0, "Ricarten probe found no stacked floors near the entrance gate");
        Console.WriteLine($"GROUND Ricarten: {checkedColumns} stacked columns reject the overhead surface and preserve the lower floor.");
        Assert.True(occludedCameras > 0, "Ricarten probe found no occluded high-camera poses");
        Console.WriteLine($"CAMERA Ricarten: {occludedCameras} occluded high-camera poses preserve their position across 16 follow updates each.");
    }

    private static Task OverheadAsync()
    {
        var world = BuildWorld([(0, 200, 0, 0), (40, 100, 90, 90)]);
        bool Floor(Vector3 point, float reference, out float height)
        {
            return world.TryGetFloorHeight(point, reference, GroundTraversal.StepUp, out height);
        }

        var feet = new Vector3(10, 0, 50);
        var ray = new Ray(new Vector3(70, 200, 50), Vector3.Down);
        Assert.True(world.TryRaycastFloor(ray, out var roof) && roof.Y == 90, "fixture did not pick the tower roof");
        Assert.True(!GroundTraversal.CanReachSurface(feet, roof, Floor), "roof accepted as a reachable click destination");
        Assert.True(GroundTraversal.TryFollow(feet, new Vector2(90, 50), Floor, out var below), "ground beneath tower rejected");
        Assert.Near(0, below.Y);
        Assert.True(GroundTraversal.CanReachSurface(feet, new Vector3(180, 0, 50), Floor), "ground beyond overhead structure rejected");

        for (var frame = 0; frame < 240; frame++)
        {
            var x = 50 + frame % 40;
            Assert.True(Floor(new Vector3(x, 0, 50), below.Y, out var correctedHeight), "correction lost the current floor");
            Assert.Near(0, correctedHeight);
        }

        return Task.CompletedTask;
    }

    private static Task ContinuityAsync()
    {
        var ramp = BuildWorld([(0, 100, 0, 0), (100, 200, 0, 60), (200, 250, 60, 60)]);
        bool RampFloor(Vector3 point, float reference, out float height)
        {
            return ramp.TryGetFloorHeight(point, reference, GroundTraversal.StepUp, out height);
        }

        var start = new Vector3(10, 0, 50);
        Assert.True(GroundTraversal.CanReachSurface(start, new Vector3(240, 60, 50), RampFloor), "connected ramp rejected by absolute height");
        foreach (var frames in new[] { 1, 30, 60, 120, 144 })
        {
            var current = start;
            for (var frame = 1; frame <= frames; frame++)
            {
                var destination = new Vector2(MathHelper.Lerp(10, 240, frame / (float)frames), 50);
                Assert.True(GroundTraversal.TryFollow(current, destination, RampFloor, out current), "ramp failed at another frame rate");
            }

            Assert.Near(60, current.Y);
        }

        var gap = BuildWorld([(0, 80, 0, 0), (90, 150, 0, 0), (150, 200, 25, 25)]);
        bool GapFloor(Vector3 point, float reference, out float height)
        {
            return gap.TryGetFloorHeight(point, reference, GroundTraversal.StepUp, out height);
        }

        Assert.True(!GroundTraversal.TryFollow(start, new Vector2(140, 50), GapFloor, out var stopped), "long frame crossed unsupported gap");
        Assert.True(stopped.X <= 80 && stopped.Y == 0, "rejected step moved past the ledge");
        Assert.True(!GroundTraversal.CanReachSurface(new Vector3(100, 0, 50), new Vector3(180, 25, 50), GapFloor), "25-unit step accepted");
        Assert.True(!GroundTraversal.TryFollow(start, new Vector2(float.NaN, 50), GapFloor, out var invalid) && invalid == start, "invalid destination moved feet");
        return Task.CompletedTask;
    }

    private static Task CollisionQueriesDoNotAllocateAsync()
    {
        var world = BuildWorld([(0, 200, 0, 0)]);
        var ray = new Ray(new Vector3(100, 100, 50), Vector3.Down);
        var segmentStart = new Vector3(10, 50, 50);
        var segmentEnd = new Vector3(190, -50, 50);

        for (var iteration = 0; iteration < 64; iteration++)
        {
            Assert.True(world.TryRaycastFloor(ray, out _), "warmup floor ray missed the fixture");
            Assert.True(world.TryTraceSegment(segmentStart, segmentEnd, out _, out _), "warmup segment missed the fixture");
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 4096; iteration++)
        {
            Assert.True(world.TryRaycastFloor(ray, out _), "measured floor ray missed the fixture");
            Assert.True(world.TryTraceSegment(segmentStart, segmentEnd, out _, out _), "measured segment missed the fixture");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(allocated == 0, $"collision queries allocated {allocated} bytes");
        return Task.CompletedTask;
    }

    private static Task PlayerVisualPositionAsync()
    {
        var body = new Vector3(120, 38, -450);
        var visual = GroundTraversal.ToVisualPosition(body);
        Assert.True(visual == new Vector3(120, 40, -450), "visual actor did not return to the supporting floor");
        return Task.CompletedTask;
    }

    private static PristonCollisionWorld BuildWorld((float Start, float End, float Low, float High)[] strips)
    {
        var vertices = new List<PristonStageVertex>();
        var faces = new List<PristonStageFace>();
        foreach (var strip in strips)
        {
            var first = vertices.Count;
            vertices.Add(new(new Vector3(strip.Start, strip.Low, 0), Color.White));
            vertices.Add(new(new Vector3(strip.Start, strip.Low, 100), Color.White));
            vertices.Add(new(new Vector3(strip.End, strip.High, 100), Color.White));
            vertices.Add(new(new Vector3(strip.End, strip.High, 0), Color.White));
            faces.Add(new(first, first + 1, first + 2, 0, -1));
            faces.Add(new(first, first + 2, first + 3, 0, -1));
        }

        var material = new PristonMaterial(true, 1, 0, 1, false, Vector3.One, 0, 1,
            PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        return new PristonCollisionWorld(new PristonStage("ground-fixture.smd", [material], vertices, faces, [], [], default));
    }
}
