using System.Collections.Immutable;
using System.Threading.Tasks;

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
    private readonly List<ChunkMaterializationGenerationTask> _sectionTasks = [];
    private readonly List<ChunkMaterializationTaskOutput?> _sectionResults = [];
    private readonly List<ChunkGenerationStatus> _sectionProgressStatuses = [];
    private readonly int _requiredSectionCount;
    private readonly List<ChunkSectionGenerationProgress> _pendingSectionProgress = [];
    private readonly Dictionary<ChunkCoord, int> _sectionTaskIndices = [];
    private readonly Dictionary<ChunkCoord, SectionGenerationFuture> _sectionGenerationFutures = [];
    private readonly Dictionary<ulong, PendingSectionOperation> _pendingOperations = [];
    private readonly SortedSet<int> _readySections = [];
    private ulong _nextDependencyId = 1;
    private ChunkGenerationStatus _lastPublishedProgressStatus =
        ChunkGenerationStatus.Unloaded;
    private int _nextOrderedSectionIndex;
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

        _requiredSectionCount = jobs.Length;
        for (var index = 0; index < jobs.Length; index++)
        {
            var coord = jobs[index].Coord;
            if (!_sectionTaskIndices.TryAdd(coord, index))
                throw new ArgumentException(
                    $"Column {column} contains more than one generation job for section {coord}.",
                    nameof(sectionJobs));

            var sectionTask = new ChunkMaterializationGenerationTask(
                jobs[index],
                initializeLighting,
                dispatchTimestamp,
                deferDependenciesToColumn: true);
            _sectionGenerationFutures.Add(
                coord,
                new SectionGenerationFuture());
            _sectionTasks.Add(sectionTask);
            _sectionResults.Add(null);
            _sectionProgressStatuses.Add(sectionTask.InitialProgressStatus);
            if (sectionTask.InitialProgressStatus != ChunkGenerationStatus.Unloaded)
            {
                _pendingSectionProgress.Add(new ChunkSectionGenerationProgress(
                    coord, sectionTask.InitialProgressStatus));
            }
        }
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
        var advanceReadySections = false;
        if (!_started)
        {
            if (completedDependencies.Count != 0)
                throw new InvalidOperationException(
                    "The first column generation advance cannot have worker completions.");

            _started = true;
            for (var index = 0; index < _requiredSectionCount; index++)
                _readySections.Add(index);
            advanceReadySections = true;
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
                    nextOperations,
                    cancellationToken);
            }
        }
        else
        {
            advanceReadySections = true;
        }

        if (advanceReadySections && _readySections.Count > 0)
        {
            // Start every ready independent CPU fork in this continuation,
            // preserving vertical-section parallelism. Mutating/coordinator
            // steps remain serialized: at most one such section advances per
            // continuation, then the parent yields for fairness.
            var readyBatch = _readySections
                .OrderBy(index => index >= _nextOrderedSectionIndex
                    ? index - _nextOrderedSectionIndex
                    : _sectionTasks.Count - _nextOrderedSectionIndex + index)
                .ToArray();
            _readySections.Clear();
            var orderedStageAdvanced = false;

            foreach (var sectionIndex in readyBatch)
            {
                var sectionTask = _sectionTasks[sectionIndex];
                if (WaitForPlanHolderRequirements(sectionIndex))
                    continue;

                var policy = sectionTask.NextExecutionPolicy;
                var isFork = policy is
                    ChunkGenerationExecutionPolicy.SharedWorkerPool or
                    ChunkGenerationExecutionPolicy.LightExecutor;

                if (!isFork && orderedStageAdvanced)
                {
                    _readySections.Add(sectionIndex);
                    continue;
                }

                CollectChildAdvance(
                    sectionIndex,
                    sectionTask.Advance(
                        Array.Empty<ChunkGenerationDependencyCompletion>(),
                        cancellationToken),
                    nextOperations,
                    cancellationToken);

                if (!isFork)
                {
                    orderedStageAdvanced = true;
                    _nextOrderedSectionIndex = sectionIndex + 1;
                    if (_nextOrderedSectionIndex >= _sectionTasks.Count)
                        _nextOrderedSectionIndex = 0;
                }
            }
        }
        else if (advanceReadySections && _pendingOperations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Column {_column} was advanced while worker dependencies were still pending.");
        }

        var progressStatus = AdvanceAggregateProgress();

        if (nextOperations.Count > 0)
            return AttachSectionProgress(
                ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                    nextOperations,
                    progressStatus));

        if (_pendingOperations.Count > 0)
            throw new InvalidOperationException(
                $"Column {_column} has outstanding operations without a wait result.");

        if (_readySections.Count > 0)
            return AttachSectionProgress(
                progressStatus is { } yieldedStatus
                    ? ChunkGenerationTaskAdvanceResult.YieldWithStatus(yieldedStatus)
                    : ChunkGenerationTaskAdvanceResult.Yield);

        var sections = _sectionResults
            .Take(_requiredSectionCount)
            .Select((result, index) => result ??
                throw new InvalidOperationException(
                    $"Column {_column} section task {index} completed without a result."))
            .OrderBy(result => result.Coord.Y)
            .ToImmutableArray();

        _completed = true;
        // The FULL holder future is completed by ChunkResidencyRuntime
        // only after the detached column has been validated and every accepted
        // section has been inserted on the world-owning thread.
        return AttachSectionProgress(
            ChunkGenerationTaskAdvanceResult.CompletedWith(
                new ChunkColumnMaterializationTaskOutput(_column, sections)));
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
        foreach (var future in _sectionGenerationFutures.Values)
        {
            future.WaitingSections.Clear();
            future.StatusWaiters.Clear();
        }
        _pendingSectionProgress.Clear();
    }

    private ChunkGenerationStatus? AdvanceAggregateProgress()
    {
        if (_requiredSectionCount == 0)
            return null;

        var aggregate = _sectionProgressStatuses[0];
        for (var index = 1; index < _requiredSectionCount; index++)
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
        List<ChunkGenerationWorkerOperation> nextOperations,
        CancellationToken cancellationToken)
    {
        if (result.ProgressStatus is { } sectionStatus &&
            sectionStatus > _sectionProgressStatuses[sectionIndex])
        {
            _sectionProgressStatuses[sectionIndex] = sectionStatus;
            CompleteSectionStatusFuture(
                _sectionTasks[sectionIndex].Coord);
            if (sectionIndex < _requiredSectionCount &&
                result.Kind != ChunkGenerationTaskAdvanceKind.Completed)
            {
                _pendingSectionProgress.Add(new ChunkSectionGenerationProgress(
                    _sectionTasks[sectionIndex].Coord,
                    sectionStatus));
            }
        }

        switch (result.Kind)
        {
            case ChunkGenerationTaskAdvanceKind.Completed:
                if (result.CompletionPayload is not ChunkMaterializationTaskOutput output)
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} in {_column} returned no materialization output.");
                if (output.Coord != _sectionTasks[sectionIndex].Coord)
                    throw new InvalidOperationException(
                        $"Section task {sectionIndex} returned {output.Coord}, expected {_sectionTasks[sectionIndex].Coord} in {_column}.");
                if (sectionIndex < _requiredSectionCount)
                {
                    if (_sectionResults[sectionIndex] is not null)
                        throw new InvalidOperationException(
                            $"Section task {sectionIndex} in {_column} completed twice.");
                    _sectionResults[sectionIndex] = output;
                }
                if (result.ProgressStatus is { } completedSectionStatus &&
                    sectionIndex < _requiredSectionCount)
                {
                    _pendingSectionProgress.Add(
                        new ChunkSectionGenerationProgress(
                            output.Coord,
                            completedSectionStatus,
                            output.Chunk));
                }
                CompleteSectionFuture(output);
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
            {
                var sectionTask = _sectionTasks[sectionIndex];
                if (sectionTask.PendingDependencyCoord is { } dependencyCoord)
                {
                    if (dependencyCoord.X != sectionTask.Coord.X ||
                        dependencyCoord.Z != sectionTask.Coord.Z ||
                        dependencyCoord.Y >= sectionTask.Coord.Y)
                    {
                        throw new InvalidOperationException(
                            $"Section {sectionTask.Coord} declared an invalid lower-section dependency {dependencyCoord}.");
                    }

                    if (_sectionGenerationFutures.TryGetValue(
                            dependencyCoord, out var dependencyFuture))
                    {
                        if (dependencyFuture.Completion.Task.IsCompletedSuccessfully)
                        {
                            sectionTask.ResolvePendingDependency(
                                dependencyCoord,
                                dependencyFuture.Completion.Task.Result);
                            AddReadySection(sectionIndex);
                        }
                        else
                        {
                            if (dependencyFuture.Completion.Task.IsCompleted)
                                throw new InvalidOperationException(
                                    $"Section dependency {dependencyCoord} failed before {sectionTask.Coord} could resume.",
                                    dependencyFuture.Completion.Task.Exception);
                            if (dependencyFuture.WaitingSections.Contains(sectionIndex))
                                throw new InvalidOperationException(
                                    $"Section {sectionTask.Coord} registered dependency {dependencyCoord} more than once.");
                            dependencyFuture.WaitingSections.Add(sectionIndex);
                        }

                        break;
                    }

                    // The dependency was discovered dynamically during this
                    // section's gravity pass. Admit its existing support-only
                    // job as another section task in the column scheduler. This
                    // flattens recursive support chains into the same fair queue.
                    var dependencyJob = sectionTask.PendingDependencyJob ??
                        throw new InvalidOperationException(
                            $"Section {sectionTask.Coord} exposed dependency {dependencyCoord} without a job.");
                    AddDependencySectionTask(sectionIndex, dependencyJob);
                    break;
                }

                AddReadySection(sectionIndex);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    private ChunkGenerationTaskAdvanceResult AttachSectionProgress(
        ChunkGenerationTaskAdvanceResult result)
    {
        if (_pendingSectionProgress.Count == 0)
            return result;

        var updates = _pendingSectionProgress.ToArray();
        _pendingSectionProgress.Clear();
        return result.WithSectionProgress(updates);
    }

    private bool WaitForPlanHolderRequirements(int sectionIndex)
    {
        var sectionTask = _sectionTasks[sectionIndex];
        if (sectionTask.NextGenerationStep is not { } targetStep)
            return false;

        var waiting = false;
        foreach (var requirement in AsteriaChunkGenerationPlan
                     .GetHolderStatusRequirements(targetStep, sectionTask.Coord))
        {
            // Same-section requirements are completed by this section task's
            // own ordered plan. Only an exact neighbouring section needs a
            // scheduler-level future; this prevents self-wait cycles.
            if (requirement.Coord == sectionTask.Coord ||
                !_sectionGenerationFutures.TryGetValue(
                    requirement.Coord, out var future) ||
                !_sectionTaskIndices.TryGetValue(
                    requirement.Coord, out var dependencyIndex) ||
                _sectionProgressStatuses[dependencyIndex] >= requirement.Status)
                continue;

            if (future.StatusWaiters.TryGetValue(
                    sectionIndex, out var registeredStatus))
            {
                if (registeredStatus < requirement.Status)
                    future.StatusWaiters[sectionIndex] = requirement.Status;
            }
            else
            {
                future.StatusWaiters.Add(sectionIndex, requirement.Status);
            }
            waiting = true;
        }

        return waiting;
    }

    private void CompleteSectionStatusFuture(ChunkCoord section)
    {
        if (!_sectionGenerationFutures.TryGetValue(section, out var future) ||
            !_sectionTaskIndices.TryGetValue(section, out var sectionIndex))
            return;

        var completedStatus = _sectionProgressStatuses[sectionIndex];
        foreach (var (waitingSection, requiredStatus) in
                 future.StatusWaiters.ToArray())
        {
            if (requiredStatus > completedStatus)
                continue;
            future.StatusWaiters.Remove(waitingSection);
            AddReadySection(waitingSection);
        }
    }

    private void CompleteSectionFuture(ChunkMaterializationTaskOutput output)
    {
        if (!_sectionGenerationFutures.TryGetValue(
                output.Coord, out var future) ||
            !future.Completion.TrySetResult(output.Chunk))
        {
            throw new InvalidOperationException(
                $"Section {output.Coord} completed its generation future more than once or without registration.");
        }

        foreach (var waitingSection in future.WaitingSections)
        {
            _sectionTasks[waitingSection].ResolvePendingDependency(
                output.Coord, future.Completion.Task.Result);
            AddReadySection(waitingSection);
        }

        future.WaitingSections.Clear();
    }

    private void AddReadySection(int sectionIndex)
    {
        if (!_readySections.Add(sectionIndex))
            throw new InvalidOperationException(
                $"Section task {sectionIndex} in {_column} was queued to resume twice.");
    }

    private void AddDependencySectionTask(
        int waitingSection,
        IChunkMaterializationJob dependencyJob)
    {
        ArgumentNullException.ThrowIfNull(dependencyJob);
        var dependencyCoord = dependencyJob.Coord;
        if (ChunkColumnCoord.FromChunk(dependencyCoord) != _column)
            throw new InvalidOperationException(
                $"Dynamic section dependency {dependencyCoord} does not belong to column {_column}.");
        if (_sectionTaskIndices.ContainsKey(dependencyCoord))
            throw new InvalidOperationException(
                $"Section {dependencyCoord} already has a task but its future was not registered.");

        var dependencyIndex = _sectionTasks.Count;
        _sectionTaskIndices.Add(dependencyCoord, dependencyIndex);
        var dependencyTask = new ChunkMaterializationGenerationTask(
            dependencyJob,
            initializeLighting: null,
            dispatchTimestamp: 0,
            returnJobOnCompletion: true,
            deferDependenciesToColumn: true);
        _sectionGenerationFutures.Add(
            dependencyCoord,
            new SectionGenerationFuture());
        _sectionTasks.Add(dependencyTask);
        _sectionResults.Add(null);
        _sectionProgressStatuses.Add(dependencyTask.InitialProgressStatus);
        _sectionGenerationFutures[dependencyCoord].WaitingSections.Add(
            waitingSection);
        AddReadySection(dependencyIndex);
    }

    private sealed class SectionGenerationFuture
    {
        public TaskCompletionSource<Chunk> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Dictionary<int, ChunkGenerationStatus> StatusWaiters { get; } = [];

        public List<int> WaitingSections { get; } = [];
    }

    private readonly record struct PendingSectionOperation(
        int SectionIndex,
        ulong ChildDependencyId);
}
