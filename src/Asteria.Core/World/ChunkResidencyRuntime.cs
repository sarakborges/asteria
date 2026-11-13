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
        WorldGameRules gameRules,
        int? maxConcurrentWorkers = null,
        int? maxGenerationCompletionsPerFrame = null,
        int? maxGenerationTaskAdvancesPerFrame = null)
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
        var concurrentWorkers = maxConcurrentWorkers ?? maxMaterializationsInFlight;
        if (concurrentWorkers <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentWorkers));

        MaxConcurrentWorkers = concurrentWorkers;
        MaxMaterializationsInFlight = concurrentWorkers;
        MaxTerrainMaterializationsInFlight =
            maxMaterializationsInFlight;
        MaxDispatchesPerFrame =
            maxDispatchesPerFrame;
        MaxResultsPerFrame =
            maxResultsPerFrame;
        MaxEvictionsPerFrame =
            maxEvictionsPerFrame;

        var defaultGenerationBudget = DefaultGenerationBudget(concurrentWorkers);
        MaxGenerationCompletionsPerFrame =
            ValidateGenerationBudget(
                maxGenerationCompletionsPerFrame ?? defaultGenerationBudget,
                nameof(maxGenerationCompletionsPerFrame));
        MaxGenerationTaskAdvancesPerFrame =
            ValidateGenerationBudget(
                maxGenerationTaskAdvancesPerFrame ?? defaultGenerationBudget,
                nameof(maxGenerationTaskAdvancesPerFrame));
    }

    private static int DefaultGenerationBudget(int workerCount) =>
        (int)Math.Min(int.MaxValue, Math.Max(16L, (long)workerCount * 8L));

    private static int ValidateGenerationBudget(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    /// <summary>
    /// Maximum number of physical worker threads in the shared generation pool.
    /// </summary>
    public int MaxConcurrentWorkers { get; }

    // Kept as a compatibility alias for the physical worker count.
    public int MaxMaterializationsInFlight { get; }

    /// <summary>
    /// Maximum number of admitted terrain chunk jobs, including suspended jobs.
    /// A separate bounded slot allows one arrival-reconciliation task alongside
    /// terrain work. This may exceed the physical worker count.
    /// </summary>
    public int MaxTerrainMaterializationsInFlight { get; }

    public int MaxDispatchesPerFrame { get; }

    public int MaxResultsPerFrame { get; }

    public int MaxEvictionsPerFrame { get; }

    /// <summary>
    /// Maximum worker completion messages consumed by one coordinator pump.
    /// This is independent of the count of completed chunks published per frame.
    /// </summary>
    public int MaxGenerationCompletionsPerFrame { get; }

    /// <summary>
    /// Maximum short continuation/state-advance operations in one coordinator pump.
    /// This is independent of new section admissions per frame.
    /// </summary>
    public int MaxGenerationTaskAdvancesPerFrame { get; }

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
    double ElapsedMilliseconds = 0d,
    double InitialLightingMilliseconds = 0d,
    double WorkerQueueWaitMilliseconds = 0d,
    double WorkerWallMilliseconds = 0d,
    double ResultCollectionWaitMilliseconds = 0d,
    double PendingQueueWaitMilliseconds = 0d,
    double DispatchPreparationMilliseconds = 0d,
    int WorkerSliceCount = 0,
    double InterSliceWaitMilliseconds = 0d);

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

public readonly record struct ChunkMaterializationStageTiming(
    ChunkCoord Coord,
    ChunkMaterializationStage Stage,
    double WorkerMilliseconds,
    DirectionalFacePaletteMetrics? DirectionalFacePaletteMetrics = null,
    double BoundaryColumnBoundsMilliseconds = 0d,
    double CaveSamplerBuildMilliseconds = 0d,
    double BoundaryColumnEvaluationMilliseconds = 0d,
    bool IsSupportOnly = false,
    bool IsSupportFaceFastPath = false,
    bool IsSparseSupportFallback = false,
    int SupportRequestedColumns = 0,
    int SupportMaterializationColumns = 0);

public sealed record ChunkResidencyUpdate(
    IReadOnlyList<ChunkResidencyActivation> Activations,
    IReadOnlyList<ChunkMaterializationFailure> Failures,
    IReadOnlyList<ChunkMaterializationStageTiming>? StageTimings = null,
    int StartedTasks = 0,
    int CancelledTasks = 0)
{
    public IReadOnlyList<ChunkMaterializationStageTiming> MaterializationStageTimings =>
        StageTimings ?? Array.Empty<ChunkMaterializationStageTiming>();

    public static ChunkResidencyUpdate Empty { get; } =
        new(
            Array.Empty<ChunkResidencyActivation>(),
            Array.Empty<ChunkMaterializationFailure>(),
            Array.Empty<ChunkMaterializationStageTiming>());
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
    private readonly IChunkDirectionalFacePaletteUpdater? _directionalFacePaletteUpdater;
    private bool _directionalFaceChunkEventAttached;
    private readonly DeduplicatedQueue<ChunkCoord> _pendingDirectionalFaceReconciliations = new();
    private readonly Dictionary<ChunkCoord, DirectionalFaceReconciliationWork>
        _directionalFaceReconciliationTasks = [];
    private readonly DeduplicatedQueue<ChunkCoord> _pendingChunkLightingReconciliations = new();
    private readonly Dictionary<ChunkCoord, ChunkLightingReconciliationWork>
        _chunkLightingReconciliationTasks = [];
    private bool _preferChunkLightingReconciliation = true;
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
    // Each section keeps one private incremental materialization job. The
    // serial dispatcher resumes it between bounded worker slices; no worker
    // and coordinator may touch the same job concurrently.
    private readonly Dictionary<ChunkCoord, MaterializationWork>
        _materializationTasks = [];
    private readonly ChunkGenerationCompletionMailbox _generationCompletionMailbox;
    private readonly ChunkGenerationWorkerPool _generationWorkerPool;
    private readonly ChunkGenerationDispatcher _generationDispatcher;
    private Task? _generationWorkerShutdown;
    private ulong _nextMaterializationGeneration = 1;
    private bool _acceptingMaterializationWork = true;
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
        _blockPhysics.ChunkReconciliationRequested +=
            HandleBlockPhysicsReconciliationRequested;
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
        var generationWorkerCount = _settings.MaxConcurrentWorkers;
        var generationPendingCapacity = Math.Max(
            _settings.MaxDispatchesPerFrame,
            generationWorkerCount);
        _generationCompletionMailbox = new ChunkGenerationCompletionMailbox(
            checked(generationPendingCapacity + generationWorkerCount));
        _generationWorkerPool = new ChunkGenerationWorkerPool(
            generationWorkerCount,
            generationPendingCapacity,
            _generationCompletionMailbox);
        _generationDispatcher = new ChunkGenerationDispatcher(
            _generationCompletionMailbox,
            _generationWorkerPool);
        _objects = objects;
        _directionalFacePaletteUpdater =
            chunkProvider as IChunkDirectionalFacePaletteUpdater;
        if (_directionalFacePaletteUpdater is not null)
        {
            _world.ChunkBecameResident += HandleDirectionalFaceChunkArrival;
            _directionalFaceChunkEventAttached = true;
        }
    }

    public ChunkCoord Center => _center;

    public ChunkMovementDirection MovementDirection =>
        _streaming.MovementDirection;

    public int DesiredCount => _streaming.DesiredCount;

    public bool IsDesired(ChunkCoord coord) => _streaming.IsDesired(coord);

    public bool IsLightingRelevant(ChunkCoord coord) =>
        _streaming.IsLightingRelevant(coord);

    public int RetainedCount => _streaming.RetainedCount;

    public int PendingCount => _streaming.PendingCount;

    public int MaterializingCount =>
        _streaming.MaterializingCount;

    /// <summary>Background tasks currently owning terrain or initial-light stages.</summary>
    public int ActiveWorkerCount =>
        _generationWorkerPool.PendingWorkCount +
        _generationWorkerPool.ActiveWorkerCount +
        _directionalFaceReconciliationTasks.Values.Count(work =>
            !work.WorkerTask.IsCompleted) +
        _chunkLightingReconciliationTasks.Values.Count(work =>
            !work.WorkerTask.IsCompleted);

    public int GenerationWorkerCount => _generationWorkerPool.WorkerCount;

    public int ActiveGenerationWorkerCount =>
        _generationWorkerPool.ActiveWorkerCount;

    public int PendingGenerationWorkerQueueCount =>
        _generationWorkerPool.PendingWorkCount;

    public int MaxTerrainMaterializationsInFlight =>
        _settings.MaxTerrainMaterializationsInFlight;

    public int MaterializationJobCount =>
        _materializationTasks.Count;

    public int DirectionalFaceReconciliationCount =>
        _pendingDirectionalFaceReconciliations.Count +
        _directionalFaceReconciliationTasks.Count;

    public int ChunkLightingReconciliationCount =>
        _pendingChunkLightingReconciliations.Count +
        _chunkLightingReconciliationTasks.Count;

    public int SuspendedMaterializationCount =>
        _generationDispatcher.WaitingTaskCount;

    public int ReadyGenerationTaskCount =>
        _generationDispatcher.ReadyTaskCount;

    public int PendingGenerationWorkerOperations =>
        _generationDispatcher.PendingWorkerOperationCount;

    public int PendingGenerationCompletions =>
        _generationCompletionMailbox.PendingCount + _generationDispatcher.PendingOutcomeCount;

    public bool IsQuiescent =>
        _streaming.MaterializingCount == 0 &&
        _materializationTasks.Count == 0 &&
        _generationDispatcher.ActiveTaskCount == 0 &&
        _generationDispatcher.RetiringTaskCount == 0 &&
        _generationDispatcher.PendingOutcomeCount == 0 &&
        DirectionalFaceReconciliationCount == 0 &&
        ChunkLightingReconciliationCount == 0 &&
        (_generationWorkerShutdown is null || _generationWorkerShutdown.IsCompleted);

    /// <summary>
    /// Stops admitting new chunks. Bounded in-flight materializations are
    /// drained by the owning session and their private results are discarded.
    /// </summary>
    public void BeginRetirement()
    {
        if (!_acceptingMaterializationWork)
            return;

        _acceptingMaterializationWork = false;
        _generationDispatcher.CancelAll();
        _generationWorkerShutdown ??= _generationWorkerPool.DisposeAsync().AsTask();
        _pendingDirectionalFaceReconciliations.Clear();
        _pendingChunkLightingReconciliations.Clear();
        if (!_directionalFaceChunkEventAttached)
            return;
        _world.ChunkBecameResident -= HandleDirectionalFaceChunkArrival;
        _directionalFaceChunkEventAttached = false;
    }

    // Chunk arrival is only an enqueue operation. Snapshot capture and all
    // directional-face scanning run later in the bounded worker lane.
    private void HandleDirectionalFaceChunkArrival(ChunkCoord coord)
    {
        if (_acceptingMaterializationWork &&
            _directionalFacePaletteUpdater is not null)
            _pendingDirectionalFaceReconciliations.Enqueue(coord);
    }

    // Chunk-physics events are queue-only. Snapshot capture and voxel scans
    // are deferred to the bounded arrival-reconciliation worker.
    private void HandleBlockPhysicsReconciliationRequested(ChunkCoord coord)
    {
        if (_acceptingMaterializationWork)
            _pendingChunkLightingReconciliations.Enqueue(coord);
    }

    private void EnqueueDirectionalFaceVisualChanges(
        IReadOnlyList<WorldVoxelCoord> positions)
    {
        foreach (var position in positions)
        {
            _terrainContentRevisions.BumpVoxelEdit(_world, position);
            _worldUpdates.EnqueueSurfaceEdit(_world, position);
        }
    }

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
        if (_materializationTasks.Count == 0 &&
            _generationDispatcher.RetiringTaskCount == 0 &&
            _generationDispatcher.PendingOutcomeCount == 0 &&
            !_directionalFaceReconciliationTasks.Values.Any(work =>
                work.WorkerTask.IsCompleted) &&
            !_chunkLightingReconciliationTasks.Values.Any(work =>
                work.WorkerTask.IsCompleted))
            return ChunkResidencyUpdate.Empty;

        var report = _generationDispatcher.Pump(
            maximumCompletions: _settings.MaxGenerationCompletionsPerFrame,
            maximumTaskAdvances: _settings.MaxGenerationTaskAdvancesPerFrame,
            maximumWorkerStarts: AvailableGenerationWorkerStarts);

        var activations = new List<ChunkResidencyActivation>();
        var failures = new List<ChunkMaterializationFailure>();
        var stageTimings = new List<ChunkMaterializationStageTiming>();
        var processed = 0;
        var cancelledTasks = 0;
        var reconciliationResultProcessed = false;

        while (processed < _settings.MaxResultsPerFrame)
        {
            // Check the frame budget before removing an outcome. Once an
            // outcome leaves the dispatcher queue, it must be applied or
            // explicitly requeued; dropping it would strand MaterializingCount.
            if (processed > 0 && budget.Exhausted(Stopwatch.GetTimestamp()))
                break;
            if (!_generationDispatcher.TryDequeueOutcome(out var outcome))
                break;

            processed++;
            switch (outcome.Task.Key.TargetStep)
            {
                case ChunkGenerationStepId.SectionReady:
                    CollectMaterializationOutcome(
                        outcome, presented, activations, failures, stageTimings,
                        ref cancelledTasks);
                    break;

                case ChunkGenerationStepId.DirectionalFacePalettes:
                    if (!reconciliationResultProcessed)
                    {
                        CollectDirectionalFaceOutcome(
                            outcome, failures, ref cancelledTasks);
                        reconciliationResultProcessed = true;
                    }
                    else
                    {
                        // Keep at most one reconciliation publication per frame.
                        // Put the outcome back in front of the dispatcher queue.
                        _generationDispatcher.RequeueOutcome(outcome);
                        processed--;
                        return FinishMaterializationCollection(
                            activations, failures, stageTimings,
                            processed, cancelledTasks, presented);
                    }
                    break;

                case ChunkGenerationStepId.InitialLighting:
                    if (!reconciliationResultProcessed)
                    {
                        CollectChunkLightingOutcome(
                            outcome, failures, ref cancelledTasks);
                        reconciliationResultProcessed = true;
                    }
                    else
                    {
                        _generationDispatcher.RequeueOutcome(outcome);
                        processed--;
                        return FinishMaterializationCollection(
                            activations, failures, stageTimings,
                            processed, cancelledTasks, presented);
                    }
                    break;

                default:
                    failures.Add(new ChunkMaterializationFailure(
                        outcome.Task.Key.Column.ToSection(
                            outcome.Task.Key.SectionY ?? 0),
                        new InvalidOperationException(
                            $"Unexpected generation outcome step: {outcome.Task.Key.TargetStep}")));
                    break;
            }
        }

        // Existing reconciliation tasks still use their detached-snapshot
        // compatibility lane in this first runtime migration slice.
        if (!reconciliationResultProcessed &&
            processed < _settings.MaxResultsPerFrame &&
            (processed == 0 || !budget.Exhausted(Stopwatch.GetTimestamp())))
        {
            var completedReconciliations = _directionalFaceReconciliationTasks
                .Where(entry => entry.Value.WorkerTask.IsCompleted)
                .OrderBy(entry => ChunkStreamingState.StreamingPriority(
                    entry.Key, _center, _streaming.MovementDirection))
                .Take(1)
                .ToArray();

            foreach (var (coord, work) in completedReconciliations)
            {
                _directionalFaceReconciliationTasks.Remove(coord);
                processed++;
                reconciliationResultProcessed = true;
                var task = work.WorkerTask;
                if (task.IsFaulted || task.IsCanceled)
                {
                    var error = task.IsFaulted
                        ? task.Exception?.GetBaseException() ??
                          new InvalidOperationException(
                              $"Directional face reconciliation failed: {coord}")
                        : new TaskCanceledException(task);
                    failures.Add(new ChunkMaterializationFailure(coord, error));
                    if (_acceptingMaterializationWork &&
                        _world.TryGetSavedChunk(coord, out _))
                        _pendingDirectionalFaceReconciliations.Enqueue(coord);
                    break;
                }

                var result = task.GetAwaiter().GetResult();
                if (!_acceptingMaterializationWork)
                    break;

                if (_directionalFacePaletteUpdater is not null &&
                    _directionalFacePaletteUpdater.TryApplyChunkArrivalReconciliation(
                        _world, result, out var visualChanges))
                {
                    EnqueueDirectionalFaceVisualChanges(visualChanges);
                }
                else if (_world.TryGetSavedChunk(result.Arriving, out _))
                {
                    _pendingDirectionalFaceReconciliations.Enqueue(result.Arriving);
                }
                break;
            }
        }

        if (!reconciliationResultProcessed &&
            processed < _settings.MaxResultsPerFrame &&
            (processed == 0 || !budget.Exhausted(Stopwatch.GetTimestamp())))
        {
            var completedLighting = _chunkLightingReconciliationTasks
                .Where(entry => entry.Value.WorkerTask.IsCompleted)
                .OrderBy(entry => ChunkStreamingState.StreamingPriority(
                    entry.Key, _center, _streaming.MovementDirection))
                .Take(1)
                .ToArray();

            foreach (var (coord, work) in completedLighting)
            {
                _chunkLightingReconciliationTasks.Remove(coord);
                processed++;
                reconciliationResultProcessed = true;
                var task = work.WorkerTask;
                if (task.IsFaulted || task.IsCanceled)
                {
                    var error = task.IsFaulted
                        ? task.Exception?.GetBaseException() ??
                          new InvalidOperationException(
                              $"Chunk lighting reconciliation failed: {coord}")
                        : new TaskCanceledException(task);
                    failures.Add(new ChunkMaterializationFailure(coord, error));
                    if (_acceptingMaterializationWork &&
                        (_world.ContainsChunk(coord) || _world.HasArchivedChunk(coord)))
                        _pendingChunkLightingReconciliations.Enqueue(coord);
                    break;
                }

                var result = task.GetAwaiter().GetResult();
                if (!_acceptingMaterializationWork)
                    break;

                if (!_world.IsContentStampCurrent(work.ContentDependencies) ||
                    !_world.AreLightingStampsCurrent(work.LightingDependencies))
                {
                    if (_world.ContainsChunk(coord) || _world.HasArchivedChunk(coord))
                        _pendingChunkLightingReconciliations.Enqueue(coord);
                    break;
                }

                foreach (var position in result.LightingSeeds)
                    _worldUpdates.EnqueueLighting(position);

                if (result.PhysicsPlan is { } physicsPlan)
                    _blockPhysics.ApplyResidentChunkActivationPlan(physicsPlan);
                break;
            }
        }

        return FinishMaterializationCollection(
            activations, failures, stageTimings, processed,
            cancelledTasks, presented);
    }

    private void CollectMaterializationOutcome(
        ChunkGenerationTaskOutcome outcome,
        IEnumerable<ChunkCoord> presented,
        List<ChunkResidencyActivation> activations,
        List<ChunkMaterializationFailure> failures,
        List<ChunkMaterializationStageTiming> stageTimings,
        ref int cancelledTasks)
    {
        var sectionY = outcome.Task.Key.SectionY ??
            throw new InvalidOperationException(
                "A section materialization outcome has no SectionY.");
        var coord = outcome.Task.Key.Column.ToSection(sectionY);
        if (!_materializationTasks.TryGetValue(coord, out var work) ||
            work.Generation != outcome.Task.Generation)
            return;

        _materializationTasks.Remove(coord);
        _streaming.FinishMaterializing(coord);

        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Cancelled)
        {
            cancelledTasks++;
            return;
        }

        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Failed)
        {
            var error = outcome.Error ??
                new InvalidOperationException(
                    $"Chunk materialization failed without an exception: {coord}");
            failures.Add(new ChunkMaterializationFailure(coord, error));
            if (_acceptingMaterializationWork)
                RequeueIfDesired(coord, work.PendingSinceTimestamp);
            return;
        }

        if (outcome.Result is not ChunkMaterializationTaskOutput result ||
            result.Coord != coord)
        {
            failures.Add(new ChunkMaterializationFailure(
                coord,
                new InvalidOperationException(
                    $"Completed generation task returned an invalid chunk result: {coord}")));
            if (_acceptingMaterializationWork)
                RequeueIfDesired(coord, work.PendingSinceTimestamp);
            return;
        }

        stageTimings.AddRange(result.StageTimings);
        if (!_acceptingMaterializationWork)
            return;

        if (!_streaming.KeepsLoaded(coord) || _world.ContainsChunk(coord))
            return;

        _world.InsertChunk(coord, result.Chunk);
        var activation = ActivateResidentChunk(
            coord,
            ChunkActivationSource.Provider,
            result.WorkerMilliseconds,
            result.WorkerMilliseconds - result.InitialLightingMilliseconds,
            elapsedMilliseconds: 0d,
            result.InitialLightingMilliseconds,
            result.WorkerQueueWaitMilliseconds,
            result.WorkerWallMilliseconds,
            resultCollectionWaitMilliseconds: 0d,
            pendingQueueWaitMilliseconds: Stopwatch.GetElapsedTime(
                work.PendingSinceTimestamp,
                work.DispatchTimestamp).TotalMilliseconds,
            dispatchPreparationMilliseconds: result.DispatchPreparationMilliseconds,
            workerSliceCount: result.WorkerSliceCount,
            interSliceWaitMilliseconds: result.InterSliceWaitMilliseconds);
        var activatedTimestamp = Stopwatch.GetTimestamp();
        activations.Add(activation with
        {
            ElapsedMilliseconds = Stopwatch.GetElapsedTime(
                work.PendingSinceTimestamp,
                activatedTimestamp).TotalMilliseconds,
            ResultCollectionWaitMilliseconds = Stopwatch.GetElapsedTime(
                result.WorkerCompletedTimestamp,
                activatedTimestamp).TotalMilliseconds,
        });
    }

    private void CollectDirectionalFaceOutcome(
        ChunkGenerationTaskOutcome outcome,
        List<ChunkMaterializationFailure> failures,
        ref int cancelledTasks)
    {
        var sectionY = outcome.Task.Key.SectionY ?? 0;
        var coord = outcome.Task.Key.Column.ToSection(sectionY);
        if (!_directionalFaceReconciliationTasks.Remove(coord))
            return;

        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Cancelled)
        {
            cancelledTasks++;
            return;
        }
        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Failed)
        {
            failures.Add(new ChunkMaterializationFailure(
                coord,
                outcome.Error ?? new InvalidOperationException(
                    $"Directional face reconciliation failed: {coord}")));
            if (_acceptingMaterializationWork && _world.TryGetSavedChunk(coord, out _))
                _pendingDirectionalFaceReconciliations.Enqueue(coord);
            return;
        }
        if (!_acceptingMaterializationWork)
            return;

        if (outcome.Result is not DirectionalFacePaletteReconciliationResult result)
        {
            failures.Add(new ChunkMaterializationFailure(
                coord,
                new InvalidOperationException(
                    $"Directional face reconciliation returned an invalid result: {coord}")));
            return;
        }

        if (_directionalFacePaletteUpdater is not null &&
            _directionalFacePaletteUpdater.TryApplyChunkArrivalReconciliation(
                _world, result, out var visualChanges))
        {
            EnqueueDirectionalFaceVisualChanges(visualChanges);
        }
        else if (_world.TryGetSavedChunk(result.Arriving, out _))
        {
            _pendingDirectionalFaceReconciliations.Enqueue(result.Arriving);
        }
    }

    private void CollectChunkLightingOutcome(
        ChunkGenerationTaskOutcome outcome,
        List<ChunkMaterializationFailure> failures,
        ref int cancelledTasks)
    {
        var sectionY = outcome.Task.Key.SectionY ?? 0;
        var coord = outcome.Task.Key.Column.ToSection(sectionY);
        if (!_chunkLightingReconciliationTasks.Remove(coord, out var work))
            return;

        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Cancelled)
        {
            cancelledTasks++;
            return;
        }
        if (outcome.Kind == ChunkGenerationTaskOutcomeKind.Failed)
        {
            failures.Add(new ChunkMaterializationFailure(
                coord,
                outcome.Error ?? new InvalidOperationException(
                    $"Chunk lighting reconciliation failed: {coord}")));
            if (_acceptingMaterializationWork &&
                (_world.ContainsChunk(coord) || _world.HasArchivedChunk(coord)))
                _pendingChunkLightingReconciliations.Enqueue(coord);
            return;
        }
        if (!_acceptingMaterializationWork)
            return;

        if (outcome.Result is not ChunkArrivalReconciliationResult result)
        {
            failures.Add(new ChunkMaterializationFailure(
                coord,
                new InvalidOperationException(
                    $"Chunk lighting reconciliation returned an invalid result: {coord}")));
            return;
        }

        if (!_world.IsContentStampCurrent(work.ContentDependencies) ||
            !_world.AreLightingStampsCurrent(work.LightingDependencies))
        {
            if (_world.ContainsChunk(coord) || _world.HasArchivedChunk(coord))
                _pendingChunkLightingReconciliations.Enqueue(coord);
            return;
        }

        foreach (var position in result.LightingSeeds)
            _worldUpdates.EnqueueLighting(position);

        if (result.PhysicsPlan is { } physicsPlan)
            _blockPhysics.ApplyResidentChunkActivationPlan(physicsPlan);
    }

    private ChunkResidencyUpdate FinishMaterializationCollection(
        List<ChunkResidencyActivation> activations,
        List<ChunkMaterializationFailure> failures,
        List<ChunkMaterializationStageTiming> stageTimings,
        int processed,
        int cancelledTasks,
        IEnumerable<ChunkCoord> presented)
    {
        if (processed > 0 || activations.Count > 0)
            SyncResidentState(presented);
        if (activations.Count == 0 && failures.Count == 0 &&
            stageTimings.Count == 0 && cancelledTasks == 0)
            return ChunkResidencyUpdate.Empty;

        return new ChunkResidencyUpdate(
            activations, failures, stageTimings,
            StartedTasks: 0, CancelledTasks: cancelledTasks);
    }

    public ChunkResidencyUpdate DispatchMaterializationTasks(
        WorldFrameWorkBudget budget,
        IEnumerable<ChunkCoord> presented,
        int maximumNewTasks = int.MaxValue,
        bool? hasWorkerAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(presented);
        if (maximumNewTasks < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumNewTasks));
        if (!_acceptingMaterializationWork)
            return ChunkResidencyUpdate.Empty;

        var activations = new List<ChunkResidencyActivation>();
        var failures = new List<ChunkMaterializationFailure>();
        var startedTasks = 0;
        var restored = 0;
        var maximumTaskStarts = Math.Min(
            _settings.MaxDispatchesPerFrame, maximumNewTasks);
        // Terrain admission is independent of physical worker count. Keep one
        // additional bounded task slot available for arrival reconciliation;
        // worker threads remain capped by MaxConcurrentWorkers.
        var materializationLimit = _settings.MaxTerrainMaterializationsInFlight;

        // Archived restores do not consume a generation worker. The caller may
        // cap new materialization starts when downstream work is pressured;
        // spatial priority decides which chunk starts when admission opens.
        while (_materializationTasks.Count <
                    _settings.MaxTerrainMaterializationsInFlight &&
                _materializationTasks.Count < materializationLimit &&
                restored <
                    _settings.MaxResultsPerFrame)
        {
            if ((startedTasks > 0 || restored > 0) &&
                budget.Exhausted(Stopwatch.GetTimestamp()))
            {
                break;
            }

            var remainingSlots = Math.Max(
                1, materializationLimit - _materializationTasks.Count);
            var remainingStarts = Math.Max(
                0, maximumTaskStarts - startedTasks);
            // Admit a bounded vertical batch from one X/Z column. The sections
            // remain independent dispatcher tasks and can complete/publish
            // separately; grouping only amortizes spatial queue admission.
            var maximumSectionsInBatch = Math.Max(
                1, Math.Min(
                    remainingSlots,
                    remainingStarts == 0 ? 1 : remainingStarts));
            var pendingColumn = _streaming.PopPendingColumnEntryByPriority(
                maximumSections: maximumSectionsInBatch);
            if (pendingColumn is null)
                break;

            var sections = pendingColumn.Value.Sections;
            var stopAfterBatch = false;
            for (var sectionIndex = 0;
                 sectionIndex < sections.Count;
                 sectionIndex++)
            {
                var pendingEntry = sections[sectionIndex];
                var selected = pendingEntry.Coord;
                var dispatchTimestamp = Stopwatch.GetTimestamp();

                if ((startedTasks > 0 || restored > 0) &&
                    budget.Exhausted(Stopwatch.GetTimestamp()))
                {
                    RequeuePendingSections(sections, sectionIndex);
                    stopAfterBatch = true;
                    break;
                }

                if (!_streaming.IsDesired(selected) ||
                    _world.ContainsChunk(selected) ||
                    _materializationTasks.ContainsKey(selected))
                {
                    continue;
                }

                var restore = _world.RestoreChunk(selected);
                if (restore == ChunkRestoreResult.Restored)
                {
                    activations.Add(ActivateResidentChunk(
                        selected,
                        ChunkActivationSource.Archive,
                        workerMilliseconds: 0.0));
                    restored++;
                    if (restored >= _settings.MaxResultsPerFrame)
                    {
                        RequeuePendingSections(sections, sectionIndex + 1);
                        stopAfterBatch = true;
                        break;
                    }
                    continue;
                }

                if (restore == ChunkRestoreResult.AlreadyResident)
                    continue;

                if (startedTasks >= maximumTaskStarts)
                {
                    RequeuePendingSections(sections, sectionIndex);
                    stopAfterBatch = true;
                    break;
                }

                IChunkMaterializationJob job;
                try
                {
                    job = _chunkProvider is IIncrementalChunkProvider incremental
                        ? incremental.BeginMaterialization(selected)
                        : new ProviderMaterializationJob(_chunkProvider, selected);
                }
                catch (Exception error)
                {
                    failures.Add(new ChunkMaterializationFailure(selected, error));
                    RequeueIfDesired(selected, pendingEntry.EnqueuedTimestamp);
                    RequeuePendingSections(sections, sectionIndex + 1);
                    stopAfterBatch = true;
                    break;
                }

                if (job.Coord != selected)
                {
                    job.Dispose();
                    failures.Add(new ChunkMaterializationFailure(
                        selected,
                        new InvalidOperationException(
                            $"Materialization provider returned a job for {job.Coord}, expected {selected}.")));
                    RequeueIfDesired(selected, pendingEntry.EnqueuedTimestamp);
                    RequeuePendingSections(sections, sectionIndex + 1);
                    stopAfterBatch = true;
                    break;
                }

                if (_nextMaterializationGeneration == 0 ||
                    _nextMaterializationGeneration == ulong.MaxValue)
                {
                    job.Dispose();
                    throw new InvalidOperationException(
                        "Materialization task generation ID space was exhausted.");
                }

                var generation = _nextMaterializationGeneration++;
                var taskKey = new ChunkGenerationTaskKey(
                    ChunkColumnCoord.FromChunk(selected),
                    ChunkGenerationStepId.SectionReady,
                    selected.Y);
                var taskPriority = GenerationPriority(selected);
                var generationTask = new ChunkMaterializationGenerationTask(
                    job,
                    chunk => ChunkLightingSolver.Initialize(
                        chunk, _blocks, _fluids, _objects),
                    dispatchTimestamp);
                var work = new MaterializationWork(
                    taskKey,
                    generation,
                    pendingEntry.EnqueuedTimestamp,
                    dispatchTimestamp);
                _streaming.MarkMaterializing(selected);
                _materializationTasks.Add(selected, work);
                if (!_generationDispatcher.TryEnqueue(
                        taskKey,
                        generation,
                        taskPriority,
                        pendingEntry.EnqueuedTimestamp,
                        generationTask))
                {
                    _materializationTasks.Remove(selected);
                    generationTask.Dispose();
                    _streaming.FinishMaterializing(selected);
                    RequeueIfDesired(selected, pendingEntry.EnqueuedTimestamp);
                    RequeuePendingSections(sections, sectionIndex + 1);
                    stopAfterBatch = true;
                    break;
                }
                startedTasks++;
            }

            if (stopAfterBatch)
                break;
        }

        if (restored > 0)
            SyncResidentState(presented);

        UpdateGenerationTaskPriorities();
        _generationDispatcher.Pump(
            maximumCompletions: 0,
            maximumTaskAdvances: _settings.MaxGenerationTaskAdvancesPerFrame,
            maximumWorkerStarts: AvailableGenerationWorkerStarts);

        // Reconciliation uses Task.Run rather than the generation pool. Start
        // it only after generation dispatch has claimed its available worker
        // capacity, so the extra logical task slot never creates an extra CPU
        // worker beyond the configured physical limit.
        StartArrivalReconciliationWorker(
            budget, failures,
            hasWorkerAdmission: hasWorkerAdmission ?? maximumNewTasks > 0);

        if (activations.Count == 0 && failures.Count == 0 && startedTasks == 0)
            return ChunkResidencyUpdate.Empty;
        return new ChunkResidencyUpdate(
            activations,
            failures,
            Array.Empty<ChunkMaterializationStageTiming>(),
            startedTasks);
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
                _pendingChunkLightingReconciliations.Enqueue(
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
        double elapsedMilliseconds = 0d,
        double initialLightingMilliseconds = 0d,
        double workerQueueWaitMilliseconds = 0d,
        double workerWallMilliseconds = 0d,
        double resultCollectionWaitMilliseconds = 0d,
        double pendingQueueWaitMilliseconds = 0d,
        double dispatchPreparationMilliseconds = 0d,
        int workerSliceCount = 0,
        double interSliceWaitMilliseconds = 0d)
    {
        _streaming.EnqueuePresentation(coord);
        _blockPhysics.RequestChunkReconciliation(coord);
        EnqueueResidentChunkFluidActivation(coord);

        return new ChunkResidencyActivation(
            coord,
            source,
            workerMilliseconds,
            terrainMilliseconds,
            elapsedMilliseconds,
            initialLightingMilliseconds,
            workerQueueWaitMilliseconds,
            workerWallMilliseconds,
            resultCollectionWaitMilliseconds,
            pendingQueueWaitMilliseconds,
            dispatchPreparationMilliseconds,
            workerSliceCount,
            interSliceWaitMilliseconds);
    }

    private ChunkGenerationTaskPriority GenerationPriority(ChunkCoord coord)
    {
        var priority = ChunkStreamingState.StreamingPriority(
            coord, _center, _streaming.MovementDirection);
        return new ChunkGenerationTaskPriority(
            priority.StreamingBand,
            priority.HorizontalDistanceSquared,
            priority.TotalDistanceSquared);
    }

    private void UpdateGenerationTaskPriorities()
    {
        foreach (var (coord, work) in _materializationTasks)
            _generationDispatcher.UpdatePriority(
                work.TaskKey, GenerationPriority(coord));
    }

    private int AvailableGenerationWorkerStarts
    {
        get
        {
            var occupiedWorkerSlots =
                _generationWorkerPool.ActiveWorkerCount +
                _generationWorkerPool.PendingWorkCount +
                _directionalFaceReconciliationTasks.Values.Count(work =>
                    !work.WorkerTask.IsCompleted) +
                _chunkLightingReconciliationTasks.Values.Count(work =>
                    !work.WorkerTask.IsCompleted);
            return Math.Max(
                0, _settings.MaxConcurrentWorkers - occupiedWorkerSlots);
        }
    }

    private void StartArrivalReconciliationWorker(
        WorldFrameWorkBudget budget,
        List<ChunkMaterializationFailure> failures,
        bool hasWorkerAdmission)
    {
        if (!hasWorkerAdmission || !_acceptingMaterializationWork ||
            budget.Exhausted(Stopwatch.GetTimestamp()))
            return;

        var occupiedWorkerSlots =
            _generationWorkerPool.PendingWorkCount +
            _generationWorkerPool.ActiveWorkerCount +
            _generationDispatcher.PendingWorkerOperationCount +
            _directionalFaceReconciliationTasks.Values.Count(work =>
                !work.WorkerTask.IsCompleted) +
            _chunkLightingReconciliationTasks.Values.Count(work =>
                !work.WorkerTask.IsCompleted);
        if (occupiedWorkerSlots >= _settings.MaxConcurrentWorkers)
            return;

        var started = _preferChunkLightingReconciliation
            ? StartChunkLightingReconciliationWorker(failures)
            : StartDirectionalFaceReconciliationWorker(failures);
        if (!started)
            started = _preferChunkLightingReconciliation
                ? StartDirectionalFaceReconciliationWorker(failures)
                : StartChunkLightingReconciliationWorker(failures);
        if (started)
            _preferChunkLightingReconciliation = !_preferChunkLightingReconciliation;
    }

    private bool StartChunkLightingReconciliationWorker(
        List<ChunkMaterializationFailure> failures)
    {
        if (_chunkLightingReconciliationTasks.Count > 0 ||
            _pendingChunkLightingReconciliations.Count == 0 ||
            _materializationTasks.Count +
                _directionalFaceReconciliationTasks.Count +
                _chunkLightingReconciliationTasks.Count >=
                _settings.MaxTerrainMaterializationsInFlight + 1)
            return false;

        if (!_pendingChunkLightingReconciliations.TryDequeue(out var coord))
            return false;
        if (!_world.ContainsChunk(coord) && !_world.HasArchivedChunk(coord))
            return false;

        try
        {
            var dependencies = LightingReconciliationDependencies(coord);
            var snapshotWorld = _world.CloneForWorker(dependencies);
            var work = new ChunkLightingReconciliationWork(
                coord,
                _world.CaptureContentStamp(dependencies),
                _world.CaptureLightingStamps(dependencies));
            work.WorkerTask = Task.Run(() =>
            {
                var lightingSeeds = ChunkTopologyFrontier.ArrivalLightingSeeds(
                    snapshotWorld, _blocks, _fluids, coord, _objects);
                var physicsPlan = snapshotWorld.ContainsChunk(coord)
                    ? _blockPhysics.CalculateResidentChunkActivationPlan(
                        snapshotWorld, coord)
                    : (BlockPhysicsChunkActivationPlan?)null;
                return new ChunkArrivalReconciliationResult(
                    lightingSeeds, physicsPlan);
            });
            _chunkLightingReconciliationTasks.Add(coord, work);
            return true;
        }
        catch (Exception error)
        {
            failures.Add(new ChunkMaterializationFailure(coord, error));
            _pendingChunkLightingReconciliations.Enqueue(coord);
            return false;
        }
    }

    private static ChunkCoord[] LightingReconciliationDependencies(ChunkCoord coord)
    {
        var dependencies = new List<ChunkCoord>(7) { coord };
        if (coord.Y < int.MaxValue)
            dependencies.Add(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
        if (coord.Y > 0)
            dependencies.Add(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
        if (coord.X > int.MinValue)
            dependencies.Add(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
        if (coord.X < int.MaxValue)
            dependencies.Add(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
        if (coord.Z > int.MinValue)
            dependencies.Add(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
        if (coord.Z < int.MaxValue)
            dependencies.Add(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));
        return dependencies.ToArray();
    }

    private bool StartDirectionalFaceReconciliationWorker(
        List<ChunkMaterializationFailure> failures)
    {
        var updater = _directionalFacePaletteUpdater;
        if (updater is null ||
            _directionalFaceReconciliationTasks.Count > 0 ||
            _pendingDirectionalFaceReconciliations.Count == 0 ||
            _materializationTasks.Count +
                _directionalFaceReconciliationTasks.Count +
                _chunkLightingReconciliationTasks.Count >=
                _settings.MaxTerrainMaterializationsInFlight + 1)
            return false;

        if (!_pendingDirectionalFaceReconciliations.TryDequeue(out var coord))
            return false;
        if (!_world.TryGetSavedChunk(coord, out _))
            return false;

        try
        {
            var snapshot =
                updater.CaptureChunkArrivalSnapshot(_world, coord);
            var work = new DirectionalFaceReconciliationWork(snapshot);
            work.WorkerTask = Task.Run(() => updater.ReconcileChunkArrival(snapshot));
            _directionalFaceReconciliationTasks.Add(coord, work);
            return true;
        }
        catch (Exception error)
        {
            failures.Add(new ChunkMaterializationFailure(coord, error));
            _pendingDirectionalFaceReconciliations.Enqueue(coord);
            return false;
        }
    }

    private sealed class DirectionalFaceReconciliationWork
    {
        public DirectionalFaceReconciliationWork(
            DirectionalFacePaletteReconciliationSnapshot snapshot)
        {
            Snapshot = snapshot ??
                throw new ArgumentNullException(nameof(snapshot));
        }

        public DirectionalFacePaletteReconciliationSnapshot Snapshot { get; }

        public Task<DirectionalFacePaletteReconciliationResult> WorkerTask { get; set; } = null!;
    }

    private sealed record ChunkArrivalReconciliationResult(
        IReadOnlyList<WorldVoxelCoord> LightingSeeds,
        BlockPhysicsChunkActivationPlan? PhysicsPlan);

    private sealed class ChunkLightingReconciliationWork
    {
        public ChunkLightingReconciliationWork(
            ChunkCoord coord,
            ChunkContentStamp contentDependencies,
            IReadOnlyList<VoxelLightChunkStamp> lightingDependencies)
        {
            Coord = coord;
            ContentDependencies = contentDependencies ??
                throw new ArgumentNullException(nameof(contentDependencies));
            LightingDependencies = lightingDependencies ??
                throw new ArgumentNullException(nameof(lightingDependencies));
        }

        public ChunkCoord Coord { get; }
        public ChunkContentStamp ContentDependencies { get; }
        public IReadOnlyList<VoxelLightChunkStamp> LightingDependencies { get; }
        public Task<ChunkArrivalReconciliationResult> WorkerTask { get; set; } = null!;
    }

    private sealed record MaterializationWork(
        ChunkGenerationTaskKey TaskKey,
        ulong Generation,
        long PendingSinceTimestamp,
        long DispatchTimestamp);

    private sealed class ProviderMaterializationJob : IChunkMaterializationJob
    {
        private readonly IChunkProvider _provider;

        public ProviderMaterializationJob(IChunkProvider provider, ChunkCoord coord)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Coord = coord;
        }

        public ChunkCoord Coord { get; }

        public ChunkMaterializationStage Stage { get; private set; } =
            ChunkMaterializationStage.ProviderFallback;

        public bool IsComplete => Stage == ChunkMaterializationStage.Complete;

        public Chunk? Result { get; private set; }

        public void Advance()
        {
            if (IsComplete)
                throw new InvalidOperationException(
                    "A completed materialization job cannot advance.");

            Result = _provider.Materialize(Coord) ??
                throw new InvalidOperationException(
                    "Chunk provider returned no materialized chunk.");
            Stage = ChunkMaterializationStage.Complete;
        }

        public void Dispose()
        {
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
        if (_pendingFluidActivationCoords.Count == 0)
        {
            return new FluidActivationWorkReport(
                0, 0, 0, 0, 0, 0d, 0d, 0);
        }

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
                    if (ShouldWakeResidentFluid(
                            chunk, x, y, z, position, fluid))
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
        Chunk chunk,
        int localX,
        int localY,
        int localZ,
        WorldVoxelCoord position,
        FluidCell fluid)
    {
        if (!fluid.IsSource)
        {
            return true;
        }

        var voxelIndex =
            localX + Chunk.Size * (localZ + Chunk.Size * localY);
        foreach (var offset in FluidSpreadTargets)
        {
            var targetX = localX + offset.X;
            var targetY = localY + offset.Y;
            var targetZ = localZ + offset.Z;
            VoxelCell targetCell;
            FluidCell neighbor;

            if ((uint)targetX < Chunk.Size &&
                (uint)targetY < Chunk.Size &&
                (uint)targetZ < Chunk.Size)
            {
                // Most generated-fluid neighbors are inside this same chunk.
                // Read the known-valid linear slot directly to avoid repeated
                // coordinate validation, index calculation and world lookups.
                var targetIndex =
                    voxelIndex + offset.X +
                    Chunk.Size * (offset.Z + Chunk.Size * offset.Y);
                targetCell = chunk.GetCellAtLinearIndexUnchecked(targetIndex);
                neighbor = chunk.GetFluidAtLinearIndexUnchecked(targetIndex);
            }
            else
            {
                var target = position + offset;
                if (target.Y < 0 ||
                    !_world.TryGetCellAndFluid(
                        target,
                        out targetCell,
                        out neighbor))
                {
                    // Missing chunks cannot accept flow. Their arrival performs
                    // the normal frontier recheck.
                    continue;
                }
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

    private void RequeuePendingSections(
        IReadOnlyList<ChunkPendingDispatch> sections,
        int startIndex)
    {
        for (var index = startIndex; index < sections.Count; index++)
        {
            var section = sections[index];
            RequeueIfDesired(section.Coord, section.EnqueuedTimestamp);
        }
    }

    private void RequeueIfDesired(
        ChunkCoord coord,
        long? enqueuedTimestamp = null)
    {
        if (!_streaming.IsDesired(coord))
            return;

        if (enqueuedTimestamp is { } timestamp)
            _streaming.EnqueuePending(coord, timestamp);
        else
            _streaming.EnqueuePending(coord);
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

}
