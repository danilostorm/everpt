using System.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Tests;

internal static class FieldLoadBenchmark
{
    public static async Task RunAsync(string assetRoot)
    {
        var field = RicartenField.Definition;
        var reader = new PristonSmdReader();
        var clock = Stopwatch.StartNew();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        Console.WriteLine($"FIELD stage parse: {clock.Elapsed.TotalMilliseconds:F1} ms; {stage.Faces.Count} faces; {stage.Materials.Count} materials");
        clock.Restart();
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        Console.WriteLine($"FIELD model parse: {clock.Elapsed.TotalMilliseconds:F1} ms; {models.Length} models; {models.Sum(model => model.Materials.Count)} materials");
        clock.Restart();
        _ = new PristonCollisionWorld(stage);
        Console.WriteLine($"FIELD collision: {clock.Elapsed.TotalMilliseconds:F1} ms");
        using var textures = new PristonTextureLoader(null!);
        clock.Restart();
        await textures.PrepareTexturesAsync(Path.GetDirectoryName(stage.Path)!, stage.Materials);
        foreach (var model in models)
        {
            await textures.PrepareTexturesAsync(Path.GetDirectoryName(model.Path)!, model.Materials);
        }

        Console.WriteLine($"FIELD texture preparation: {clock.Elapsed.TotalMilliseconds:F1} ms");
        return;

        string Resolve(string path)
        {
            return Path.Combine(assetRoot, path.Replace('\\', Path.DirectorySeparatorChar));
        }
    }
}
