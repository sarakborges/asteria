using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record FluidSimulationWorkerResult(
    FluidWorkBatch SourceBatch,
    FluidSimulationDependencies Dependencies,
    FluidSimulationResult Simulation,
    double WorkerMilliseconds);

public sealed class FluidSimulationWorker
{
    private readonly SingleFlightWorker<FluidSimulationWorkerResult>
        _worker = new();

    public bool IsRunning => _worker.IsRunning;

    public bool TryStart(
        VoxelWorld world,
        FluidRegistry fluids,
        FluidWorkBatch batch)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.IsEmpty ||
            IsRunning)
        {
            return false;
        }

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                batch,
                fluids.MaximumSpread);

        return _worker.TryStart(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var simulation =
                FluidSimulationSolver.Process(
                    snapshot.World,
                    fluids,
                    batch);
            stopwatch.Stop();

            return new FluidSimulationWorkerResult(
                batch,
                snapshot.Dependencies,
                simulation,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    public bool TryTakeCompleted(
        out FluidSimulationWorkerResult? result,
        out Exception? error) =>
        _worker.TryTakeCompleted(
            out result,
            out error);
}
