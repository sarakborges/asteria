using System.Collections.Immutable;
using System.Diagnostics;

namespace Asteria.Core.World;

/// <summary>
/// Detached output from one completed materialization task. The chunk is not
/// inserted into VoxelWorld here; the owning world loop must validate its
/// streaming ticket and then publish it atomically. DependencyJob is populated
/// only when a nested task transfers ownership of its completed job to a parent.
/// </summary>
public sealed record ChunkMaterializationTaskOutput(
    ChunkCoord Coord,
    Chunk Chunk,
    ImmutableArray<ChunkMaterializationStageTiming> StageTimings,
    double WorkerMilliseconds,
    double InitialLightingMilliseconds,
    double DispatchPreparationMilliseconds,
    double WorkerQueueWaitMilliseconds,
    double WorkerWallMilliseconds,
    long WorkerCompletedTimestamp,
    int WorkerSliceCount,
    double InterSliceWaitMilliseconds,
    IChunkMaterializationJob? CompletedDependencyJob = null);

/// <summary>One worker result: either a completed chunk or a real chunk dependency.</summary>
internal sealed record ChunkMaterializationWorkerSlice(
    ChunkCoord Coord,
    ImmutableArray<ChunkMaterializationStageTiming> StageTimings,
    double WorkerMilliseconds,
    bool IsComplete,
    Chunk? CompletedChunk,
    double InitialLightingMilliseconds,
    long WorkerStartedTimestamp,
    long WorkerCompletedTimestamp,
    IChunkMaterializationJob? PendingDependency = null);

/// <summary>
/// Adapts a stateful materializer job to the cooperative generation scheduler.
/// Synchronous stages run to completion in one worker operation. When the job
/// exposes another chunk job as a dependency, this task suspends, schedules the
/// dependency as a separate task, and resumes the parent only after it completes.
/// This mirrors ChunkGenerationTask.runUntilWait rather than recursively
/// materializing a lower chunk on the same worker.
/// </summary>
public sealed class ChunkMaterializationGenerationTask :
    IChunkGenerationTask,
    IDisposable
{
    /// <summary>
    /// Legacy source-compatibility value. Worker operations are no longer
    /// split by advance count; they run until completion or a real dependency.
    /// </summary>
    public const int MaximumAdvancesPerWorkerOperation = 32;

    private readonly IChunkMaterializationJob _job;
    private readonly Action<Chunk>? _initializeLighting;
    private readonly long _dispatchTimestamp;
    private readonly long _createdTimestamp;
    private readonly bool _returnJobOnCompletion;
    private readonly bool _deferDependenciesToColumn;
    private readonly List<ChunkMaterializationStageTiming> _stageTimings = [];
    private readonly Dictionary<ulong, ulong> _activeDependencyOperationIds = [];
    private ulong _nextDependencyId = 1;
    private ulong _lastDependencyId;
    private bool _hasScheduledOperation;
    private bool _biomesStatusPublished;
    private bool _biomesStatusPending;
    private bool _terrainStatusPublished;
    private bool _terrainStatusPending;
    private bool _featuresStatusPublished;
    private bool _waitingForLighting;
    private bool _lightingInitialized;
    private bool _hasStartedWorker;
    private bool _isCompleted;
    private bool _isDisposed;
    private bool _jobOwnershipTransferred;
    private long _firstWorkerStartedTimestamp;
    private long _workerCompletedTimestamp;
    private double _workerMilliseconds;
    private double _initialLightingMilliseconds;
    private double _interSliceWaitMilliseconds;
    private int _workerOperationCount;
    private IChunkMaterializationJob? _pendingDependencyJob;
    private ChunkMaterializationGenerationTask? _activeDependencyTask;

    public ChunkMaterializationGenerationTask(
        IChunkMaterializationJob job,
        Action<Chunk>? initializeLighting,
        long dispatchTimestamp = 0,
        bool returnJobOnCompletion = false,
        bool deferDependenciesToColumn = false)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _initializeLighting = initializeLighting;
        _dispatchTimestamp = dispatchTimestamp == 0
            ? Stopwatch.GetTimestamp()
            : dispatchTimestamp;
        _createdTimestamp = Stopwatch.GetTimestamp();
        _returnJobOnCompletion = returnJobOnCompletion;
        _deferDependenciesToColumn = deferDependenciesToColumn;
        // A nested dependency job remains owned by the parent materialization
        // job until it is returned in a completion payload. On cancellation,
        // the parent's Dispose path is the single owner that releases it.
        _jobOwnershipTransferred = returnJobOnCompletion;
    }

    public ChunkCoord Coord => _job.Coord;

    /// <summary>Exact vertical section requested by the current materialization dependency.</summary>
    public ChunkCoord? PendingDependencyCoord => _pendingDependencyJob?.Coord;

    /// <summary>
    /// Pending support-only section job owned by this materialization job. The
    /// column scheduler may admit it as an independent dynamic section task.
    /// </summary>
    public IChunkMaterializationJob? PendingDependencyJob => _pendingDependencyJob;

    /// <summary>Whether an out-of-column dependency is already being advanced by this task.</summary>
    public bool HasActiveDependencyTask => _activeDependencyTask is not null;

    /// <summary>
    /// Satisfies a pending dependency with a section already generated by the
    /// owning column task. The receiver consumes the detached chunk result and
    /// releases its redundant support-only job.
    /// </summary>
    public void ResolvePendingDependency(
        ChunkCoord dependencyCoord,
        Chunk completedChunk)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(completedChunk);
        if (!_deferDependenciesToColumn)
            throw new InvalidOperationException(
                "Only a column-owned materialization task can resolve a sibling section dependency.");
        if (_activeDependencyTask is not null)
            throw new InvalidOperationException(
                "An external dependency is already active for this materialization task.");
        var pending = _pendingDependencyJob ??
            throw new InvalidOperationException(
                $"Materialization section {_job.Coord} has no pending dependency.");
        if (pending.Coord != dependencyCoord)
            throw new InvalidOperationException(
                $"Materialization section {_job.Coord} expected dependency {pending.Coord}, not {dependencyCoord}.");
        if (_job is not IChunkMaterializationDependencyReceiver receiver)
            throw new InvalidOperationException(
                $"Materialization job {_job.Coord} cannot consume a completed section dependency.");

        receiver.ResolveDependency(dependencyCoord, completedChunk);
        _pendingDependencyJob = null;
    }

    /// <summary>Starts the nested job only when the required section is outside this column task.</summary>
    public ChunkGenerationTaskAdvanceResult AdvancePendingDependency(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_deferDependenciesToColumn)
            throw new InvalidOperationException(
                "Only a column-owned materialization task can delegate dependency admission.");
        if (_pendingDependencyJob is null)
            throw new InvalidOperationException(
                $"Materialization section {_job.Coord} has no pending dependency to advance.");
        return _activeDependencyTask is not null
            ? AdvanceActiveDependencyTask(
                Array.Empty<ChunkGenerationDependencyCompletion>(), cancellationToken)
            : BeginDependencyTask(cancellationToken);
    }

    /// <summary>
    /// Execution lane required by the next bounded continuation. The column
    /// parent uses this to batch independent worker forks across vertical
    /// sections while advancing no more than one ordered mutation stage.
    /// </summary>
    public ChunkGenerationExecutionPolicy NextExecutionPolicy
    {
        get
        {
            if (_activeDependencyTask is not null)
                return _activeDependencyTask.NextExecutionPolicy;
            if (_hasScheduledOperation)
                throw new InvalidOperationException(
                    $"A materialization task with an in-flight operation is not ready: {_job.Coord}");
            if (_biomesStatusPending || _terrainStatusPending)
                return ChunkGenerationExecutionPolicy.Coordinator;
            if (_job.IsComplete)
            {
                if (!_featuresStatusPublished)
                    return ChunkGenerationExecutionPolicy.Coordinator;
                if (_initializeLighting is not null && !_lightingInitialized)
                    return ChunkGenerationExecutionPolicy.LightExecutor;
                return ChunkGenerationExecutionPolicy.MainThreadIntegration;
            }

            return AsteriaChunkGenerationPlan.GetExecutionPolicy(
                _job.ActiveWorkStage);
        }
    }

    public ChunkGenerationTaskAdvanceResult Advance(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted)
            throw new InvalidOperationException(
                "A completed materialization task cannot advance.");
        ArgumentNullException.ThrowIfNull(completedDependencies);
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeDependencyTask is not null)
        {
            if (_activeDependencyOperationIds.Count == 0)
            {
                if (completedDependencies.Count != 0)
                    throw new InvalidOperationException(
                        "An inline dependency continuation cannot have worker completions.");
                return AdvanceActiveDependencyTask(
                    Array.Empty<ChunkGenerationDependencyCompletion>(),
                    cancellationToken);
            }

            return ResumeDependencyTask(
                completedDependencies, cancellationToken);
        }

        var collectedAsynchronousCompletion = false;
        var collectedLightingCompletion = false;
        if (_hasScheduledOperation)
        {
            collectedLightingCompletion = _waitingForLighting;
            if (collectedLightingCompletion)
                CollectLightingCompletion(completedDependencies);
            else
                CollectWorkerSlice(completedDependencies);
            collectedAsynchronousCompletion = true;
        }
        else if (completedDependencies.Count != 0)
            throw new InvalidOperationException(
                "The first materialization operation cannot have worker completions.");

        if (_biomesStatusPending)
        {
            _biomesStatusPending = false;
            return ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                ChunkGenerationStatus.Biomes);
        }
        if (_terrainStatusPending)
        {
            _terrainStatusPending = false;
            return ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                ChunkGenerationStatus.Terrain);
        }

        // A worker completion only makes the next stage runnable. Return to
        // the column scheduler before performing another stage so sibling
        // vertical sections and other columns get a fair serial continuation.
        if (collectedAsynchronousCompletion && !collectedLightingCompletion)
            return ChunkGenerationTaskAdvanceResult.Yield;

        // Completing the light future is the final generation step. The
        // detached completed chunk can now be handed to the owner thread for
        // FULL publication without another empty continuation.
        return AdvanceParent(cancellationToken);
    }

    private ChunkGenerationTaskAdvanceResult AdvanceParent(
        CancellationToken cancellationToken)
    {
        if (_pendingDependencyJob is not null)
            return _deferDependenciesToColumn
                ? ChunkGenerationTaskAdvanceResult.Yield
                : BeginDependencyTask(cancellationToken);

        if (_job.IsComplete)
        {
            var chunk = _job.Result ??
                throw new InvalidOperationException(
                    $"Completed materialization job returned no chunk: {_job.Coord}");
            if (!_featuresStatusPublished)
            {
                _featuresStatusPublished = true;
                return ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                    ChunkGenerationStatus.Features);
            }
            if (_initializeLighting is not null && !_lightingInitialized)
                return ScheduleInitialLighting(chunk);
            return Complete(chunk);
        }

        var policy = AsteriaChunkGenerationPlan.GetExecutionPolicy(
            _job.ActiveWorkStage);
        return policy switch
        {
            ChunkGenerationExecutionPolicy.SharedWorkerPool =>
                ScheduleParentOperation(),
            ChunkGenerationExecutionPolicy.Coordinator or
            ChunkGenerationExecutionPolicy.OrderedMutation =>
                AdvanceParentInline(cancellationToken),
            ChunkGenerationExecutionPolicy.LightExecutor or
            ChunkGenerationExecutionPolicy.MainThreadIntegration =>
                throw new InvalidOperationException(
                    $"Materialization stage {_job.ActiveWorkStage} cannot run in the section materializer."),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
    }

    private ChunkGenerationTaskAdvanceResult ScheduleInitialLighting(
        Chunk chunk)
    {
        if (_initializeLighting is null || _lightingInitialized || _waitingForLighting)
            throw new InvalidOperationException(
                $"Initial lighting was scheduled in an invalid state: {_job.Coord}");

        var dependencyId = NextDependencyId();
        _lastDependencyId = dependencyId;
        _hasScheduledOperation = true;
        _waitingForLighting = true;

        return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
        [
            new ChunkGenerationWorkerOperation(
                dependencyId,
                cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var started = Stopwatch.GetTimestamp();
                    _initializeLighting(chunk);
                    var completed = Stopwatch.GetTimestamp();
                    return new ChunkLightingWorkerResult(
                        Stopwatch.GetElapsedTime(started, completed).TotalMilliseconds,
                        started,
                        completed);
                },
                ChunkGenerationExecutionPolicy.LightExecutor),
        ],
        progressStatus: ChunkGenerationStatus.InitializeLight);
    }

    private void CollectLightingCompletion(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completions)
    {
        if (!_waitingForLighting || completions.Count != 1)
            throw new InvalidOperationException(
                $"Expected one serial-light completion for {_job.Coord}, received {completions.Count}.");

        var completion = completions[0];
        if (completion.DependencyId != _lastDependencyId ||
            completion.Result is not ChunkLightingWorkerResult result)
        {
            throw new InvalidOperationException(
                $"Serial-light executor returned a mismatched completion for {_job.Coord}.");
        }

        _waitingForLighting = false;
        _hasScheduledOperation = false;
        _lightingInitialized = true;
        _initialLightingMilliseconds += result.ElapsedMilliseconds;
        _workerMilliseconds += result.ElapsedMilliseconds;
        _workerOperationCount++;
        if (!_hasStartedWorker)
        {
            _hasStartedWorker = true;
            _firstWorkerStartedTimestamp = result.StartedTimestamp;
        }
        _workerCompletedTimestamp = result.CompletedTimestamp;
    }

    private ChunkGenerationTaskAdvanceResult AdvanceParentInline(
        CancellationToken cancellationToken)
    {
        var slice = AdvanceSingleStage(cancellationToken);
        ApplySlice(slice);
        if (_pendingDependencyJob is not null)
            return _deferDependenciesToColumn
                ? ChunkGenerationTaskAdvanceResult.Yield
                : BeginDependencyTask(cancellationToken);

        if (!_biomesStatusPublished &&
            slice.StageTimings.Any(timing =>
                timing.Stage == ChunkMaterializationStage.TerrainSetup) &&
            _job.ActiveWorkStage != ChunkMaterializationStage.TerrainSetup)
        {
            _biomesStatusPublished = true;
            return ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                ChunkGenerationStatus.Biomes);
        }

        // Publish TERRAIN only after the last terrain row has been written.
        if (!_terrainStatusPublished &&
            slice.StageTimings.Any(timing =>
                timing.Stage == ChunkMaterializationStage.TerrainLayer) &&
            _job.ActiveWorkStage != ChunkMaterializationStage.TerrainLayer)
        {
            _terrainStatusPublished = true;
            return ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                ChunkGenerationStatus.Terrain);
        }

        if (_job.IsComplete)
            return AdvanceParent(cancellationToken);

        // One ordered stage quantum per continuation. Yielding lets the
        // dimension dispatcher advance other chunk columns before resuming.
        return ChunkGenerationTaskAdvanceResult.Yield;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _activeDependencyTask?.Dispose();
        _activeDependencyTask = null;
        if (!_jobOwnershipTransferred)
            _job.Dispose();
        _pendingDependencyJob = null;
        _activeDependencyOperationIds.Clear();
    }

    private ChunkGenerationTaskAdvanceResult BeginDependencyTask(
        CancellationToken cancellationToken)
    {
        var dependency = _pendingDependencyJob ??
            throw new InvalidOperationException(
                "A dependency task was requested without a pending materialization job.");

        if (ReferenceEquals(dependency, _job))
            throw new InvalidOperationException(
                "A materialization job cannot depend on itself.");
        if (dependency.IsComplete)
            throw new InvalidOperationException(
                "A completed materialization job was exposed as a pending dependency.");

        _activeDependencyTask = new ChunkMaterializationGenerationTask(
            dependency,
            initializeLighting: null,
            dispatchTimestamp: Stopwatch.GetTimestamp(),
            returnJobOnCompletion: true);
        return AdvanceActiveDependencyTask(
            Array.Empty<ChunkGenerationDependencyCompletion>(),
            cancellationToken);
    }

    private ChunkGenerationTaskAdvanceResult ResumeDependencyTask(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completions,
        CancellationToken cancellationToken)
    {
        if (completions.Count != _activeDependencyOperationIds.Count)
            throw new InvalidOperationException(
                $"Expected {_activeDependencyOperationIds.Count} dependency completions for {_job.Coord}, " +
                $"received {completions.Count}.");

        var childCompletions = new List<ChunkGenerationDependencyCompletion>(
            completions.Count);
        foreach (var completion in completions)
        {
            if (completion.Error is not null)
                throw new InvalidOperationException(
                    $"A chunk dependency failed while materializing {_job.Coord}.",
                    completion.Error);

            if (!_activeDependencyOperationIds.Remove(
                    completion.DependencyId, out var childDependencyId))
                throw new InvalidOperationException(
                    $"Unknown parent dependency operation {completion.DependencyId} for {_job.Coord}.");

            childCompletions.Add(new ChunkGenerationDependencyCompletion(
                completion.Task,
                WaitToken: 1,
                childDependencyId,
                completion.Result));
        }

        _hasScheduledOperation = false;
        return AdvanceActiveDependencyTask(childCompletions, cancellationToken);
    }

    private ChunkGenerationTaskAdvanceResult AdvanceActiveDependencyTask(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completions,
        CancellationToken cancellationToken)
    {
        var dependencyAdvance = (_activeDependencyTask ??
            throw new InvalidOperationException(
                "A chunk dependency continuation was requested without an active task."))
            .Advance(completions, cancellationToken);

        return dependencyAdvance.Kind switch
        {
            ChunkGenerationTaskAdvanceKind.WaitForWorkers =>
                WrapDependencyOperations(dependencyAdvance),
            ChunkGenerationTaskAdvanceKind.Yield =>
                ChunkGenerationTaskAdvanceResult.Yield,
            ChunkGenerationTaskAdvanceKind.Completed =>
                CompleteDependencyTask(dependencyAdvance, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(dependencyAdvance)),
        };
    }

    private ChunkGenerationTaskAdvanceResult CompleteDependencyTask(
        ChunkGenerationTaskAdvanceResult dependencyAdvance,
        CancellationToken cancellationToken)
    {
        if (dependencyAdvance.CompletionPayload is not ChunkMaterializationTaskOutput dependencyOutput ||
            dependencyOutput.CompletedDependencyJob is null)
        {
            throw new InvalidOperationException(
                $"Chunk dependency task for {_job.Coord} completed without transferring its job.");
        }

        if (!ReferenceEquals(dependencyOutput.CompletedDependencyJob, _pendingDependencyJob))
            throw new InvalidOperationException(
                $"Chunk dependency task returned a different job than the parent requested: {_job.Coord}");

        _stageTimings.AddRange(dependencyOutput.StageTimings);
        _workerMilliseconds += dependencyOutput.WorkerMilliseconds;
        _initialLightingMilliseconds += dependencyOutput.InitialLightingMilliseconds;
        _workerOperationCount += dependencyOutput.WorkerSliceCount;
        _interSliceWaitMilliseconds += dependencyOutput.InterSliceWaitMilliseconds;
        _workerCompletedTimestamp = dependencyOutput.WorkerCompletedTimestamp;

        // The child task relinquished ownership in its output. Disposing its
        // wrapper is safe; the parent job still owns the completed dependency.
        _activeDependencyTask?.Dispose();
        _activeDependencyTask = null;
        _pendingDependencyJob = null;
        _activeDependencyOperationIds.Clear();

        // Resolving a nested support dependency only makes the parent stage
        // runnable. Let the column scheduler select the next section fairly.
        return ChunkGenerationTaskAdvanceResult.Yield;
    }

    private ChunkGenerationTaskAdvanceResult WrapDependencyOperations(
        ChunkGenerationTaskAdvanceResult childAdvance)
    {
        if (childAdvance.Kind != ChunkGenerationTaskAdvanceKind.WaitForWorkers ||
            childAdvance.WorkerOperations.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "A pending chunk dependency did not schedule worker operations.");
        }

        var wrapped = new List<ChunkGenerationWorkerOperation>(
            childAdvance.WorkerOperations.Length);
        foreach (var operation in childAdvance.WorkerOperations)
        {
            var parentDependencyId = NextDependencyId();
            if (!_activeDependencyOperationIds.TryAdd(
                    parentDependencyId, operation.DependencyId))
                throw new InvalidOperationException(
                    $"Duplicate dependency operation ID {parentDependencyId}: {_job.Coord}");

            wrapped.Add(new ChunkGenerationWorkerOperation(
                parentDependencyId,
                operation.Execute,
                operation.ExecutionPolicy));
        }

        return ChunkGenerationTaskAdvanceResult.WaitForWorkers(wrapped);
    }

    private ChunkGenerationTaskAdvanceResult ScheduleParentOperation()
    {
        var dependencyId = NextDependencyId();
        _lastDependencyId = dependencyId;
        _hasScheduledOperation = true;

        return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
        [
            new ChunkGenerationWorkerOperation(
                dependencyId,
                token => AdvanceSingleStage(token)),
        ]);
    }

    private ulong NextDependencyId()
    {
        if (_nextDependencyId == 0 || _nextDependencyId == ulong.MaxValue)
            throw new InvalidOperationException(
                "Materialization worker dependency ID space was exhausted.");
        return _nextDependencyId++;
    }

    private void CollectWorkerSlice(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completions)
    {
        if (completions.Count != 1)
            throw new InvalidOperationException(
                $"Expected one materialization worker completion, received {completions.Count}.");
        var completion = completions[0];
        if (completion.DependencyId != _lastDependencyId ||
            completion.Result is not ChunkMaterializationWorkerSlice slice)
        {
            throw new InvalidOperationException(
                "Materialization worker returned a mismatched or invalid stage result.");
        }

        _hasScheduledOperation = false;
        ApplySlice(slice);
        if (!_biomesStatusPublished &&
            slice.StageTimings.Any(timing =>
                timing.Stage == ChunkMaterializationStage.TerrainSetup) &&
            _job.ActiveWorkStage != ChunkMaterializationStage.TerrainSetup)
        {
            _biomesStatusPublished = true;
            _biomesStatusPending = true;
        }
        if (!_terrainStatusPublished &&
            _job.ActiveWorkStage != ChunkMaterializationStage.TerrainLayer &&
            (slice.StageTimings.Any(timing =>
                 timing.Stage == ChunkMaterializationStage.TerrainLayer) ||
             slice.StageTimings.Any(timing =>
                 timing.Stage == ChunkMaterializationStage.TerrainSetup) &&
             _job.ActiveWorkStage != ChunkMaterializationStage.TerrainLayer))
        {
            _terrainStatusPublished = true;
            _terrainStatusPending = true;
        }
    }

    private void ApplySlice(ChunkMaterializationWorkerSlice slice)
    {
        if (slice.StageTimings.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Materialization advance returned an empty stage-timing batch.");

        if (slice.IsComplete)
        {
            if (slice.CompletedChunk is null || slice.PendingDependency is not null)
                throw new InvalidOperationException(
                    "Completed materialization advance returned an invalid result.");
        }
        else if (slice.CompletedChunk is not null)
        {
            throw new InvalidOperationException(
                "Incomplete materialization advance returned a completed chunk.");
        }

        _stageTimings.AddRange(slice.StageTimings);
        _workerMilliseconds += slice.WorkerMilliseconds;
        _workerOperationCount++;
        if (!_hasStartedWorker)
        {
            _hasStartedWorker = true;
            _firstWorkerStartedTimestamp = slice.WorkerStartedTimestamp;
        }
        _workerCompletedTimestamp = slice.WorkerCompletedTimestamp;
        _initialLightingMilliseconds += slice.InitialLightingMilliseconds;
        _pendingDependencyJob = slice.PendingDependency;
    }

    private ChunkGenerationTaskAdvanceResult Complete(Chunk chunk)
    {
        if (!_isCompleted)
            _isCompleted = true;

        if (_initializeLighting is not null && !_lightingInitialized)
            throw new InvalidOperationException(
                $"Chunk {_job.Coord} reached completion before serial lighting finished.");

        var dispatchPreparation = Stopwatch.GetElapsedTime(
            _dispatchTimestamp, _createdTimestamp).TotalMilliseconds;
        var queueWait = _hasStartedWorker
            ? Stopwatch.GetElapsedTime(
                _createdTimestamp, _firstWorkerStartedTimestamp).TotalMilliseconds
            : 0d;
        var workerWall = _hasStartedWorker
            ? Stopwatch.GetElapsedTime(
                _firstWorkerStartedTimestamp, _workerCompletedTimestamp).TotalMilliseconds
            : 0d;

        IChunkMaterializationJob? completedDependencyJob = null;
        if (_returnJobOnCompletion)
        {
            completedDependencyJob = _job;
            _jobOwnershipTransferred = true;
        }

        return ChunkGenerationTaskAdvanceResult.CompletedWith(
            new ChunkMaterializationTaskOutput(
                _job.Coord,
                chunk,
                _stageTimings.ToImmutableArray(),
                _workerMilliseconds,
                _initialLightingMilliseconds,
                dispatchPreparation,
                queueWait,
                workerWall,
                _workerCompletedTimestamp,
                _workerOperationCount,
                _interSliceWaitMilliseconds,
                completedDependencyJob),
            progressStatus: _initializeLighting is null
                ? ChunkGenerationStatus.Features
                : ChunkGenerationStatus.Light);
    }

    private readonly record struct ChunkLightingWorkerResult(
        double ElapsedMilliseconds,
        long StartedTimestamp,
        long CompletedTimestamp);

    private ChunkMaterializationWorkerSlice AdvanceSingleStage(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_job.IsComplete)
            throw new InvalidOperationException(
                $"Completed materialization job was advanced again: {_job.Coord}");

        var workerStartedTimestamp = Stopwatch.GetTimestamp();
        var coord = _job.ActiveWorkCoord;
        var stage = _job.ActiveWorkStage;
        var supportOnly = _job.ActiveSupportOnly;
        var supportFaceFastPath = _job.ActiveSupportFaceFastPath;
        var sparseSupportFallback = _job.ActiveSparseSupportFallback;
        var supportRequestedColumns = _job.ActiveSupportRequestedColumnCount;
        var supportMaterializationColumns = _job.ActiveSupportMaterializationColumnCount;

        var stageStarted = Stopwatch.GetTimestamp();
        _job.Advance();
        var stageMilliseconds = Stopwatch.GetElapsedTime(
            stageStarted).TotalMilliseconds;

        var paletteMetrics =
            stage == ChunkMaterializationStage.DirectionalFacePalettes
                ? _job.DirectionalFacePaletteMetrics
                : null;
        var stageTiming = new ChunkMaterializationStageTiming(
            coord,
            stage,
            stageMilliseconds,
            paletteMetrics,
            stage == ChunkMaterializationStage.DirectionalFacePalettes
                ? _job.DirectionalFaceBoundaryColumnBoundsMilliseconds
                : 0d,
            stage == ChunkMaterializationStage.DirectionalFacePalettes
                ? _job.DirectionalFaceCaveSamplerBuildMilliseconds
                : 0d,
            stage == ChunkMaterializationStage.DirectionalFacePalettes
                ? _job.DirectionalFaceBoundaryColumnEvaluationMilliseconds
                : 0d,
            supportOnly,
            supportFaceFastPath,
            sparseSupportFallback,
            supportRequestedColumns,
            supportMaterializationColumns);

        var completed = _job.IsComplete;
        var completedChunk = completed
            ? _job.Result ?? throw new InvalidOperationException(
                $"Completed materialization job returned no chunk: {_job.Coord}")
            : null;
        var pendingDependency = _job.PendingDependency is { IsComplete: false } dependency
            ? dependency
            : null;

        return new ChunkMaterializationWorkerSlice(
            coord,
            ImmutableArray.Create(stageTiming),
            stageMilliseconds,
            completed,
            completedChunk,
            InitialLightingMilliseconds: 0d,
            workerStartedTimestamp,
            Stopwatch.GetTimestamp(),
            pendingDependency);
    }

}
