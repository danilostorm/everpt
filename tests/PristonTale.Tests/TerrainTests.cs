using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PristonTale.Application.World;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class TerrainTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("server stage collision reader matches the client SMD reader in Priston axes", ReaderParityAsync);
        yield return ("field terrain blocks walls and high steps, settles drops and spawns on the floor", TerrainStepAsync);
        yield return ("movement reports off the floor or through walls are rejected and take the floor height", ReportTerrainAsync);
        yield return ("a player saved where the stage has no floor enters at the field's first start point", JoinWithoutFloorAsync);
        yield return ("many small movement reports in one tick share one distance budget", ReportBudgetAsync);
        yield return ("small movement reports cannot pass a wall but may cut a corner", ReportWallSegmentsAsync);
        yield return ("a long movement report climbs a ramp but not through a wall beyond it", ReportRampAsync);
        yield return ("a falling player keeps its height between reports and may fall over a gap, not walk into one", ReportFallAsync);
        yield return ("a movement report cannot climb a step higher than the client can, wall or no wall", ReportClimbAsync);
    }

    private static Task ReaderParityAsync()
    {
        var bytes = Stage(
            [new(0, 10, 0), new(200, 10, 0), new(200, 30, 200), new(0, 30, 200), new(100, 0, 100), new(100, 120, 100), new(100, 0, 140)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6)]);
        var path = Path.Combine(Path.GetTempPath(), $"terrain-{Guid.NewGuid():N}.smd");
        File.WriteAllBytes(path, bytes);
        try
        {
            var server = StageCollisionReader.Read(bytes);
            var clientStage = new PristonSmdReader().ReadStage(path);
            Assert.True(clientStage.Materials.Single().MeshState == StageCollisionReader.CheckFace, "fixture MeshState offset disagrees with the client reader");
            var client = new PristonCollisionWorld(clientStage);
            Assert.True(server.CollisionTriangleCount == 3 && server.FloorTriangleCount == 2, "check-face triangles were not classified");
            foreach (var (x, z) in new[] { (10f, 10f), (100f, 50f), (190f, 190f), (50f, 150f) })
            {
                Assert.True(server.TryGetFloorHeight(x, z, out var serverHeight), "server lost the floor");
                Assert.True(client.TryGetFloorHeight(new Microsoft.Xna.Framework.Vector3(x, 0, -z), out var clientHeight), "client lost the floor");
                Assert.Near(clientHeight, serverHeight, 0.001f);
            }

            Assert.True(!server.TryGetFloorHeight(500, 500, out _), "floor reported outside the stage");
            Assert.True(StageCollisionReader.Read(Stage([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [(0, 1, 2)], meshState: 0)).CollisionTriangleCount == 0,
                "a face without the check-face flag became collision");
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static Task TerrainStepAsync()
    {
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 0, 100), new(400, 0, 100), new(400, 0, 400), new(0, 0, 400), new(200, 0, 0), new(200, 200, 0), new(200, 0, 400),
             new(300, 50, 0), new(400, 50, 0), new(400, 50, 100), new(300, 50, 100)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (7, 8, 9), (7, 9, 10)])));
        Assert.True(terrain.TryStep(new Vector3(50, 0, 150), new Vector3(60, 5, 160), out var walked) && walked == new Vector3(60, 0, 160),
            "open floor step was blocked or kept the client height");
        Assert.True(!terrain.TryStep(new Vector3(190, 0, 300), new Vector3(210, 0, 300), out var blocked) && blocked == new Vector3(190, 0, 300),
            "a wall did not block the step");
        Assert.True(!terrain.TryStep(new Vector3(350, 0, 150), new Vector3(350, 0, 90), out _), "a 50-unit ledge was climbed in one step");
        Assert.True(terrain.TryStep(new Vector3(350, 50, 90), new Vector3(350, 50, 150), out var fallen) && fallen.Y == 0, "stepping off a ledge did not fall");
        Assert.True(terrain.PlaceOnFloor(new Vector3(350, 0, 50)).Y == 50, "spawns did not use the highest floor");
        Assert.True(terrain.SettleNear(new Vector3(100, 20, 150), FieldTerrain.DropTolerance).Y == 0, "a drop did not settle on the floor below");
        Assert.True(FieldTerrain.None.TryStep(Vector3.Zero, new Vector3(5, 7, 5), out var free) && free == new Vector3(5, 7, 5),
            "a field without geometry changed movement");
        return Task.CompletedTask;
    }

    private static async Task JoinWithoutFloorAsync()
    {
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 40, 0), new(400, 40, 0), new(400, 40, 400), new(0, 40, 400)], [(0, 1, 2), (0, 2, 3)])));
        Assert.True(!terrain.TryPlaceOnFloor(new Vector3(900, 0, 900), out _) && terrain.TryPlaceOnFloor(new Vector3(100, 0, 100), out var placed)
            && placed.Y == 40, "the floor query accepted a point outside the stage or missed the floor");

        var fixture = new ChatFixture(terrain: terrain, startPoints: [new FieldPoint(50, 150)]);
        var lost = await fixture.JoinAsync("Lost", 900, 900);
        var kept = await fixture.JoinAsync("Kept", 300, 200);
        Assert.True(lost.Session.SpawnPosition == new Vector3(50, 40, 150), "a player saved off the stage did not enter at the start point");
        Assert.True(kept.Session.SpawnPosition == new Vector3(300, 40, 200), "a player saved on the floor was moved");
    }

    private static Task ReportTerrainAsync()
    {
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 4, 0), new(400, 4, 0), new(400, 4, 400), new(0, 4, 400), new(200, 0, 0), new(200, 200, 0), new(200, 0, 400)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6)])));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Ground");
        var player = new WorldPlayer(1, new Vector3(150, 0, 100), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(170, 99, 100), 0f, true, true, 0, 500, terrain) == MovementVerdict.Accepted
            && player.Position == new Vector3(170, 4, 100), "an on-floor report was rejected or kept the client height");
        Assert.True(player.ReportMovement(new Vector3(190, 4, 100), 0f, true, true, 0, 800, terrain) == MovementVerdict.Accepted,
            "a short report beside a wall was rejected");
        Assert.True(player.ReportMovement(new Vector3(260, 4, 100), 0f, true, true, 0, 1500, terrain) == MovementVerdict.Rejected
            && player.Position.X == 190, "a long report through a wall was accepted");
        var edge = new WorldPlayer(2, new Vector3(390, 4, 100), character, false, default);
        Assert.True(edge.ReportMovement(new Vector3(450, 4, 100), 0f, true, true, 0, 500, terrain) == MovementVerdict.Rejected,
            "a report off the stage floor was accepted");
        return Task.CompletedTask;
    }

    private static Task ReportBudgetAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Budget");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        var budget = Locomotion.RunUnitsPerSecond(character.Derived.MoveSpeed) * 1.35f * 0.5f + 32f;
        var rejected = 0;
        for (var report = 1; report <= 40; report++)
        {
            if (player.ReportMovement(new Vector3(player.Position.X + 20, 0, 0), 0f, true, true, 0, 1000) == MovementVerdict.Rejected)
            {
                rejected++;
            }
        }

        Assert.True(rejected > 0 && player.Position.X <= budget + 0.01f, $"one tick of small reports moved {player.Position.X} units, budget {budget}");
        var held = player.Position.X;
        Assert.True(player.ReportMovement(new Vector3(held + 20, 0, 0), 0f, true, true, 0, 2000) == MovementVerdict.Accepted,
            "the budget did not refill with time");
        return Task.CompletedTask;
    }

    private static Task ReportWallSegmentsAsync()
    {
        var vertices = new List<Vector3> { new(0, 4, 0), new(400, 4, 0), new(400, 4, 400), new(0, 4, 400) };
        var faces = new List<(int, int, int)> { (0, 1, 2), (0, 2, 3) };
        void Wall(float x0, float z0, float x1, float z1)
        {
            var first = vertices.Count;
            vertices.AddRange([new(x0, 0, z0), new(x1, 0, z1), new(x1, 300, z1), new(x0, 300, z0)]);
            faces.Add((first, first + 1, first + 2));
            faces.Add((first, first + 2, first + 3));
        }

        Wall(100, 250, 100, 400);
        Wall(250, 150, 300, 150);
        Wall(300, 150, 300, 200);
        Wall(300, 200, 250, 200);
        Wall(250, 200, 250, 150);
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(vertices.ToArray(), faces.ToArray())));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Segments");
        var player = new WorldPlayer(1, new Vector3(90, 4, 300), character, false, default);
        var now = 1000L;
        foreach (var x in new[] { 95f, 103f, 108f, 113f, 118f })
        {
            now += 50;
            player.ReportMovement(new Vector3(x, 4, 300), 0f, true, false, 0, now, terrain);
            Assert.True(player.Position.X < 100f, $"small reports carried the player through a wall to {player.Position.X}");
        }

        var cutter = new WorldPlayer(2, new Vector3(240, 4, 160), character, false, default);
        Assert.True(cutter.ReportMovement(new Vector3(256, 4, 146), 0f, true, false, 0, 500, terrain) == MovementVerdict.Accepted,
            "a report cutting a pillar corner by a unit was rejected");
        Assert.True(cutter.Position.X < 250f || cutter.Position.Z < 150f, $"the server kept a point inside the pillar: {cutter.Position}");
        return Task.CompletedTask;
    }

    private static Task ReportRampAsync()
    {
        // Ground to x 100, a ramp rising 100 units to x 250, a plateau beyond it and a wall across the plateau at x 400.
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 0, 0), new(100, 0, 0), new(100, 0, 400), new(0, 0, 400),
             new(100, 0, 0), new(250, 100, 0), new(250, 100, 400), new(100, 0, 400),
             new(250, 100, 0), new(600, 100, 0), new(600, 100, 400), new(250, 100, 400),
             new(400, 100, 0), new(400, 100, 400), new(400, 400, 400), new(400, 400, 0)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (8, 9, 10), (8, 10, 11), (12, 13, 14), (12, 14, 15)])));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Ramp");
        var player = new WorldPlayer(1, new Vector3(60, 0, 200), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(60, 0, 200), 0f, false, false, 0, 500, terrain) == MovementVerdict.Accepted,
            "a standing report was rejected");
        Assert.True(player.ReportMovement(new Vector3(240, 0, 200), 0f, true, true, 0, 3000, terrain) == MovementVerdict.Accepted,
            "a long report up a ramp was taken for a wall");
        Assert.True(MathF.Abs(player.Position.X - 240) < 0.01f && MathF.Abs(player.Position.Y - 93.33f) < 0.1f,
            $"the ramp report did not stand on the ramp: {player.Position}");
        Assert.True(player.ReportMovement(new Vector3(300, 0, 200), 0f, true, true, 0, 4000, terrain) == MovementVerdict.Accepted
            && player.Position.Y == 100, "a report onto the plateau was rejected or missed its floor");
        Assert.True(player.ReportMovement(new Vector3(450, 100, 200), 0f, true, true, 0, 5000, terrain) == MovementVerdict.Rejected
            && player.Position.X == 300, "a long report crossed the wall beyond the ramp");
        return Task.CompletedTask;
    }

    private static Task ReportClimbAsync()
    {
        // Ground to x 100 and a plateau from x 100, 60 units higher, with no wall face between them.
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 0, 0), new(100, 0, 0), new(100, 0, 400), new(0, 0, 400),
             new(100, 60, 0), new(400, 60, 0), new(400, 60, 400), new(100, 60, 400)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7)])));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Climb");
        var player = new WorldPlayer(1, new Vector3(60, 0, 200), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(60, 0, 200), 0f, false, false, 0, 500, terrain) == MovementVerdict.Accepted,
            "a standing report was rejected");
        Assert.True(player.ReportMovement(new Vector3(140, 0, 200), 0f, true, true, 0, 1500, terrain) == MovementVerdict.Rejected
            && player.Position.X == 60, $"a report climbed a 60-unit step: {player.Position}");
        return Task.CompletedTask;
    }

    private static Task ReportFallAsync()
    {
        // A ledge 300 high to x 100 over ground to x 140, water (no floor) to x 170, then ground again.
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 300, 0), new(100, 300, 0), new(100, 300, 400), new(0, 300, 400),
             new(0, 0, 0), new(140, 0, 0), new(140, 0, 400), new(0, 0, 400),
             new(170, 0, 0), new(400, 0, 0), new(400, 0, 400), new(170, 0, 400)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (8, 9, 10), (8, 10, 11)])));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Fall");
        var player = new WorldPlayer(1, new Vector3(90, 300, 200), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(90, 300, 200), 0f, false, false, 0, 1000, terrain) == MovementVerdict.Accepted
            && player.Position.Y == 300, "a report on the ledge was rejected or left it");
        Assert.True(player.ReportMovement(new Vector3(120, 0, 200), 0f, true, true, 0, 1100, terrain) == MovementVerdict.Accepted,
            "walking off the ledge was rejected");
        // The 0.1 s are spread along the path, a third of which is still on the ledge.
        Assert.True(player.Position.Y < 300 - Falling.SnapDistance && player.Position.Y >= 300 - Falling.UnitsPerSecond * 0.1f,
            $"the server did not fall at most 0.1 s worth off the ledge: {player.Position.Y}");
        Assert.True(player.ReportMovement(new Vector3(200, 0, 200), 0f, true, true, 0, 1300, terrain) == MovementVerdict.Accepted,
            "falling over the water to the far ground was rejected");
        Assert.True(player.Position.X == 200 && player.Position.Y > 0 && player.Position.Y < 300, $"the fall over the water ended at {player.Position}");
        Assert.True(player.ReportMovement(new Vector3(200, 0, 200), 0f, false, false, 0, 1600, terrain) == MovementVerdict.Accepted
            && player.Position.Y == 0, "the fall did not land on the ground");

        var walker = new WorldPlayer(2, new Vector3(180, 0, 200), character, false, default);
        Assert.True(walker.ReportMovement(new Vector3(180, 0, 200), 0f, false, false, 0, 1000, terrain) == MovementVerdict.Accepted,
            "a report on the ground was rejected");
        Assert.True(walker.ReportMovement(new Vector3(130, 0, 200), 0f, true, true, 0, 1300, terrain) == MovementVerdict.Rejected
            && walker.Position.X == 180, "walking on the ground across the water was accepted");
        return Task.CompletedTask;
    }

    // Minimal stage SMD: file header (556), stage header (262260), one material group with one unused material record,
    // then 28-byte vertices and faces, matching the reader offsets of both client and server.
    internal static byte[] Stage(Vector3[] vertices, (int A, int B, int C)[] faces, uint meshState = StageCollisionReader.CheckFace)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var signature = new byte[24];
        Encoding.ASCII.GetBytes("SMD Stage data Ver 0.72").CopyTo(signature, 0);
        writer.Write(signature);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[32 * 16]);
        writer.Write(0);
        writer.Write(new byte[256 * 256 * 4]);
        writer.Write(new byte[12 * 4]);
        writer.Write(vertices.Length);
        writer.Write(faces.Length);
        writer.Write(new byte[5 * 4 + 3 * 4 + 2 * 4 + 4 * 4]);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1u);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(new byte[64]);
        var material = new byte[320];
        BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(168), meshState);
        writer.Write(material);
        foreach (var vertex in vertices)
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((int)(vertex.X * 256));
            writer.Write((int)(vertex.Y * 256));
            writer.Write((int)(vertex.Z * 256));
            writer.Write(new byte[8]);
        }

        foreach (var (a, b, c) in faces)
        {
            writer.Write(0u);
            writer.Write(0);
            writer.Write((ushort)a);
            writer.Write((ushort)b);
            writer.Write((ushort)c);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(new byte[8]);
        }

        return stream.ToArray();
    }
}
