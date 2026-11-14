using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>
/// Detached result of one horizontal-column FULL request. Its section chunks
/// are still private until the owning world coordinator validates the holder
/// generation and publishes the column's sections to live residency.
/// </summary>
public sealed record ChunkColumnMaterializationTaskOutput(
    ChunkColumnCoord Column,
    ImmutableArray<ChunkMaterializationTaskOutput> Sections);

/// <summary>
/// One logical generation task per horizontal ChunkHolder. Independent
/// vertical storage sections fork onto the shared worker pool; the holder task
/// resumes only after every section operation in its current fork has returned.
/// </summary>
public sealed class ChunkColumnMaterializationGenerationTask :
    IChunkGenerationTask,
    IDisposable
{
    private readonly ChunkColumnCoord _column;
    private readonly ChunkMaterializationGenerationTask[] _sectionTasks;
    private readonly ChunkMaterializationTaskOutput?[] _sectionResults;
    private readonly ChunkGenerationStatus[] _sectionProgressStatuses;
    private readonly Dictionary<ulong, PendingSectionOperation> _pendingOperations = [];
    private readonly SortedSet<int> _readySections = [];
    private ulong _nextDependencyId = 1;
    private ChunkGenerationStatus _lastPublishedProgressStatus =
        ChunkGenerationStatus.Unloaded;
    private bool _started;
    private bool _completed;
    private bool _disposed;

    public ChunkColumnMaterializationGenerationTask(
        ChunkColumnCoord column,
        IEnumerable<IChunkMaterializationJob> sectionJobs,
        Action<Chunk> initializeLighting,
        long dispatchTimestamp = 0)
    {
        ArgumentNullException.ThrowIfNull(sectionJobs);
        ArgumentNullException.ThrowIfNull(initializeLighting);

        _column = column;
        var jobs = sectionJobs
            .OrderBy(job => job.Coord.Y)
            .ToArray();
        if (jobs.Length == 0)
            throw new ArgumentException(
                "A column generation task requires at least one section job.",
                nameof(sectionJobs));

        foreach (var job in jobs)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (ChunkColumnCoord.FromChunk(job.Coord) != column)
                throw new ArgumentException(
                    $"Section job {job.Coord} does not belong to column {column}.",
                    nameof(sectionJobs));
        }

        _sectionTasks = jobs
            .Select(job => new ChunkMaterializationGenerationTask(
                job,
                initializeLighting,
                dispatchTimestamp))
            .ToArray();
        _sectionResults = new ChunkMaterializationTaskOutput?[jobs.Length];
        _sectionProgressStatuses = new ChunkGenerationStatus[jobs.Length];
    }

    public ChunkColumnCoord Column => _column;

    public ChunkGenerationTaskAdvanceResult Advance(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            throw new InvalidOperationException(
                "A completed column materialization task cannot advance.");
        ArgumentNullException.ThrowIfNull(completedDependencies);
        cancellationToken.ThrowIfCancellationRequested();

        var nextOperations = new List<ChunkGenerationWorkerOperation>();
        if (!_started)
        {
            if (completedDependencies.Count != 0)
                throw new InvalidOperationException(
                    "The first column generation advance cannot have worker completions.");

            _started = true;
            for (var index = 0; index < _sectionTasks.Length; index++)
                CollectChildAdvance(
                    index,
                    _sectionTasks[index].Advance(
                        Array.Empty<ChunkGenerationDependencyCompletion>(),
                        cancellationToken),
                    nextOperations);
        }
        else if (completedDependencies.Count > 0)
        {
            if (completedDependencies.Count != _pendingOperations.Count)
                throw new InvalidOperationException(
                    $"Column {_column} expected {_pendingOperations.Count} worker completions, " +
                    $"received {completedDependencies.Count}.");

            foreach (var completion in completedDependencies)
            {
                if (completion.Error is not null)
                    throw new InvalidOperationException(
                        $"A section operation failed in column {_column}.",
                        completion.Error);

                if (!_pendingOperations.Remove(
                        completion.DependencyId, out var pending))
                    throw new InvalidOperationException(
                        $"Unknown section dependency {completion.DependencyId} in column {_column}.");

                var childCompletion = new ChunkGenerationDependencyCompletion(
                    completion.Task,
                    WaitToken: 1,
                    pending.ChildDependencyId,
                    completion.Result);
                var childAdvance = _sectionTasks[pending.SectionIndex].Advance(
                    [childCompletion],
                    cancellationToken);
                CollectChildAdvance(
                    pending.SectionIndex,
                    childAdvance,
                    nextOperations);
            }
        }
        else if (_readySections.Count > 0)
        {
            // Feature/mutation steps are advanced one section at a time on the
            // dimension's serial worldgen lane. A Yield never creates a fake
            // worker dependency or a second in-flight section advance.
            var sectionIndex = _readySections.Min;
            _readySections.Remove(sectionIndex);
            CollectChildAdvance(
                sectionIndex,
                _sectionTasks[sectionIndex].Advance(
                    Array.Empty<ChunkGenerationDependencyCompletion>(),
                    cancellationToken),
                nextOperations);
        }
        else if (_pendingOperations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Column {_column} was advanced while worker dependencies were still pending.");
        }

        var progressStatus = AdvanceAggregateProgress();

        if (nextOperations.Count > 0)
            return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                nextOperations,
                progressStatus);

        if (_pendingOperations.Count > 0)
            throw new InvalidOperationException(
                $"Column {_column} has outstanding operations without a wait result.");

        if (_readySections.Count > 0)
            return progressStatus is { } yieldedStatus
                ? ChunkGenerationTaskAdvanceResult.YieldWithStatus(yieldedStatus)
                : ChunkGenerationTaskAdvanceResult.Yield;

        var sections = _sectionResults
            .Select((result, index) => result ??
                throw new InvalidOperationException(
                    $"Column {_column} section task {index} completed without a result."))
            .OrderBy(result => result.Coord.Y)
            .ToImmutableArray();

        _completed = true;
        // The FULL holder future is completed by ChunkResidencyRuntime
        // only after the detached column has been validated and every accepted
        // section has been inserted on the world-owning thread.
        return ChunkGenerationTaskAdvanceResult.CompletedWith(
            new ChunkColumnMaterializationTaskOutput(_column, sections));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var task in _sectionTasks)
            task.Dispose();
        _pendingOperations.Clear();
        _readySections.Clear();
    }

    private ChunkGenerationStatus? AdvanceAggregateProgress()
    {
        if (_sectionProgressStatuses.Length == 0)
            return null;

        var aggregate = _sectionProgressStatuses[0];
        for (var index = 1; index < _sectionProgressStatuses.Length; index++)
        {
            if (_sectionProgressStatuses[index] < aggregate)
                aggregate = _sectionProgressStatuses[index];
        }

        if (aggregate <= _lastPublishedProgressStatus)
            return null;

        _lastPublishedProgressStatus = aggregate;
        return aggregate;
    }

    private void CollectChildAdvance(
        int sectionIndex,
        ChunkGenerationTaskAdvanceResult result,
        List<ChunkGenerationWorkerOperation> nextOperations)
    {
        if (result.ProgressStatus is { } sectionStatus &&
            sectionStatus > _sectionProgressStatuses[sectionIndex])
        {
            _sectionProgressStatuses[sectionIndex] = sectionStatus;
        }

        switch (result.Kind)
        {
            case ChunkGenerationTaskAdvanceKind.Completed:
                if (result.CompletionPayload is not ChunkMaterializationTaskOutput output)
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} in {_column} returned no materialization output.");
                if (output.Coord.X != _column.X || output.Coord.Z != _column.Z)
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} returned a chunk outside {_column}.");
                if (_sectionResults[sectionIndex] is not null)
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} in {_column} completed twice.");
                _sectionResults[sectionIndex] = output;
                break;

            case ChunkGenerationTaskAdvanceKind.WaitForWorkers:
                foreach (var operation in result.WorkerOperations)
                {
                    if (_nextDependencyId == 0 ||
                        _nextDependencyId == ulong.MaxValue)
                        throw new InvalidOperationException(
                            $"Column worker dependency ID space was exhausted: {_column}");

                    var dependencyId = _nextDependencyId++;
                    if (!_pendingOperations.TryAdd(
                            dependencyId,
                            new PendingSectionOperation(
                                sectionIndex,
                                operation.DependencyId)))
                        throw new InvalidOperationException(
                            $"Duplicate column dependency ID {dependencyId}: {_column}");

                    nextOperations.Add(new ChunkGenerationWorkerOperation(
                        dependencyId,
                        operation.Execute,
                        operation.ExecutionPolicy));
                }
                break;

            case ChunkGenerationTaskAdvanceKind.Yield:
                if (!_readySections.Add(sectionIndex))
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} in {_column} was queued to yield twice.");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    private readonly record struct PendingSectionOperation(
        int SectionIndex,
        ulong ChildDependencyId);
}
