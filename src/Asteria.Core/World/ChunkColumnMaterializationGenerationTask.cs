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
    private readonly Dictionary<ulong, PendingHolderStatusOperation>
        _pendingHolderStatusOperations = [];
    private readonly Dictionary<HolderStatusRequirementKey, ulong>
        _pendingHolderStatusRequirementIds = [];
    private readonly HashSet<HolderStatusRequirementKey>
        _completedHolderStatusRequirements = [];
    private readonly HashSet<ulong> _activeWaitDependencyIds = [];
    private readonly List<ChunkGenerationWorkerOperation>
        _deferredWorkerOperations = [];
    private readonly Func<ChunkGenerationStepId, ChunkCoord,
        IReadOnlyList<ChunkGenerationSectionRequirement>> _getHolderStatusRequirements;
    private readonly Func<ChunkGenerationSectionRequirement,
        Task<ChunkGenerationFutureResult>>? _getHolderStatusFuture;
    private readonly Action<ChunkGenerationSectionRequirement>? _requestHolderProducer;
    private readonly object _targetStatusGate = new();
    private readonly SortedSet<int> _readySections = [];
    private ChunkGenerationStatus _targetStatus;
    private TargetUpgradeWait? _targetUpgradeWait;
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
        long dispatchTimestamp = 0,
        Func<ChunkGenerationStepId, ChunkCoord,
            IReadOnlyList<ChunkGenerationSectionRequirement>>? getHolderStatusRequirements = null,
        Func<ChunkGenerationSectionRequirement,
            Task<ChunkGenerationFutureResult>>? getHolderStatusFuture = null,
        Action<ChunkGenerationSectionRequirement>? requestHolderProducer = null,
        ChunkGenerationStatus targetStatus = ChunkGenerationStatus.Full)
    {
        ArgumentNullException.ThrowIfNull(sectionJobs);
        ArgumentNullException.ThrowIfNull(initializeLighting);
        if ((getHolderStatusFuture is null) != (requestHolderProducer is null))
            throw new ArgumentException(
                "Cross-column holder status futures and producer admission must be configured together.");

        _column = column;
        _targetStatus = NormalizeTargetStatus(targetStatus);
        _getHolderStatusRequirements = getHolderStatusRequirements ??
            ((targetStep, coord) =>
                AsteriaChunkLoadingPlan.GetHolderStatusRequirements(
                    targetStep, coord));
        _getHolderStatusFuture = getHolderStatusFuture;
        _requestHolderProducer = requestHolderProducer;
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

    /// <summary>
    /// Highest generation stage this column task is currently asked to
    /// produce. InitializeLight is promoted to Light because its light-executor
    /// operation is part of the same resumable materialization job.
    /// </summary>
    public ChunkGenerationStatus TargetStatus
    {
        get
        {
            lock (_targetStatusGate)
                return _targetStatus;
        }
    }

    /// <summary>
    /// Raises the target without replacing the in-flight task. A task parked at
    /// an achieved status is resumed through its own non-blocking status future.
    /// Lowering demand is a cancellation/invalidation operation, not a target
    /// downgrade, because achieved immutable progress cannot be rolled back.
    /// </summary>
    public bool TryUpgradeTargetStatus(ChunkGenerationStatus requestedStatus)
    {
        var target = NormalizeTargetStatus(requestedStatus);
        lock (_targetStatusGate)
        {
            if (target < _targetStatus)
                throw new ArgumentOutOfRangeException(
                    nameof(requestedStatus),
                    "A column generation target can only be upgraded.");
            if (target == _targetStatus)
                return false;

            _targetStatus = target;
            _targetUpgradeWait?.Completion.TrySetResult(
                ChunkGenerationFutureResult.Success(target));
            return true;
        }
    }

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
        var nextFutureOperations = new List<ChunkGenerationFutureOperation>();
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
            if (completedDependencies.Count != _activeWaitDependencyIds.Count)
                throw new InvalidOperationException(
                    $"Column {_column} expected {_activeWaitDependencyIds.Count} active dependency completions, " +
                    $"received {completedDependencies.Count}.");

            var sectionsReadyAfterHolderWait = new HashSet<int>();
            foreach (var completion in completedDependencies)
            {
                if (!_activeWaitDependencyIds.Remove(completion.DependencyId))
                    throw new InvalidOperationException(
                        $"Unknown or stale column dependency {completion.DependencyId} in {_column}.");
                if (IsTargetUpgradeCompletion(completion.DependencyId))
                {
                    if (completion.Error is not null ||
                        completion.Result is not ChunkGenerationFutureResult targetResult ||
                        !targetResult.Succeeded)
                        throw new InvalidOperationException(
                            $"Column {_column} received an invalid target-upgrade signal.",
                            completion.Error);
                    CompleteTargetUpgradeWait(completion.DependencyId);
                    continue;
                }
                if (completion.Error is not null)
                    throw new InvalidOperationException(
                        $"A generation dependency failed in column {_column}.",
                        completion.Error);

                if (_pendingOperations.Remove(
                        completion.DependencyId, out var pendingWorker))
                {
                    var childCompletion = new ChunkGenerationDependencyCompletion(
                        completion.Task,
                        WaitToken: 1,
                        pendingWorker.ChildDependencyId,
                        completion.Result);
                    var childAdvance = _sectionTasks[pendingWorker.SectionIndex].Advance(
                        [childCompletion],
                        cancellationToken);
                    CollectChildAdvance(
                        pendingWorker.SectionIndex,
                        childAdvance,
                        nextOperations,
                        cancellationToken);
                    continue;
                }

                if (!_pendingHolderStatusOperations.Remove(
                        completion.DependencyId, out var pendingHolderStatus))
                    throw new InvalidOperationException(
                        $"Column {_column} has no owner for dependency {completion.DependencyId}.");

                _pendingHolderStatusRequirementIds.Remove(pendingHolderStatus.Key);
                if (completion.Result is not ChunkGenerationFutureResult holderResult)
                    throw new InvalidOperationException(
                        $"Holder status dependency {completion.DependencyId} returned an invalid result.");
                ValidateHolderStatusResult(
                    pendingHolderStatus.Key,
                    holderResult);
                _completedHolderStatusRequirements.Add(pendingHolderStatus.Key);
                foreach (var waitingSection in pendingHolderStatus.WaitingSections)
                    sectionsReadyAfterHolderWait.Add(waitingSection);
            }

            if (_activeWaitDependencyIds.Count != 0)
                throw new InvalidOperationException(
                    $"Column {_column} resumed before every dependency completed.");

            foreach (var sectionIndex in sectionsReadyAfterHolderWait)
                AddReadySectionIfMissing(sectionIndex);
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
                if (HasSectionReachedTarget(sectionIndex))
                {
                    _readySections.Add(sectionIndex);
                    continue;
                }
                if (WaitForPlanHolderRequirements(sectionIndex, nextFutureOperations))
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
        if (TryCreateTargetUpgradeWait(out var targetUpgradeOperation))
            nextFutureOperations.Add(targetUpgradeOperation);

        if (nextFutureOperations.Count > 0)
        {
            // Future waits take precedence over CPU forks from other ready
            // sections. Preserve those forks and dispatch them immediately
            // after the external holder prerequisites resume this column.
            _deferredWorkerOperations.AddRange(nextOperations);
            RegisterActiveWaitDependencies(
                nextFutureOperations.Select(operation => operation.DependencyId));
            return AttachSectionProgress(
                ChunkGenerationTaskAdvanceResult.WaitForFutures(
                    nextFutureOperations,
                    progressStatus));
        }

        if (_deferredWorkerOperations.Count > 0)
        {
            nextOperations.InsertRange(0, _deferredWorkerOperations);
            _deferredWorkerOperations.Clear();
        }

        if (nextOperations.Count > 0)
        {
            RegisterActiveWaitDependencies(
                nextOperations.Select(operation => operation.DependencyId));
            return AttachSectionProgress(
                ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                    nextOperations,
                    progressStatus));
        }

        if (_pendingOperations.Count > 0 ||
            _pendingHolderStatusOperations.Count > 0 ||
            _deferredWorkerOperations.Count > 0)
            throw new InvalidOperationException(
                $"Column {_column} has outstanding dependencies without a wait result.");

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
        _pendingHolderStatusOperations.Clear();
        _pendingHolderStatusRequirementIds.Clear();
        _completedHolderStatusRequirements.Clear();
        _activeWaitDependencyIds.Clear();
        _deferredWorkerOperations.Clear();
        _readySections.Clear();
        lock (_targetStatusGate)
        {
            _targetUpgradeWait?.Completion.TrySetResult(
                ChunkGenerationFutureResult.Unloaded(
                    _targetStatus,
                    "Column generation task was disposed."));
            _targetUpgradeWait = null;
        }
        foreach (var future in _sectionGenerationFutures.Values)
        {
            future.WaitingSections.Clear();
            future.StatusWaiters.Clear();
        }
        _pendingSectionProgress.Clear();
    }

    private ChunkGenerationStatus? AdvanceAggregateProgress()
    {
        var aggregate = GetAggregateProgressStatus();
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
                    var dependencyId = NextDependencyId();
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

    private bool WaitForPlanHolderRequirements(
        int sectionIndex,
        List<ChunkGenerationFutureOperation> nextFutureOperations)
    {
        var sectionTask = _sectionTasks[sectionIndex];
        if (sectionTask.NextGenerationStep is not { } targetStep)
            return false;

        var waiting = false;
        foreach (var requirement in _getHolderStatusRequirements(
                     targetStep, sectionTask.Coord))
        {
            // Same-section requirements are completed by this section task's
            // own ordered plan.
            if (requirement.Coord == sectionTask.Coord)
                continue;

            if (requirement.Coord.X != sectionTask.Coord.X ||
                requirement.Coord.Z != sectionTask.Coord.Z)
            {
                var key = new HolderStatusRequirementKey(
                    requirement.Coord,
                    requirement.Status);
                if (_completedHolderStatusRequirements.Contains(key))
                    continue;
                if (_pendingHolderStatusRequirementIds.TryGetValue(
                        key, out var existingDependencyId))
                {
                    _pendingHolderStatusOperations[existingDependencyId]
                        .WaitingSections.Add(sectionIndex);
                    waiting = true;
                    continue;
                }

                var getFuture = _getHolderStatusFuture ??
                    throw new InvalidOperationException(
                        $"Generation step {targetStep} for {sectionTask.Coord} requires " +
                        $"holder status {requirement.Status} from {requirement.Coord}, " +
                        "but no cross-column future resolver is configured.");
                var requestProducer = _requestHolderProducer ??
                    throw new InvalidOperationException(
                        $"Generation step {targetStep} for {sectionTask.Coord} requires " +
                        $"holder status {requirement.Status} from {requirement.Coord}, " +
                        "but no cross-column producer admission is configured.");
                var holderFuture = getFuture(requirement) ??
                    throw new InvalidOperationException(
                        $"No holder status future was returned for {requirement.Coord}.");

                if (holderFuture.IsCompletedSuccessfully)
                {
                    ValidateHolderStatusResult(key, holderFuture.Result);
                    _completedHolderStatusRequirements.Add(key);
                    continue;
                }

                var dependencyId = NextDependencyId();
                var operation = new ChunkGenerationFutureOperation(
                    dependencyId,
                    holderFuture,
                    () => requestProducer(requirement));
                if (!_pendingHolderStatusOperations.TryAdd(
                        dependencyId,
                        new PendingHolderStatusOperation(key, sectionIndex)))
                    throw new InvalidOperationException(
                        $"Duplicate cross-column holder dependency ID {dependencyId}: {_column}");
                if (!_pendingHolderStatusRequirementIds.TryAdd(key, dependencyId))
                    throw new InvalidOperationException(
                        $"Cross-column holder dependency {requirement.Coord} was registered twice for {sectionTask.Coord}.");
                nextFutureOperations.Add(operation);
                waiting = true;
                continue;
            }

            // A lower section outside the initial vertical batch is created as
            // a dynamic gravity-support task when the materialization job
            // exposes its pending dependency. Its progress future is registered
            // before the next dependent continuation.
            if (!_sectionGenerationFutures.TryGetValue(
                    requirement.Coord, out var future))
                continue;
            if (!_sectionTaskIndices.TryGetValue(
                    requirement.Coord, out var dependencyIndex))
                throw new InvalidOperationException(
                    $"Section future {requirement.Coord} has no owning task in {_column}.");
            if (_sectionProgressStatuses[dependencyIndex] >= requirement.Status)
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

    private bool HasSectionReachedTarget(int sectionIndex)
    {
        lock (_targetStatusGate)
            return _targetStatus < ChunkGenerationStatus.Full &&
                _sectionProgressStatuses[sectionIndex] >= _targetStatus;
    }

    private bool TryCreateTargetUpgradeWait(
        out ChunkGenerationFutureOperation operation)
    {
        lock (_targetStatusGate)
        {
            var aggregateStatus = GetAggregateProgressStatus();
            if (_targetStatus >= ChunkGenerationStatus.Full ||
                aggregateStatus < _targetStatus)
            {
                operation = default;
                return false;
            }

            if (_targetUpgradeWait is null)
            {
                var dependencyId = NextDependencyId();
                var completion = new TaskCompletionSource<ChunkGenerationFutureResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _targetUpgradeWait = new TargetUpgradeWait(
                    dependencyId, completion);
            }

            operation = new ChunkGenerationFutureOperation(
                _targetUpgradeWait.DependencyId,
                _targetUpgradeWait.Completion.Task);
            return true;
        }
    }

    private bool IsTargetUpgradeCompletion(ulong dependencyId)
    {
        lock (_targetStatusGate)
            return _targetUpgradeWait?.DependencyId == dependencyId;
    }

    private void CompleteTargetUpgradeWait(ulong dependencyId)
    {
        lock (_targetStatusGate)
        {
            if (_targetUpgradeWait?.DependencyId != dependencyId)
                throw new InvalidOperationException(
                    $"Column {_column} resumed from an obsolete target-upgrade signal.");
            _targetUpgradeWait = null;
        }
    }

    private ChunkGenerationStatus GetAggregateProgressStatus()
    {
        var aggregate = _sectionProgressStatuses[0];
        for (var index = 1; index < _requiredSectionCount; index++)
        {
            if (_sectionProgressStatuses[index] < aggregate)
                aggregate = _sectionProgressStatuses[index];
        }
        return aggregate;
    }

    private static ChunkGenerationStatus NormalizeTargetStatus(
        ChunkGenerationStatus status)
    {
        if (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(status));

        return status switch
        {
            ChunkGenerationStatus.StructureStarts or
            ChunkGenerationStatus.StructureReferences =>
                ChunkGenerationStatus.Biomes,
            ChunkGenerationStatus.InitializeLight =>
                ChunkGenerationStatus.Light,
            ChunkGenerationStatus.Spawn =>
                ChunkGenerationStatus.Full,
            _ => status,
        };
    }

    private sealed record TargetUpgradeWait(
        ulong DependencyId,
        TaskCompletionSource<ChunkGenerationFutureResult> Completion);

    private void RegisterActiveWaitDependencies(IEnumerable<ulong> dependencyIds)
    {
        _activeWaitDependencyIds.Clear();
        foreach (var dependencyId in dependencyIds)
        {
            if (!_activeWaitDependencyIds.Add(dependencyId))
                throw new InvalidOperationException(
                    $"Column {_column} registered dependency {dependencyId} twice in one wait.");
        }
    }

    private ulong NextDependencyId()
    {
        if (_nextDependencyId == 0 || _nextDependencyId == ulong.MaxValue)
            throw new InvalidOperationException(
                $"Column dependency ID space was exhausted: {_column}");
        return _nextDependencyId++;
    }

    private static void ValidateHolderStatusResult(
        HolderStatusRequirementKey requirement,
        ChunkGenerationFutureResult result)
    {
        if (!result.Succeeded || result.Status < requirement.RequiredStatus)
            throw new InvalidOperationException(
                $"Holder {requirement.Coord} completed at {result.Status}, " +
                $"below required status {requirement.RequiredStatus}. " +
                $"{result.FailureReason}");
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

    private void AddReadySectionIfMissing(int sectionIndex) =>
        _readySections.Add(sectionIndex);

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

    private readonly record struct HolderStatusRequirementKey(
        ChunkCoord Coord,
        ChunkGenerationStatus RequiredStatus);

    private sealed class PendingHolderStatusOperation(
        HolderStatusRequirementKey key,
        int initialSectionIndex)
    {
        public HolderStatusRequirementKey Key { get; } = key;

        public HashSet<int> WaitingSections { get; } = [initialSectionIndex];
    }
}
