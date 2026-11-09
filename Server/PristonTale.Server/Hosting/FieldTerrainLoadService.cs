using System.Collections.Concurrent;
using System.Diagnostics;
using PristonTale.Application.World;
using PristonTale.Simulation.World;
using PristonTale.Assets;

namespace PristonTale.Server.Hosting;

// Loads every field's stage collision at startup, as the original server does, which also
// refuses to start without its stages. A missing assets directory or stage stops startup unless
// Assets:AllowMissingTerrain is set, which leaves those fields without terrain checks (tests and development only).
public sealed class FieldTerrainLoadService(
    WorldCatalog world,
    FieldTerrainCatalog terrain,
    IConfiguration configuration,
    ILogger<FieldTerrainLoadService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var allowMissing = configuration.GetValue("Assets:AllowMissingTerrain", false);
        var root = configuration["Assets:Root"] is { Length: > 0 } configured ? configured : AssetFiles.FindAssetsRoot();
        if (root is null || !Directory.Exists(root))
        {
            if (!allowMissing)
            {
                throw new InvalidOperationException("No assets directory found. Set Assets:Root, or Assets:AllowMissingTerrain=true to run without terrain.");
            }

            logger.LogWarning("No assets directory found (set Assets:Root); fields run without terrain collision.");
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var loaded = new ConcurrentDictionary<int, FieldTerrain>();
        var triangles = 0L;
        var failures = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(world.AllFields, cancellationToken, async (field, ct) =>
        {
            var path = string.IsNullOrEmpty(field.StageSmd) ? null : AssetFiles.ResolveFile(root, field.StageSmd);
            if (path is null)
            {
                failures.Add($"field {field.Id} ({field.FieldCode}): stage '{field.StageSmd}' not found");
                return;
            }

            try
            {
                var mesh = StageCollisionReader.Read(await File.ReadAllBytesAsync(path, ct));
                loaded[field.FieldCode] = new FieldTerrain(mesh);
                Interlocked.Add(ref triangles, mesh.CollisionTriangleCount);
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"field {field.Id} ({field.FieldCode}): stage '{field.StageSmd}' unreadable: {ex.Message}");
            }
        });

        if (!failures.IsEmpty)
        {
            var report = string.Join("; ", failures.Order(StringComparer.Ordinal));
            if (!allowMissing)
            {
                throw new InvalidOperationException($"Field terrain incomplete ({failures.Count} stages): {report}. Set Assets:AllowMissingTerrain=true to start anyway.");
            }

            logger.LogWarning("Fields without terrain collision: {Report}", report);
        }

        terrain.Publish(new Dictionary<int, FieldTerrain>(loaded));
        var memory = (GC.GetTotalMemory(forceFullCollection: true) - memoryBefore) / (1024.0 * 1024.0);
        logger.LogInformation("Field terrain loaded: {Loaded}/{Total} stages, {Triangles} collision triangles, {Memory:F0} MiB in {Elapsed:F0} ms.",
            loaded.Count, world.AllFields.Count, triangles, memory, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
