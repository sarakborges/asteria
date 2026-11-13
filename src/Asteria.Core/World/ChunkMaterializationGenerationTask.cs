using System.Collections.Immutable;
using System.Diagnostics;

namespace Asteria.Core.World;

/// <summary>
/// Detached output from one completed materialization task. The chunk is not
/// inserted into VoxelWorld here; the owning world loop must validate its
/// streaming ticket and then publish it atomically.
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
    long WorkerCompletedTimestamp);

/// <summary>Result of advancing one worker-owned materialization slice.</summary>
internal sealed record ChunkMaterializationWorkerSlice(
    ChunkCoord Coord,
    ImmutableArray<ChunkMaterializationStageTiming> StageTimings,
    double WorkerMilliseconds,
    bool IsComplete,
    Chunk? CompletedChunk,
    double InitialLightingMilliseconds,
    long WorkerStartedTimestamp,
    long WorkerCompletedTimestamp);

/// <summary>
/// Adapts the existing stateful materializer job to the cooperative generation
/// scheduler. Each worker operation executes a bounded batch of Advance calls;
/// the job remains private and is never touched by the coordinator while a
/// worker owns the slice. A completed chunk is returned as a detached outcome.
/// </summary>
public sealed class ChunkMaterializationGenerationTask :
    IChunkGenerationTask,
    IDisposable
{
    internal const int MaximumAdvancesPerWorkerOperation = 32;
    internal const double MaximumWorkerSliceMilliseconds = 6d;
    private readonly IChunkMaterializationJob _job;
    private readonly Action<Chunk> _initializeLighting;
    private readonly long _dispatchTimestamp;
    private readonly long _createdTimestamp;
    private readonly List<ChunkMaterializationStageTiming> _stageTimings = [];
    private ulong _nextDependencyId = 1;
    private ulong _lastDependencyId;
    private bool _hasScheduledSlice;
    private bool _hasStartedWorker;
    private bool _isCompleted;
    private bool _isDisposed;
    private long _firstWorkerStartedTimestamp;
    private long _workerCompletedTimestamp;
    private double _workerMilliseconds;
    private double _initialLightingMilliseconds;

    public ChunkMaterializationGenerationTask(
        IChunkMaterializationJob job,
        Action<Chunk> initializeLighting,
        long dispatchTimestamp = 0)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _initializeLighting = initializeLighting ??
            throw new ArgumentNullException(nameof(initializeLighting));
        _dispatchTimestamp = dispatchTimestamp == 0
            ? Stopwatch.GetTimestamp()
            : dispatchTimestamp;
        _createdTimestamp = Stopwatch.GetTimestamp();
    }

    public ChunkCoord Coord => _job.Coord;

    public ChunkGenerationTaskAdvanceResult Advance(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isCompleted)
            throw new InvalidOperationException(
                "A completed materialization generation task cannot advance.");
        ArgumentNullException.ThrowIfNull(completedDependencies);
        cancellationToken.ThrowIfCancellationRequested();

        if (_hasScheduledSlice)
            CollectSlice(completedDependencies);
        else if (completedDependencies.Count != 0)
            throw new InvalidOperationException(
                "The first materialization task slice cannot have worker completions.");

        if (_isCompleted)
            throw new InvalidOperationException(
                "The materialization task completed without returning its output.");

        if (_job.IsComplete)
        {
            var chunk = _job.Result ??
                throw new InvalidOperationException(
                    $"Completed materialization job returned no chunk: {_job.Coord}");
            return Complete(chunk);
        }

        if (_nextDependencyId == 0 || _nextDependencyId == ulong.MaxValue)
            throw new InvalidOperationException(
                "Materialization worker dependency ID space was exhausted.");

        var dependencyId = _nextDependencyId++;
        _lastDependencyId = dependencyId;
        _hasScheduledSlice = true;

        return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
        [
            new ChunkGenerationWorkerOperation(
                dependencyId,
                token => AdvanceOneSlice(token)),
        ]);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _job.Dispose();
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
                "Materialization worker returned a mismatched or invalid slice result.");
        }
        // A composite materialization job may advance a nested gravity-support
        // child and then return to its parent during this same slice. Preserve
        // the leaf coordinate captured before Advance rather than comparing it
        // against the now-advanced job cursor.
        if (slice.StageTimings.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Materialization worker returned an empty stage-timing batch.");
        _stageTimings.AddRange(slice.StageTimings);
        _workerMilliseconds += slice.WorkerMilliseconds;
        if (!_hasStartedWorker)
        {
            _hasStartedWorker = true;
            _firstWorkerStartedTimestamp = slice.WorkerStartedTimestamp;
        }
        _workerCompletedTimestamp = slice.WorkerCompletedTimestamp;
        _initialLightingMilliseconds += slice.InitialLightingMilliseconds;

        if (slice.IsComplete)
        {
            if (slice.CompletedChunk is null)
                throw new InvalidOperationException(
                    "Completed materialization slice has no chunk.");
        }
        else if (slice.CompletedChunk is not null)
        {
            throw new InvalidOperationException(
                "Incomplete materialization slice unexpectedly returned a chunk.");
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
                _workerCompletedTimestamp));
    }

    private ChunkMaterializationWorkerSlice AdvanceOneSlice(
        CancellationToken cancellationToken)
    {
        var workerStartedTimestamp = Stopwatch.GetTimestamp();
        var stageTimings = ImmutableArray.CreateBuilder<ChunkMaterializationStageTiming>();
        var workerMilliseconds = 0d;
        var initialLightingMilliseconds = 0d;
        Chunk? completedChunk = null;
        var coord = _job.ActiveWorkCoord;
        var advances = 0;

        do
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
            advances++;

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

            if (_job.IsComplete)
            {
                completedChunk = _job.Result ??
                    throw new InvalidOperationException(
                        $"Completed materialization job returned no chunk: {_job.Coord}");
                var lightingStarted = Stopwatch.GetTimestamp();
                _initializeLighting(completedChunk);
                initialLightingMilliseconds = Stopwatch.GetElapsedTime(
                    lightingStarted).TotalMilliseconds;
                workerMilliseconds += initialLightingMilliseconds;
                break;
            }
        }
        while (advances < MaximumAdvancesPerWorkerOperation &&
               Stopwatch.GetElapsedTime(
                   workerStartedTimestamp).TotalMilliseconds <
               MaximumWorkerSliceMilliseconds);

        return new ChunkMaterializationWorkerSlice(
            coord,
            stageTimings.ToImmutable(),
            workerMilliseconds,
            _job.IsComplete,
            completedChunk,
            initialLightingMilliseconds,
            workerStartedTimestamp,
            Stopwatch.GetTimestamp());
    }
}
