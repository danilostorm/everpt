using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Tests;

// "shade": an NPC on a floor in the open and the same NPC on a floor whose authored light is dark (the art's shade under
// a tree, which the leaves' sparse shadow map does not reproduce), under the 10:00 sun. The actor must darken with the
// floor: its luminance in the shade over its luminance in the open stays within ShadeTolerance of the floor's own
// ratio (an actor lit as in the open everywhere stayed bright under a tree while the floor around it darkened).
internal sealed partial class SceneLookProbe
{
    private const float ShadeHour = 10f;
    private const float ShadeTolerance = 1.5f;
    private static readonly Color ShadeFloorLight = new(120, 120, 120);
    private static readonly Vector3 ShadeSpot = new(-250f, 0f, 0f);
    private static readonly Vector3 OpenSpot = new(250f, 0f, 0f);
    private PristonActor? _shadeActor;

    private void LoadShade(PristonAssetCache cache)
    {
        var reader = new PristonSmdReader(cache);
        var ricarten = reader.ReadStage(AssetFiles.Resolve(Path.Combine(_root, "Field/Ricarten/village-2.smd")));
        var material = ricarten.Materials.First(m => m.InUse && string.Equals(m.TextureName, SceneryTexture, StringComparison.OrdinalIgnoreCase));
        var vertices = new List<PristonStageVertex>();
        var faces = new List<PristonStageFace>();
        var links = new List<PristonTexLink>();
        AddHorizontalQuad(ShadeSpot, 200f, ShadeFloorLight, vertices, faces, links);
        AddHorizontalQuad(OpenSpot, 200f, Color.White, vertices, faces, links);
        var stage = new PristonStage(
            Path.Combine(_root, "Field/Ricarten/shade-probe.smd"),
            [material],
            vertices,
            faces,
            links,
            [],
            new BoundingBox(new Vector3(-600f, -10f, -600f), new Vector3(600f, 600f, 600f)));
        _renderer.BackgroundColor = new Color(30, 32, 36);
        _renderer.DayLengthSeconds = 0f;
        _renderer.SetTimeOfDay(ShadeHour);
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _preparation = Task.Run(async () =>
        {
            var actor = new PristonActorLoader(cache).Load(_root, Path.Combine(_root, "char/npc/TN-003/TN-003.inx"));
            await _renderer.PrepareActorResourcesAsync(actor);
            _shadeActor = actor;
        });
    }

    private static void AddHorizontalQuad(Vector3 centre, float half, Color light, List<PristonStageVertex> vertices, List<PristonStageFace> faces, List<PristonTexLink> links)
    {
        var first = vertices.Count;
        vertices.Add(new PristonStageVertex(centre + new Vector3(-half, 0f, -half), light));
        vertices.Add(new PristonStageVertex(centre + new Vector3(half, 0f, -half), light));
        vertices.Add(new PristonStageVertex(centre + new Vector3(half, 0f, half), light));
        vertices.Add(new PristonStageVertex(centre + new Vector3(-half, 0f, half), light));
        faces.Add(new PristonStageFace(first, first + 2, first + 1, 0, links.Count));
        links.Add(new PristonTexLink(new Vector2(0f, 0f), new Vector2(4f, 4f), new Vector2(4f, 0f), 0));
        faces.Add(new PristonStageFace(first, first + 3, first + 2, 0, links.Count));
        links.Add(new PristonTexLink(new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(4f, 4f), 0));
    }

    private void BuildShade()
    {
        foreach (var spot in new[] { ShadeSpot, OpenSpot })
        {
            var world = Matrix.CreateTranslation(spot);
            _actors.Add((_renderer.AddActor(new PristonActorInstance(_shadeActor!, world, MotionCode.Stand)), world));
        }
    }

    private void DrawShade()
    {
        var camera = ShadeCamera(OpenSpot);
        _camera.SetScriptedView(GraphicsDevice, camera, OpenSpot + new Vector3(0f, 45f, 0f));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        if (_frames != 150)
        {
            return;
        }

        var open = MeasureShade(OpenSpot, _actors[1].Handle, "shade-open");
        var shaded = MeasureShade(ShadeSpot, _actors[0].Handle, "shade-shaded");
        var floorRatio = shaded.Floor / Math.Max(open.Floor, 1e-3);
        var actorRatio = shaded.Actor / Math.Max(open.Actor, 1e-3);
        Console.WriteLine($"LOOK shade: floor {shaded.Floor:F1} in the shade against {open.Floor:F1} in the open (ratio {floorRatio:F2}); "
            + $"actor {shaded.Actor:F1} against {open.Actor:F1} (ratio {actorRatio:F2})");
        Assert.True(open.Floor > 20.0 && open.Actor > 10.0, "shade: the open floor or actor is not lit");
        Assert.True(actorRatio <= floorRatio * ShadeTolerance + 0.03 && actorRatio >= floorRatio / ShadeTolerance - 0.03,
            $"shade: the actor keeps {actorRatio:F2} of its light in the shade, the floor {floorRatio:F2}");
        Exit();
    }

    // From the sun's side, above: the actor's lit side faces the camera.
    private static Vector3 ShadeCamera(Vector3 spot)
    {
        var towardSun = PristonDayNightCycle.TowardSunAt(ShadeHour);
        var across = Vector3.Normalize(new Vector3(towardSun.X, 0f, towardSun.Z));
        return spot + across * 170f + new Vector3(0f, 100f, 0f);
    }

    // The actor at the centre of its bounds (its torso), the floor beside it.
    private (double Actor, double Floor) MeasureShade(Vector3 spot, ActorHandle handle, string name)
    {
        Assert.True(_renderer.TryGetActorBounds([handle], out var bounds), $"{name}: the actor has no bounds");
        _camera.SetScriptedView(GraphicsDevice, ShadeCamera(spot), spot + new Vector3(0f, 45f, 0f));
        var pixels = Capture(_camera, name);
        var towardSun = PristonDayNightCycle.TowardSunAt(ShadeHour);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.Up, new Vector3(towardSun.X, 0f, towardSun.Z)));
        var (actor, _) = Sample(pixels, (bounds.Min + bounds.Max) * 0.5f, 2);
        var (floor, _) = Sample(pixels, spot + side * 90f, 12);
        return (actor, floor);
    }
}
