namespace Asteria.Core.World;

public enum FluidSimulationCompletionKind : byte
{
    Applied = 0,
    RequeuedStale = 1,
    RequeuedMutationConflict = 2,
}

public sealed record FluidSimulationRuntimeReport(
    FluidSimulationCompletionKind Kind,
    ulong Tick,
    double WorkerMilliseconds,
    int ProcessedVoxelCount,
    int AppliedChangeCount,
    int UniquePositionCount,
    int ScheduledRequestCount,
    int DownhillSearchCount,
    int DownhillVisitedNodeCount,
    int BacklogCount);

public sealed class FluidSimulationRuntime
{
    private readonly VoxelWorld _world;
    private readonly FluidRegistry _fluids;
    private readonly FluidUpdateQueue _updates;
    private readonly VoxelMutationRuntime _mutations;
    private readonly WorldTickClock _ticks;
    private readonly WorldGameRules _gameRules;
    private readonly int _maximumUpdatesPerWorker;
    private readonly FluidSimulationWorker _worker = new();

    private bool _acceptingWork = true;

    private FluidWorkBatch? _inFlightBatch;

    public FluidSimulationRuntime(
        VoxelWorld world,
        FluidRegistry fluids,
        FluidUpdateQueue updates,
        VoxelMutationRuntime mutations,
        WorldTickClock ticks,
        WorldGameRules gameRules,
        int maximumUpdatesPerWorker)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _fluids =
            fluids ??
            throw new ArgumentNullException(nameof(fluids));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
        _mutations =
            mutations ??
            throw new ArgumentNullException(nameof(mutations));
        _ticks =
            ticks ??
            throw new ArgumentNullException(nameof(ticks));

        _gameRules = gameRules ??
            throw new ArgumentNullException(nameof(gameRules));

        if (maximumUpdatesPerWorker <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumUpdatesPerWorker));
        }

        _maximumUpdatesPerWorker =
            maximumUpdatesPerWorker;
    }

    public bool IsRunning => _worker.IsRunning;

    public void BeginRetirement()
    {
        _acceptingWork = false;
    }

    public bool TryStartReadyWork()
    {
        if (!_acceptingWork)
        {
            return false;
        }

        if (_worker.IsRunning ||
            !_updates.HasReadyWork(
                _ticks.CurrentTick))
        {
            return false;
        }

        var batch =
            _updates.DrainReady(
                _ticks.CurrentTick,
                _maximumUpdatesPerWorker);

        if (batch.IsEmpty)
        {
            return false;
        }

        if (!_worker.TryStart(
                _world,
                _fluids,
                batch))
        {
            Requeue(batch);
            return false;
        }

        _inFlightBatch = batch;
        return true;
    }

    public bool TryPollCompleted(
        out FluidSimulationRuntimeReport? report,
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
                Requeue(sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                Requeue(sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            Requeue(result.SourceBatch);
            report =
                BuildReport(
                    result,
                    FluidSimulationCompletionKind.RequeuedStale,
                    appliedChangeCount: 0,
                    uniquePositionCount: 0);
            TryStartReadyWork();
            return true;
        }

        var applied =
            _mutations.ApplyFluidChanges(
                result.Simulation.Changes);

        if (!applied.Accepted)
        {
            Requeue(result.SourceBatch);
            report =
                BuildReport(
                    result,
                    FluidSimulationCompletionKind.RequeuedMutationConflict,
                    appliedChangeCount: 0,
                    uniquePositionCount: 0);
            TryStartReadyWork();
            return true;
        }

        foreach (var dormant in
                 result.Simulation.DormantTicks)
        {
            _updates.DeferUnloaded(dormant);
        }

        foreach (var request in
                 result.Simulation.ScheduleRequests)
        {
            Schedule(request);
        }

        report =
            BuildReport(
                result,
                FluidSimulationCompletionKind.Applied,
                applied.AppliedChangeCount,
                applied.UniquePositionCount);

        TryStartReadyWork();
        return true;
    }

    private FluidSimulationRuntimeReport BuildReport(
        FluidSimulationWorkerResult result,
        FluidSimulationCompletionKind kind,
        int appliedChangeCount,
        int uniquePositionCount) =>
        new(
            kind,
            _ticks.CurrentTick,
            result.WorkerMilliseconds,
            result.Simulation.ProcessedVoxelCount,
            appliedChangeCount,
            uniquePositionCount,
            result.Simulation.ScheduleRequests.Count,
            result.Simulation.DownhillSearchCount,
            result.Simulation.DownhillVisitedNodeCount,
            _updates.Count);

    private void Requeue(
        FluidWorkBatch batch)
    {
        _updates.RequeueTopology(
            batch.TopologyPositions);
        _updates.RequeueDue(
            batch.DueTicks,
            _ticks.CurrentTick);
    }

    private void Schedule(
        FluidScheduleRequest request)
    {
        var delay =
            FluidTiming.DelayTicks(
                _fluids,
                request.Fluid,
                _gameRules.TicksPerSecond);

        if (delay is null)
        {
            return;
        }

        var dueTick =
            SaturatingAdd(
                _ticks.CurrentTick,
                delay.Value);

        if (request.Neighborhood)
        {
            _updates.ScheduleNeighborhood(
                request.Fluid,
                request.Position,
                dueTick);
        }
        else
        {
            _updates.ScheduleAt(
                new FluidTickKey(
                    request.Fluid,
                    request.Position),
                dueTick);
        }
    }

    private static ulong SaturatingAdd(
        ulong value,
        ulong amount) =>
        ulong.MaxValue - value < amount
            ? ulong.MaxValue
            : value + amount;
}
