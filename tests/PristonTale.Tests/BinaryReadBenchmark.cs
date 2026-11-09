using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Assets;

namespace PristonTale.Tests;

internal static class BinaryReadBenchmark
{
    public static void Run(string assetRoot)
    {
        Console.WriteLine($"PARSING {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; processors={Environment.ProcessorCount}");
        var bytes = new byte[64];
        Encoding.ASCII.GetBytes("Bip01 Pelvis").CopyTo(bytes, 0);
        var cursor = new BinaryCursor(bytes);
        Measure("fixed-string-32", 500_000, () =>
        {
            cursor.Position = 0;
            GC.KeepAlive(cursor.FixedString(32));
        });

        bytes.AsSpan().Clear();
        BitConverter.GetBytes(256).CopyTo(bytes, 0);
        BitConverter.GetBytes(256).CopyTo(bytes, 20);
        BitConverter.GetBytes(256).CopyTo(bytes, 40);
        BitConverter.GetBytes(256).CopyTo(bytes, 60);
        var matrixSum = 0f;
        Measure("fixed-matrix", 500_000, () =>
        {
            cursor.Position = 0;
            matrixSum += PristonBinaryUtil.ReadFixedMatrix(cursor).M11;
        });
        GC.KeepAlive(matrixSum);

        var stagePath = AssetFiles.Resolve(Path.Combine(assetRoot, "Field/forest/fore-1.smd"));
        Measure("fore-1-stage", 10, () =>
        {
            var stage = new PristonSmdReader().ReadStage(stagePath);
            Assert.True(stage.Materials.Count == 752 && stage.Faces.Count == 35519, "benchmark asset differs from the recorded fore-1 fixture");
        });
    }

    private static void Measure(string name, int iterations, Action operation)
    {
        for (var warmup = 0; warmup < Math.Min(iterations, 10_000); warmup++)
        {
            operation();
        }

        for (var run = 0; run < 5; run++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                operation();
            }

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine($"PARSING {name} run={run + 1}; ns/op={elapsed * 1_000_000 / iterations:F2}; bytes/op={(double)bytes / iterations:F2}; iterations={iterations}");
        }
    }
}
