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
    private readonly List<ChunkMaterializationStageTiming> _stageTimings = [];
    private readonly Dictionary<ulong, ulong> _activeDependencyOperationIds = [];
    private ulong _nextDependencyId = 1;
    private ulong _lastDependencyId;
    private bool _hasScheduledOperation;
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
        bool returnJobOnCompletion = false)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _initializeLighting = initializeLighting;
        _dispatchTimestamp = dispatchTimestamp == 0
            ? Stopwatch.GetTimestamp()
            : dispatchTimestamp;
        _createdTimestamp = Stopwatch.GetTimestamp();
        _returnJobOnCompletion = returnJobOnCompletion;
        // A nested dependency job remains owned by the parent materialization
        // job until it is returned in a completion payload. On cancellation,
        // the parent's Dispose path is the single owner that releases it.
        _jobOwnershipTransferred = returnJobOnCompletion;
    }

    public ChunkCoord Coord => _job.Coord;

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
            return ResumeDependencyTask(completedDependencies, cancellationToken);

        if (_hasScheduledOperation)
            CollectSlice(completedDependencies);
        else if (completedDependencies.Count != 0)
            throw new InvalidOperationException(
                "The first materialization operation cannot have worker completions.");

        if (_pendingDependencyJob is not null)
            return BeginDependencyTask(cancellationToken);

        if (_job.IsComplete)
        {
            var chunk = _job.Result ??
                throw new InvalidOperationException(
                    $"Completed materialization job returned no chunk: {_job.Coord}");
            return Complete(chunk);
        }

        return ScheduleParentOperation();
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
        var advance = _activeDependencyTask.Advance(
            Array.Empty<ChunkGenerationDependencyCompletion>(),
            cancellationToken);
        return WrapDependencyOperations(advance);
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

        var dependencyAdvance = (_activeDependencyTask ??
            throw new InvalidOperationException(
                "A chunk dependency completion arrived without an active dependency task."))
            .Advance(childCompletions, cancellationToken);

        if (dependencyAdvance.Kind == ChunkGenerationTaskAdvanceKind.WaitForWorkers)
            return WrapDependencyOperations(dependencyAdvance);

        if (dependencyAdvance.Kind != ChunkGenerationTaskAdvanceKind.Completed ||
            dependencyAdvance.CompletionPayload is not ChunkMaterializationTaskOutput dependencyOutput ||
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
        _activeDependencyTask.Dispose();
        _activeDependencyTask = null;
        _pendingDependencyJob = null;

        return ScheduleParentOperation();
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
                operation.Execute));
        }

        _hasScheduledOperation = true;
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
                token => AdvanceUntilWait(token)),
        ]);
    }

    private ulong NextDependencyId()
    {
        if (_nextDependencyId == 0 || _nextDependencyId == ulong.MaxValue)
            throw new InvalidOperationException(
                "Materialization worker dependency ID space was exhausted.");
        return _nextDependencyId++;
    }

    private void CollectSlice(
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
        if (slice.StageTimings.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Materialization worker returned an empty stage-timing batch.");

        if (slice.IsComplete)
        {
            if (slice.CompletedChunk is null || slice.PendingDependency is not null)
                throw new InvalidOperationException(
                    "Completed materialization worker returned an invalid result.");
        }
        else if (slice.CompletedChunk is not null ||
                 slice.PendingDependency is null)
        {
            throw new InvalidOperationException(
                "Incomplete materialization worker did not return a chunk dependency.");
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
        if (!slice.IsComplete)
        {
            _pendingDependencyJob = slice.PendingDependency;
        }
    }

    private ChunkGenerationTaskAdvanceResult Complete(Chunk chunk)
    {
        if (!_isCompleted)
            _isCompleted = true;

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
                completedDependencyJob));
    }

    private ChunkMaterializationWorkerSlice AdvanceUntilWait(
        CancellationToken cancellationToken)
    {
        var workerStartedTimestamp = Stopwatch.GetTimestamp();
        var stageTimings = ImmutableArray.CreateBuilder<ChunkMaterializationStageTiming>();
        var workerMilliseconds = 0d;
        var initialLightingMilliseconds = 0d;
        var coord = _job.ActiveWorkCoord;

        // Synchronous work runs through the entire available pipeline. A
        // dependency is the only reason to return control to the coordinator.
        while (!_job.IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stage = _job.ActiveWorkStage;
            coord = _job.ActiveWorkCoord;
            var supportOnly = _job.ActiveSupportOnly;
            var supportFaceFastPath = _job.ActiveSupportFaceFastPath;
            var sparseSupportFallback = _job.ActiveSparseSupportFallback;
            var supportRequestedColumns = _job.ActiveSupportRequestedColumnCount;
            var supportMaterializationColumns = _job.ActiveSupportMaterializationColumnCount;

            var stageStarted = Stopwatch.GetTimestamp();
            _job.Advance();
            var stageMilliseconds = Stopwatch.GetElapsedTime(
                stageStarted).TotalMilliseconds;
            workerMilliseconds += stageMilliseconds;

            var paletteMetrics =
                stage == ChunkMaterializationStage.DirectionalFacePalettes
                    ? _job.DirectionalFacePaletteMetrics
                    : null;
            stageTimings.Add(new ChunkMaterializationStageTiming(
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
                supportMaterializationColumns));

            if (_job.PendingDependency is { IsComplete: false } dependency)
            {
                return new ChunkMaterializationWorkerSlice(
                    coord,
                    stageTimings.ToImmutable(),
                    workerMilliseconds,
                    IsComplete: false,
                    CompletedChunk: null,
                    InitialLightingMilliseconds: 0d,
                    workerStartedTimestamp,
                    Stopwatch.GetTimestamp(),
                    dependency);
            }
        }

        var completedChunk = _job.Result ??
            throw new InvalidOperationException(
                $"Completed materialization job returned no chunk: {_job.Coord}");
        if (_initializeLighting is not null)
        {
            var lightingStarted = Stopwatch.GetTimestamp();
            _initializeLighting(completedChunk);
            initialLightingMilliseconds = Stopwatch.GetElapsedTime(
                lightingStarted).TotalMilliseconds;
            workerMilliseconds += initialLightingMilliseconds;
        }

        var completedTimestamp = Stopwatch.GetTimestamp();
        return new ChunkMaterializationWorkerSlice(
            coord,
            stageTimings.ToImmutable(),
            workerMilliseconds,
            IsComplete: true,
            completedChunk,
            initialLightingMilliseconds,
            workerStartedTimestamp,
            completedTimestamp);
    }
}
