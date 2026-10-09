using System.Diagnostics;

namespace Asteria.Core.World;

public sealed class ChunkResidencySettings
{
    public ChunkResidencySettings(
        int maxMaterializationsInFlight,
        int maxDispatchesPerFrame,
        int maxResultsPerFrame,
        int maxEvictionsPerFrame,
        WorldGameRules gameRules)
    {
        if (maxMaterializationsInFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMaterializationsInFlight));
        }

        if (maxDispatchesPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDispatchesPerFrame));
        }

        if (maxResultsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResultsPerFrame));
        }

        if (maxEvictionsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEvictionsPerFrame));
        }

        GameRules = gameRules ??
            throw new ArgumentNullException(nameof(gameRules));

        MaxMaterializationsInFlight =
            maxMaterializationsInFlight;
        MaxDispatchesPerFrame =
            maxDispatchesPerFrame;
        MaxResultsPerFrame =
            maxResultsPerFrame;
        MaxEvictionsPerFrame =
            maxEvictionsPerFrame;
    }

    public int MaxMaterializationsInFlight { get; }

    public int MaxDispatchesPerFrame { get; }

    public int MaxResultsPerFrame { get; }

    public int MaxEvictionsPerFrame { get; }

    public WorldGameRules GameRules { get; }
}

public enum ChunkActivationSource
{
    Archive,
    Provider,
}

public readonly record struct ChunkResidencyActivation(
    ChunkCoord Coord,
    ChunkActivationSource Source,
    double WorkerMilliseconds);

public readonly record struct ChunkResidencyRetirement(
    ChunkCoord Coord,
    ChunkArchiveResult ArchiveResult);

public readonly record struct ChunkMaterializationFailure(
    ChunkCoord Coord,
    Exception Error);

public sealed record ChunkResidencyUpdate(
    IReadOnlyList<ChunkResidencyActivation> Activations,
    IReadOnlyList<ChunkMaterializationFailure> Failures)
{
    public static ChunkResidencyUpdate Empty { get; } =
        new(
            Array.Empty<ChunkResidencyActivation>(),
            Array.Empty<ChunkMaterializationFailure>());
}

public sealed class ChunkResidencyRuntime
{
    private static readonly (int X, int Y, int Z)[] FluidSpreadTargets =
    [
        (0, -1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly IChunkProvider _chunkProvider;
    private readonly WorldUpdateQueue _worldUpdates;
    private readonly FluidUpdateQueue _fluidUpdates;
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates;
    private readonly BlockPhysicsRuntime _blockPhysics;
    private readonly MeshletContentRevisions _terrainContentRevisions;
    private readonly MeshletContentRevisions _fluidContentRevisions;
    private readonly WorldTickClock _worldTicks;
    private readonly ChunkResidencySettings _settings;
    private readonly ChunkStreamingState _streaming = new();
    private readonly ChunkPresentationSelection _presentationSelection = new();
    private readonly Dictionary<ChunkCoord, Task<MaterializedChunk>>
        _materializationTasks = [];

    private ChunkCoord _center;
    private int _retentionRadius;

    public ChunkResidencyRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IChunkProvider chunkProvider,
        WorldUpdateQueue worldUpdates,
        FluidUpdateQueue fluidUpdates,
        FluidMeshUpdateQueue fluidMeshUpdates,
        BlockPhysicsRuntime blockPhysics,
        MeshletContentRevisions terrainContentRevisions,
        MeshletContentRevisions fluidContentRevisions,
        WorldTickClock worldTicks,
        ChunkResidencySettings settings)
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
        _chunkProvider =
            chunkProvider ??
            throw new ArgumentNullException(nameof(chunkProvider));
        _worldUpdates =
            worldUpdates ??
            throw new ArgumentNullException(nameof(worldUpdates));
        _fluidUpdates =
            fluidUpdates ??
            throw new ArgumentNullException(nameof(fluidUpdates));
        _fluidMeshUpdates =
            fluidMeshUpdates ??
            throw new ArgumentNullException(nameof(fluidMeshUpdates));
        _blockPhysics =
            blockPhysics ??
            throw new ArgumentNullException(nameof(blockPhysics));
        _terrainContentRevisions =
            terrainContentRevisions ??
            throw new ArgumentNullException(
                nameof(terrainContentRevisions));
        _fluidContentRevisions =
            fluidContentRevisions ??
            throw new ArgumentNullException(
                nameof(fluidContentRevisions));
        _worldTicks =
            worldTicks ??
            throw new ArgumentNullException(nameof(worldTicks));
        _settings =
            settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public ChunkCoord Center => _center;

    public ChunkMovementDirection MovementDirection =>
        _streaming.MovementDirection;

    public int DesiredCount => _streaming.DesiredCount;

    public int RetainedCount => _streaming.RetainedCount;

    public int PendingCount => _streaming.PendingCount;

    public int MaterializingCount =>
        _streaming.MaterializingCount;

    public int PresentationPendingCount =>
        _streaming.PresentationPendingCount;

    public bool HasRenderableBacklog =>
        _streaming.HasRenderableBacklog;

    public ulong PresentationSelectionRevision =>
        _presentationSelection.Revision;

    public bool SelectionNeedsRebuild(
        ChunkCoord center,
        int horizontalRadius) =>
        _streaming.SelectionNeedsRebuild(
            center,
            horizontalRadius);

    public bool SyncSelection(
        ChunkCoord center,
        int horizontalRadius,
        int retentionRadius,
        IReadOnlySet<ChunkCoord> desired,
        IEnumerable<ChunkCoord> presented)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(presented);

        var changed =
            _streaming.RebuildSelection(
                center,
                horizontalRadius,
                retentionRadius,
                desired);

        _presentationSelection.Sync(
            center,
            horizontalRadius);

        if (!changed)
        {
            return false;
        }

        _center = center;
        _retentionRadius =
            Math.Max(0, retentionRadius);
        SyncResidentState(presented);
        return true;
    }

    public void SyncResidentState(
        IEnumerable<ChunkCoord> presented)
    {
        ArgumentNullException.ThrowIfNull(presented);

        _streaming.SyncResidentState(
            _world.LoadedChunkCoords,
            presented);
    }

    public bool ShouldPresentationBeVisible(
        ChunkCoord coord,
        bool currentlyVisible) =>
        _presentationSelection.ShouldBeVisible(
            coord,
            currentlyVisible);

    public bool ShouldPresentationHaveCollision(ChunkCoord coord) =>
        _presentationSelection.ShouldEnablePhysics(coord);

    public ChunkCoord? PopPresentationByPriority() =>
        _streaming.PopPresentationByPriority(
            _presentationSelection,
            PresentationDependenciesReady);

    public ChunkResidencyUpdate CollectMaterializationResults(
        WorldFrameWorkBudget budget,
        IEnumerable<ChunkCoord> presented)
    {
        ArgumentNullException.ThrowIfNull(presented);

        if (_materializationTasks.Count == 0)
        {
            return ChunkResidencyUpdate.Empty;
        }

        var completed =
            _materializationTasks
                .Where(entry => entry.Value.IsCompleted)
                .OrderBy(entry =>
                    ChunkStreamingState.Priority(
                        entry.Key,
                        _center,
                        _streaming.MovementDirection))
                .Take(_settings.MaxResultsPerFrame)
                .ToArray();

        if (completed.Length == 0)
        {
            return ChunkResidencyUpdate.Empty;
        }

        var activations =
            new List<ChunkResidencyActivation>();
        var failures =
            new List<ChunkMaterializationFailure>();
        var processed = 0;

        foreach (var (coord, task) in completed)
        {
            if (processed > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                break;
            }

            processed++;
            _materializationTasks.Remove(coord);
            _streaming.FinishMaterializing(coord);

            if (task.IsFaulted)
            {
                failures.Add(
                    new ChunkMaterializationFailure(
                        coord,
                        task.Exception?.GetBaseException() ??
                        new InvalidOperationException(
                            $"Chunk materialization failed: {coord}")));
                RequeueIfDesired(coord);
                continue;
            }

            if (task.IsCanceled)
            {
                failures.Add(
                    new ChunkMaterializationFailure(
                        coord,
                        new TaskCanceledException(task)));
                RequeueIfDesired(coord);
                continue;
            }

            if (!_streaming.KeepsLoaded(coord) ||
                _world.ContainsChunk(coord))
            {
                continue;
            }

            var result = task.Result;
            _world.InsertChunk(
                coord,
                result.Chunk);
            activations.Add(
                ActivateResidentChunk(
                    coord,
                    ChunkActivationSource.Provider,
                    result.WorkerMilliseconds));
        }

        if (processed > 0)
        {
            SyncResidentState(presented);
        }

        return new ChunkResidencyUpdate(
            activations,
            failures);
    }

    public ChunkResidencyUpdate DispatchMaterializationTasks(
        WorldFrameWorkBudget budget,
        IEnumerable<ChunkCoord> presented)
    {
        ArgumentNullException.ThrowIfNull(presented);

        var activations =
            new List<ChunkResidencyActivation>();
        var dispatched = 0;

        while (_materializationTasks.Count <
                   _settings.MaxMaterializationsInFlight &&
               dispatched <
                   _settings.MaxDispatchesPerFrame)
        {
            if (dispatched > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                break;
            }

            var coord =
                _streaming.PopPendingByPriority();

            if (coord is null)
            {
                break;
            }

            if (!_streaming.IsDesired(coord.Value) ||
                _world.ContainsChunk(coord.Value) ||
                _materializationTasks.ContainsKey(
                    coord.Value))
            {
                continue;
            }

            var selected = coord.Value;
            var restore =
                _world.RestoreChunk(selected);

            if (restore == ChunkRestoreResult.Restored)
            {
                activations.Add(
                    ActivateResidentChunk(
                        selected,
                        ChunkActivationSource.Archive,
                        workerMilliseconds: 0.0));
                SyncResidentState(presented);
                continue;
            }

            if (restore ==
                ChunkRestoreResult.AlreadyResident)
            {
                continue;
            }

            _streaming.MarkMaterializing(selected);
            _materializationTasks.Add(
                selected,
                Task.Run(() =>
                    Materialize(selected)));
            dispatched++;
        }

        return activations.Count == 0
            ? ChunkResidencyUpdate.Empty
            : new ChunkResidencyUpdate(
                activations,
                Array.Empty<ChunkMaterializationFailure>());
    }

    public IReadOnlyList<ChunkResidencyRetirement>
        RetireDistantChunks(
            WorldFrameWorkBudget budget)
    {
        var maximum =
            _streaming.HasRenderableBacklog
                ? 1
                : _settings.MaxEvictionsPerFrame;
        var retired =
            new List<ChunkResidencyRetirement>();
        var processed = 0;

        while (processed < maximum)
        {
            if (processed > 0 &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                break;
            }

            var coord =
                _streaming
                    .PopRetiredOutsideHorizontalRadius(
                        _center,
                        _retentionRadius);

            if (coord is null)
            {
                break;
            }

            if (_streaming.KeepsLoaded(coord.Value))
            {
                continue;
            }

            _worldUpdates.RemoveMeshChunk(
                coord.Value);
            _fluidMeshUpdates.RemoveChunk(
                coord.Value);
            _terrainContentRevisions.RemoveChunk(
                coord.Value);
            _fluidContentRevisions.RemoveChunk(
                coord.Value);

            var archiveResult =
                _world.ArchiveChunk(coord.Value);

            if (archiveResult !=
                ChunkArchiveResult.NotResident)
            {
                EnqueueChunkLightingReconciliation(
                    coord.Value);
            }

            _streaming.Forget(coord.Value);
            retired.Add(
                new ChunkResidencyRetirement(
                    coord.Value,
                    archiveResult));
            processed++;
        }

        return retired;
    }

    private bool PresentationDependenciesReady(
        ChunkCoord coord)
    {
        if (!_world.ContainsChunk(
                coord))
        {
            return false;
        }

        for (var y = -1;
             y <= 1;
             y++)
        {
            for (var z = -1;
                 z <= 1;
                 z++)
            {
                for (var x = -1;
                     x <= 1;
                     x++)
                {
                    if (x == 0 &&
                        y == 0 &&
                        z == 0)
                    {
                        continue;
                    }

                    var dependency =
                        new ChunkCoord(
                            coord.X + x,
                            coord.Y + y,
                            coord.Z + z);

                    if (_streaming.IsDesired(
                            dependency) &&
                        !_world.ContainsChunk(
                            dependency))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private ChunkResidencyActivation ActivateResidentChunk(
        ChunkCoord coord,
        ChunkActivationSource source,
        double workerMilliseconds)
    {
        _streaming.EnqueuePresentation(coord);
        EnqueueChunkLightingReconciliation(coord);
        EnqueueResidentChunkFluids(coord);
        _blockPhysics.EnqueueResidentChunk(coord);

        return new ChunkResidencyActivation(
            coord,
            source,
            workerMilliseconds);
    }

    private MaterializedChunk Materialize(
        ChunkCoord coord)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunk =
            _chunkProvider.Materialize(
                coord);

        ChunkLightingSolver.Initialize(
            chunk,
            _blocks,
            _fluids);

        stopwatch.Stop();
        return new MaterializedChunk(
            chunk,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private void EnqueueChunkLightingReconciliation(
        ChunkCoord coord)
    {
        foreach (var position in
                 ChunkTopologyFrontier.LightingSeeds(
                     coord,
                     _world.ContainsChunk))
        {
            if (_world.IsLoadedAt(position))
            {
                _worldUpdates.EnqueueLighting(position);
            }
        }
    }

    private void EnqueueResidentChunkFluids(
        ChunkCoord coord)
    {
        if (!_world.TryGetChunk(
                coord,
                out var chunk))
        {
            return;
        }

        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        _fluidUpdates.ReactivateLoadedChunk(
            coord,
            _worldTicks.CurrentTick);

        chunk.VisitFluidCells(
            (x, y, z, fluid) =>
            {
                var position =
                    new WorldVoxelCoord(
                        originX + x,
                        originY + y,
                        originZ + z);

                if (ShouldWakeResidentFluid(
                        position,
                        fluid))
                {
                    ScheduleFluidNeighborhood(
                        fluid.Fluid,
                        position);
                }
            });
    }

    private bool ShouldWakeResidentFluid(
        WorldVoxelCoord position,
        FluidCell fluid)
    {
        if (!fluid.IsSource)
        {
            return true;
        }

        foreach (var offset in FluidSpreadTargets)
        {
            var target = position + offset;

            if (target.Y < 0)
            {
                continue;
            }

            if (!_world.IsLoadedAt(target))
            {
                return true;
            }

            if (!_world.GetCellOrEmpty(target).IsEmpty)
            {
                continue;
            }

            var neighbor = _world.GetFluidOrEmpty(target);
            if (neighbor.IsEmpty ||
                neighbor.Fluid != fluid.Fluid ||
                neighbor.Level < fluid.Level)
            {
                return true;
            }
        }

        return false;
    }

    private void ScheduleFluidNeighborhood(
        FluidRuntimeId fluid,
        WorldVoxelCoord position)
    {
        var delay =
            FluidTiming.DelayTicks(
                _fluids,
                fluid,
                _settings.GameRules.TicksPerSecond);

        if (delay is null)
        {
            return;
        }

        _fluidUpdates.ScheduleNeighborhood(
            fluid,
            position,
            SaturatingAdd(
                _worldTicks.CurrentTick,
                delay.Value));
    }

    private void RequeueIfDesired(
        ChunkCoord coord)
    {
        if (_streaming.IsDesired(coord))
        {
            _streaming.EnqueuePending(coord);
        }
    }

    private static ulong SaturatingAdd(
        ulong value,
        ulong amount) =>
        ulong.MaxValue - value < amount
            ? ulong.MaxValue
            : value + amount;

    private sealed record MaterializedChunk(
        Chunk Chunk,
        double WorkerMilliseconds);
}
