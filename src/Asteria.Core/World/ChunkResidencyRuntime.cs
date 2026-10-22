using System.Diagnostics;
using Asteria.Core.Content;

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
    double WorkerMilliseconds,
    double TerrainMilliseconds = 0d,
    double InitialLightingMilliseconds = 0d);

public readonly record struct FluidActivationWorkReport(
    int CompletedChunks,
    int SlicesProcessed,
    int FluidCellsProcessed,
    int FrontierCellsProcessed,
    int DormantTicksReactivated,
    double ProcessingMilliseconds,
    double MaximumSliceMilliseconds,
    int PendingChunks);

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
    // Relative directions of existing fluid that can enter an arriving chunk.
    // Fluids can fall from above, but do not rise from the chunk below.
    private static readonly (int X, int Y, int Z)[] IncomingFluidNeighbors =
    [
        (0, 1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly PackContentRegistry<ObjectDefinition>? _objects;
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
    private const int FluidActivationItemsPerSlice = 32;
    private const int MaximumFluidActivationSlicesPerFrame = 8;
    private readonly DeduplicatedQueue<ChunkCoord> _pendingFluidActivationCoords = new();
    private readonly Dictionary<ChunkCoord, PendingFluidActivation> _pendingFluidActivations = [];
    // Fluid definitions are immutable for the session. Tick-rate changes
    // invalidate the derived delays before the next fluid is scheduled.
    private readonly Dictionary<FluidRuntimeId, ulong?> _fluidDelayTicksById = [];
    private uint _fluidDelayTicksPerSecond;

    private ChunkCoord _center;
    private int _retentionRadius;
    // Scheduling-only state. Allow one distant launch every eight
    // pressured frames without modifying the authoritative pending set.
    private const int DistantDispatchIntervalFrames = 8;
    private int _pressuredFramesSinceDistantDispatch;

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
        ChunkResidencySettings settings,
        PackContentRegistry<ObjectDefinition>? objects = null)
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
        _objects = objects;
    }

    public ChunkCoord Center => _center;

    public ChunkMovementDirection MovementDirection =>
        _streaming.MovementDirection;

    public int DesiredCount => _streaming.DesiredCount;

    public bool IsDesired(ChunkCoord coord) => _streaming.IsDesired(coord);

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

    public HashSet<ChunkCoord> BootstrapSelection(
        ChunkCoord center, int horizontalRadius) =>
        _streaming.BootstrapSelection(center, horizontalRadius);

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

    public bool ShouldPrewarmPresentationCollision(ChunkCoord coord) =>
        _presentationSelection.ShouldPrewarmPhysics(coord);

    // A resident chunk may be presented before its neighboring chunks arrive.
    // Mesh dependency stamps and arrival-frontier invalidation rebuild its
    // boundary meshlets when those neighbors become resident.
    public ChunkCoord? PopPresentationByPriority() =>
        _streaming.PopPresentationByPriority(
            _presentationSelection,
            _world.ContainsChunk);

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
                    result.WorkerMilliseconds,
                    result.TerrainMilliseconds,
                    result.InitialLightingMilliseconds));
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
        IEnumerable<ChunkCoord> presented,
        bool deferDistantWork = false)
    {
        ArgumentNullException.ThrowIfNull(presented);

        if (deferDistantWork)
            _pressuredFramesSinceDistantDispatch = Math.Min(
                DistantDispatchIntervalFrames,
                _pressuredFramesSinceDistantDispatch + 1);
        else
            _pressuredFramesSinceDistantDispatch = 0;

        var allowDistant = !deferDistantWork ||
            _pressuredFramesSinceDistantDispatch >= DistantDispatchIntervalFrames;
        var localRadius = ChunkStreamingSelection.PlayerLocalHorizontalRadiusChunks;
        var localDistanceSquared = (long)localRadius * localRadius;

        var activations =
            new List<ChunkResidencyActivation>();
        var dispatched = 0;
        var restored = 0;

        // Archived restores also mutate residency and wake lighting, fluids
        // and physics. Bound them independently of provider dispatches.
        while (_materializationTasks.Count <
                   _settings.MaxMaterializationsInFlight &&
               dispatched <
                   _settings.MaxDispatchesPerFrame &&
               restored <
                   _settings.MaxResultsPerFrame)
        {
            if ((dispatched > 0 || restored > 0) &&
                budget.Exhausted(
                    Stopwatch.GetTimestamp()))
            {
                break;
            }

            var coord = _streaming.PopPendingByPriority(
                allowDistant ? null : localDistanceSquared);

            if (coord is null)
            {
                break;
            }

            // Distant dispatch is rate-limited, while nearby chunks keep
            // their existing deterministic priority on every frame.
            var priority = ChunkStreamingState.Priority(
                coord.Value, _center, _streaming.MovementDirection);
            if (deferDistantWork &&
                priority.HorizontalDistanceSquared > localDistanceSquared)
            {
                allowDistant = false;
                _pressuredFramesSinceDistantDispatch = 0;
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
                restored++;
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

        // Sync derived streaming state once per batch, not once per restore.
        // Each sync scans desired/resident sets, so syncing in the loop was
        // quadratic in the number of archived chunks restored in a frame.
        if (restored > 0)
        {
            SyncResidentState(presented);
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

            _pendingFluidActivationCoords.Remove(coord.Value);
            _pendingFluidActivations.Remove(coord.Value);
            _streaming.Forget(coord.Value);
            retired.Add(
                new ChunkResidencyRetirement(
                    coord.Value,
                    archiveResult));
            processed++;
        }

        return retired;
    }

    private ChunkResidencyActivation ActivateResidentChunk(
        ChunkCoord coord,
        ChunkActivationSource source,
        double workerMilliseconds,
        double terrainMilliseconds = 0d,
        double initialLightingMilliseconds = 0d)
    {
        _streaming.EnqueuePresentation(coord);
        EnqueueChunkLightingReconciliation(coord);
        EnqueueResidentChunkFluidActivation(coord);
        _blockPhysics.EnqueueResidentChunk(coord);

        return new ChunkResidencyActivation(
            coord,
            source,
            workerMilliseconds,
            terrainMilliseconds,
            initialLightingMilliseconds);
    }

    private MaterializedChunk Materialize(
        ChunkCoord coord)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunk =
            _chunkProvider.Materialize(
                coord);
        var terrainMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

        ChunkLightingSolver.Initialize(
            chunk,
            _blocks,
            _fluids,
            _objects);

        stopwatch.Stop();
        var totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        return new MaterializedChunk(
            chunk,
            totalMilliseconds,
            terrainMilliseconds,
            totalMilliseconds - terrainMilliseconds);
    }

    private void EnqueueChunkLightingReconciliation(
        ChunkCoord coord)
    {
        var arriving = _world.ContainsChunk(coord);
        foreach (var position in
                 ChunkTopologyFrontier.LightingSeeds(
                     coord,
                     _world.ContainsChunk))
        {
            if (!_world.IsLoadedAt(position))
                continue;

            var needsRelight = arriving
                ? ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
                    _world, _blocks, _fluids, coord, position)
                : ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
                    _world, coord, position);
            if (needsRelight)
                _worldUpdates.EnqueueLighting(position);
        }

        // When an emitter's support and target are in different chunks,
        // initially lit individual chunks cannot see the other side.
        // Reconcile the loaded adjacent support chunks at each arrival.
        if (_objects is null) return;
        ReadOnlySpan<(int X, int Y, int Z)> neighbors =
        [
            (0, 0, 0), (1, 0, 0), (-1, 0, 0),
            (0, 1, 0), (0, -1, 0),
            (0, 0, 1), (0, 0, -1)
        ];
        foreach (var offset in neighbors)
        {
            var neighborCoord = new ChunkCoord(
                coord.X + offset.X, coord.Y + offset.Y, coord.Z + offset.Z);
            if (neighborCoord.Y < 0 ||
                !_world.TryGetChunk(neighborCoord, out var host) ||
                host.ObjectCount == 0)
                continue;
            var origin = VoxelCoordinates.ChunkOrigin(neighborCoord);
            host.VisitObjectCells((x, y, z, obj) =>
            {
                if (!_objects.TryGet(obj.Id, out var definition) ||
                    definition is null || definition.LightEmission.IsDark)
                    return;
                var support = new WorldVoxelCoord(
                    origin.X + x, origin.Y + y, origin.Z + z);
                var target = ObjectLightEmissionField.Target(support, obj.Face);
                if (target.Y < 0 || !_world.IsLoadedAt(target))
                    return;
                if (neighborCoord == coord ||
                    VoxelCoordinates.FromWorld(
                        target.X, target.Y, target.Z).Chunk == coord)
                    _worldUpdates.EnqueueLighting(target);
            });
        }
    }

    private void EnqueueResidentChunkFluidActivation(ChunkCoord coord)
    {
        if (_pendingFluidActivations.ContainsKey(coord))
        {
            return;
        }

        _pendingFluidActivations.Add(
            coord,
            new PendingFluidActivation());
        _pendingFluidActivationCoords.Enqueue(coord);
    }

    public FluidActivationWorkReport ProcessPendingFluidActivations(
        WorldFrameWorkBudget budget)
    {
        var started = Stopwatch.GetTimestamp();
        var completedChunks = 0;
        var slicesProcessed = 0;
        var fluidCellsProcessed = 0;
        var frontierCellsProcessed = 0;
        var dormantTicksReactivated = 0;
        var maximumSliceMilliseconds = 0d;

        while (slicesProcessed < MaximumFluidActivationSlicesPerFrame &&
               _pendingFluidActivationCoords.Count > 0)
        {
            if (slicesProcessed > 0 &&
                budget.Exhausted(Stopwatch.GetTimestamp()))
            {
                break;
            }

            if (!_pendingFluidActivationCoords.TryDequeue(out var coord))
            {
                break;
            }

            if (!_pendingFluidActivations.TryGetValue(coord, out var state))
            {
                continue;
            }

            if (!_world.TryGetChunk(coord, out var chunk))
            {
                _pendingFluidActivations.Remove(coord);
                continue;
            }

            var sliceStarted = Stopwatch.GetTimestamp();
            var complete = ProcessFluidActivationSlice(
                coord,
                chunk,
                state,
                out var fluidCells,
                out var frontierCells,
                out var reactivatedTicks);
            var sliceMilliseconds =
                Stopwatch.GetElapsedTime(sliceStarted).TotalMilliseconds;

            slicesProcessed++;
            fluidCellsProcessed += fluidCells;
            frontierCellsProcessed += frontierCells;
            dormantTicksReactivated += reactivatedTicks;
            maximumSliceMilliseconds = Math.Max(
                maximumSliceMilliseconds,
                sliceMilliseconds);

            if (complete)
            {
                _pendingFluidActivations.Remove(coord);
                completedChunks++;
            }
            else
            {
                // Rotate incomplete chunks so dense fluid volumes do not
                // block activation work for other newly resident chunks.
                _pendingFluidActivationCoords.Enqueue(coord);
            }
        }

        return new FluidActivationWorkReport(
            completedChunks,
            slicesProcessed,
            fluidCellsProcessed,
            frontierCellsProcessed,
            dormantTicksReactivated,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            maximumSliceMilliseconds,
            _pendingFluidActivations.Count);
    }

    private bool ProcessFluidActivationSlice(
        ChunkCoord coord,
        Chunk chunk,
        PendingFluidActivation state,
        out int fluidCellsProcessed,
        out int frontierCellsProcessed,
        out int dormantTicksReactivated)
    {
        var remaining = FluidActivationItemsPerSlice;
        fluidCellsProcessed = 0;
        frontierCellsProcessed = 0;
        dormantTicksReactivated = 0;

        if (!state.DormantReactivationComplete)
        {
            dormantTicksReactivated = _fluidUpdates.ReactivateLoadedChunk(
                coord,
                _worldTicks.CurrentTick,
                remaining,
                out var reactivationComplete);
            remaining -= dormantTicksReactivated;
            state.DormantReactivationComplete = reactivationComplete;

            if (!reactivationComplete || remaining == 0)
            {
                return false;
            }
        }

        if (!state.FluidScanComplete)
        {
            var (originX, originY, originZ) =
                VoxelCoordinates.ChunkOrigin(coord);
            state.NextFluidVoxelIndex = chunk.VisitFluidCells(
                state.NextFluidVoxelIndex,
                remaining,
                (x, y, z, fluid) =>
                {
                    var position = new WorldVoxelCoord(
                        originX + x,
                        originY + y,
                        originZ + z);
                    if (ShouldWakeResidentFluid(position, fluid))
                    {
                        ScheduleFluidNeighborhood(fluid.Fluid, position);
                    }
                },
                out fluidCellsProcessed);
            remaining -= fluidCellsProcessed;
            state.FluidScanComplete =
                state.NextFluidVoxelIndex == Chunk.Volume;

            if (!state.FluidScanComplete || remaining == 0)
            {
                return false;
            }
        }

        var (chunkOriginX, chunkOriginY, chunkOriginZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        while (remaining > 0 &&
               state.FrontierNeighborIndex < IncomingFluidNeighbors.Length)
        {
            var (dx, dy, dz) =
                IncomingFluidNeighbors[state.FrontierNeighborIndex];
            var neighbor = new ChunkCoord(
                coord.X + dx,
                coord.Y + dy,
                coord.Z + dz);
            if (!_world.ContainsChunk(neighbor))
            {
                state.FrontierNeighborIndex++;
                state.FrontierFaceIndex = 0;
                continue;
            }

            var u = state.FrontierFaceIndex / Chunk.Size;
            var v = state.FrontierFaceIndex % Chunk.Size;
            var position = dx != 0
                ? new WorldVoxelCoord(
                    chunkOriginX + (dx > 0 ? Chunk.Size : -1),
                    chunkOriginY + u,
                    chunkOriginZ + v)
                : dy != 0
                    ? new WorldVoxelCoord(
                        chunkOriginX + u,
                        chunkOriginY + Chunk.Size,
                        chunkOriginZ + v)
                    : new WorldVoxelCoord(
                        chunkOriginX + u,
                        chunkOriginY + v,
                        chunkOriginZ + (dz > 0 ? Chunk.Size : -1));
            var fluid = _world.GetFluidOrEmpty(position);
            if (!fluid.IsEmpty &&
                ShouldWakeResidentFluid(position, fluid))
            {
                ScheduleFluidNeighborhood(fluid.Fluid, position);
            }

            state.FrontierFaceIndex++;
            frontierCellsProcessed++;
            remaining--;

            if (state.FrontierFaceIndex == Chunk.Area)
            {
                state.FrontierNeighborIndex++;
                state.FrontierFaceIndex = 0;
            }
        }

        return state.FluidScanComplete &&
            state.FrontierNeighborIndex >= IncomingFluidNeighbors.Length;
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

            // Missing chunks cannot accept flow. Recheck this source
            // when the neighboring chunk becomes resident instead.
            if (!_world.TryGetCellAndFluid(
                    target,
                    out var targetCell,
                    out var neighbor))
            {
                continue;
            }

            if (!targetCell.IsEmpty)
            {
                continue;
            }
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
        var ticksPerSecond = _settings.GameRules.TicksPerSecond;
        if (_fluidDelayTicksPerSecond != ticksPerSecond)
        {
            _fluidDelayTicksById.Clear();
            _fluidDelayTicksPerSecond = ticksPerSecond;
        }

        if (!_fluidDelayTicksById.TryGetValue(fluid, out var delay))
        {
            delay = FluidTiming.DelayTicks(
                _fluids,
                fluid,
                ticksPerSecond);
            _fluidDelayTicksById.Add(fluid, delay);
        }

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

    private sealed class PendingFluidActivation
    {
        public int NextFluidVoxelIndex { get; set; }

        public bool DormantReactivationComplete { get; set; }

        public bool FluidScanComplete { get; set; }

        public int FrontierNeighborIndex { get; set; }

        public int FrontierFaceIndex { get; set; }
    }

    private sealed record MaterializedChunk(
        Chunk Chunk,
        double WorkerMilliseconds,
        double TerrainMilliseconds,
        double InitialLightingMilliseconds);
}
