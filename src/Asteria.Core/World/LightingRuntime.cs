namespace Asteria.Core.World;

public enum LightingCompletionKind : byte
{
    Applied = 0,
    RequeuedStale = 1,
}

public sealed record LightingRuntimeReport(
    LightingCompletionKind Kind,
    double WorkerMilliseconds,
    int ChangedVoxelCount,
    int DirtyChunkCount,
    int DirtyMeshletCount,
    int ProcessedVoxelCount);

public sealed class LightingRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly WorldUpdateQueue _updates;
    private readonly LightingResultIntegrator _integration;
    private readonly LightingWorker _worker = new();

    private WorldLightingBatch? _inFlightBatch;

    public LightingRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldUpdateQueue updates,
        LightingResultIntegrator integration)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _fluids =
            fluids ??
            throw new ArgumentNullException(nameof(fluids));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
        _integration =
            integration ??
            throw new ArgumentNullException(nameof(integration));
    }

    public bool IsRunning => _worker.IsRunning;

    public bool TryStartReadyWork()
    {
        if (_worker.IsRunning ||
            !_updates.HasLightingWork)
        {
            return false;
        }

        var batch =
            _updates.DrainLighting();

        if (batch.IsEmpty)
        {
            return false;
        }

        if (!_worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch))
        {
            _updates.RequeueLighting(batch);
            return false;
        }

        _inFlightBatch = batch;
        return true;
    }

    public bool TryPollCompleted(
        out LightingRuntimeReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        if (!_worker.TryTakeCompleted(
                out var result,
                out error))
        {
            return false;
        }

        var sourceBatch =
            _inFlightBatch;
        _inFlightBatch = null;

        if (error is not null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueLighting(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueLighting(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            _updates.RequeueLighting(
                result.SourceBatch);
            report =
                new LightingRuntimeReport(
                    LightingCompletionKind.RequeuedStale,
                    result.WorkerMilliseconds,
                    0,
                    0,
                    0,
                    result.Lighting.ProcessedVoxelCount);
            TryStartReadyWork();
            return true;
        }

        var integrated =
            _integration.Apply(
                result.LightingSnapshot,
                result.Lighting.ChangedPositions);

        report =
            new LightingRuntimeReport(
                LightingCompletionKind.Applied,
                result.WorkerMilliseconds,
                integrated.ChangedVoxelCount,
                integrated.DirtyChunkCount,
                integrated.DirtyMeshletCount,
                result.Lighting.ProcessedVoxelCount);

        TryStartReadyWork();
        return true;
    }
}
