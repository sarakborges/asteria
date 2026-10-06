using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record LightingWorkerResult(
    VoxelLightingDependencies Dependencies,
    VoxelWorld LightingSnapshot,
    WorldLightingBatch SourceBatch,
    VoxelLightingUpdateResult Lighting,
    double WorkerMilliseconds);

public sealed class LightingWorker
{
    private readonly SingleFlightWorker<LightingWorkerResult>
        _worker = new();

    public bool IsRunning => _worker.IsRunning;

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldLightingBatch batch)
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

        return _worker.TryStart(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var lighting =
                VoxelWorldLightingSolver.RelightAfterEdits(
                    snapshot.World,
                    blocks,
                    fluids,
                    batch.EditedPositions);
            stopwatch.Stop();

            return new LightingWorkerResult(
                snapshot.Dependencies,
                snapshot.World,
                batch,
                lighting,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    public bool TryTakeCompleted(
        out LightingWorkerResult? result,
        out Exception? error) =>
        _worker.TryTakeCompleted(
            out result,
            out error);
}
