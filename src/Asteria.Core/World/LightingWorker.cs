using System.Diagnostics;

using Asteria.Core.Content;

namespace Asteria.Core.World;

public sealed record LightingWorkerResult(
    VoxelLightingDependencies Dependencies,
    VoxelWorld LightingSnapshot,
    WorldLightingBatch SourceBatch,
    VoxelLightingUpdateResult Lighting,
    double WorkerMilliseconds,
    bool Cancelled = false);

public sealed class LightingWorker
{
    private readonly SingleFlightWorker<LightingWorkerResult>
        _worker = new();
    private CancellationTokenSource? _cancellation;
    private VoxelLightingDependencies? _dependencies;

    public bool IsRunning => _worker.IsRunning;

    // Called only on the authoritative thread. Never read mutable world
    // state from the worker; its snapshot remains isolated.
    public bool CancelIfStale(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_worker.IsRunning || _dependencies is not { } dependencies ||
            dependencies.IsCurrent(world))
            return false;

        _cancellation?.Cancel();
        return true;
    }

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldLightingBatch batch,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.IsEmpty ||
            IsRunning)
        {
            return false;
        }

        var snapshot =
            VoxelLightingSnapshot.Capture(
                world,
                batch.EditedPositions);

        var cancellation = new CancellationTokenSource();
        if (!_worker.TryStart(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var lighting = VoxelWorldLightingSolver.RelightAfterEdits(
                    snapshot.World, blocks, fluids, batch.EditedPositions,
                    objects, cancellation.Token);
                return new LightingWorkerResult(
                    snapshot.Dependencies, snapshot.World, batch, lighting,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // No partial worker light may be copied to the live world.
                return new LightingWorkerResult(
                    snapshot.Dependencies, snapshot.World, batch,
                    new VoxelLightingUpdateResult([], 0),
                    stopwatch.Elapsed.TotalMilliseconds, Cancelled: true);
            }
        }))
        {
            cancellation.Dispose();
            return false;
        }

        _cancellation = cancellation;
        _dependencies = snapshot.Dependencies;
        return true;
    }

    public bool TryTakeCompleted(
        out LightingWorkerResult? result,
        out Exception? error)
    {
        if (!_worker.TryTakeCompleted(out result, out error))
            return false;
        _dependencies = null;
        _cancellation?.Dispose();
        _cancellation = null;
        return true;
    }
}
