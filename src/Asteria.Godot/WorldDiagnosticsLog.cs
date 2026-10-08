using System.Diagnostics;
using System.Globalization;
using System.Text;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client;

/// <summary>
/// Bounded periodic diagnostics for the active client process. Unlike F3
/// telemetry, collection is always enabled; writes occur only per interval.
/// No world state is owned by this observer.
/// </summary>
internal sealed class WorldDiagnosticsLog : IDisposable
{
    private static readonly long IntervalTicks = 5L * Stopwatch.Frequency;
    private readonly StreamWriter _writer;
    private long _nextFlush = Stopwatch.GetTimestamp() + IntervalTicks;
    private int _materialized;
    private double _materializationMs;
    private double _materializationMaxMs;
    private int _restored;
    private int _terrainMeshBatches;
    private double _terrainMeshMs;
    private int _terrainMeshAccepted;
    private int _terrainMeshStale;
    private int _lightingBatches;
    private double _lightingMs;
    private int _lightingVoxels;

    private WorldDiagnosticsLog(StreamWriter writer)
    {
        _writer = writer;
    }

    public static WorldDiagnosticsLog Open()
    {
        var directory = ProjectSettings.GlobalizePath("user://logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "worldgen-latest.log");
        var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        var result = new WorldDiagnosticsLog(writer);
        result.Write("diagnostics.start path=" + path);
        GD.Print("world diagnostics: " + path);
        return result;
    }

    public void Write(string message)
    {
        _writer.Write(DateTimeOffset.Now.ToString(
            "yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
        _writer.Write(' ');
        _writer.WriteLine(message);
    }

    public void WorldStarted(ulong seed, DimensionId dimension) =>
        Write($"world.start seed={seed} dimension={dimension}");

    public void Observe(ChunkResidencyUpdate update)
    {
        foreach (var activation in update.Activations)
        {
            if (activation.Source == ChunkActivationSource.Archive)
            {
                _restored++;
                continue;
            }

            _materialized++;
            _materializationMs += activation.WorkerMilliseconds;
            _materializationMaxMs = Math.Max(
                _materializationMaxMs, activation.WorkerMilliseconds);
        }

        foreach (var failure in update.Failures)
            Write($"chunk.materialize.error coord={failure.Coord} error={failure.Error}");
    }

    public void ObserveTerrainMesh(
        double workerMilliseconds, int accepted, int stale)
    {
        _terrainMeshBatches++;
        _terrainMeshMs += workerMilliseconds;
        _terrainMeshAccepted += accepted;
        _terrainMeshStale += stale;
    }

    public void ObserveLighting(
        double workerMilliseconds, int processedVoxels)
    {
        _lightingBatches++;
        _lightingMs += workerMilliseconds;
        _lightingVoxels += processedVoxels;
    }

    public void FlushIfDue(
        int resident, int pending, int materializing,
        int presented, string loadingPhase)
    {
        var now = Stopwatch.GetTimestamp();
        if (now < _nextFlush)
            return;

        _nextFlush = now + IntervalTicks;
        Write(
            $"world.snapshot phase={loadingPhase} resident={resident} " +
            $"pending={pending} inflight={materializing} presented={presented} " +
            $"generated={_materialized} restored={_restored} " +
            $"generation_ms_total={_materializationMs:F1} " +
            $"generation_ms_max={_materializationMaxMs:F1} " +
            $"mesh_batches={_terrainMeshBatches} mesh_ms_total={_terrainMeshMs:F1} " +
            $"mesh_accepted={_terrainMeshAccepted} mesh_stale={_terrainMeshStale} " +
            $"lighting_batches={_lightingBatches} lighting_ms_total={_lightingMs:F1} " +
            $"lighting_voxels={_lightingVoxels}");

        _materialized = 0;
        _restored = 0;
        _materializationMs = 0d;
        _materializationMaxMs = 0d;
        _terrainMeshBatches = 0;
        _terrainMeshMs = 0d;
        _terrainMeshAccepted = 0;
        _terrainMeshStale = 0;
        _lightingBatches = 0;
        _lightingMs = 0d;
        _lightingVoxels = 0;
    }

    public void Dispose() => _writer.Dispose();
}
