namespace Asteria.Core.World;

/// <summary>
/// Result of one Minecraft-style holder future indexed by ChunkStatus.
/// A failed result is a value (like ChunkResult.error), not a faulted Task.
/// </summary>
public readonly record struct ChunkStatusFutureResult(
    ChunkStatus Status,
    bool Succeeded,
    string? FailureReason)
{
    public static ChunkStatusFutureResult Success(ChunkStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ChunkStatusFutureResult(status, true, null);
    }

    public static ChunkStatusFutureResult Failure(ChunkStatus status, string reason)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ChunkStatusFutureResult(status, false, reason);
    }

    public ChunkStatusFutureResult Validate()
    {
        ArgumentNullException.ThrowIfNull(Status);
        if (Succeeded && FailureReason is not null)
            throw new ArgumentException("A successful chunk-status result cannot contain a failure reason.");
        if (!Succeeded && string.IsNullOrWhiteSpace(FailureReason))
            throw new ArgumentException("A failed chunk-status result must explain why the status is unavailable.");
        return this;
    }
}

/// <summary>
/// Requests one status step from a holder. The returned future operation must
/// use the supplied dependency ID and expose an immutable ChunkStatusFutureResult.
/// </summary>
public delegate ChunkGenerationFutureOperation ChunkStatusStepScheduler(
    ulong dependencyId,
    ChunkHolder holder,
    ChunkStep step,
    ChunkGenerationCache2D cache);

/// <summary>Reads the last persisted status stored by a holder's chunk, if any.</summary>
public delegate ChunkStatus? PersistedChunkStatusReader(ChunkHolder holder);

/// <summary>
/// Minecraft 26.3 ChunkGenerationTask model: one horizontal column and one
/// target status. It schedules status layers in order, chooses the loading or
/// generation pyramid from persisted neighbor state, and yields on holder
/// futures without occupying a worker while those futures are incomplete.
/// </summary>
public sealed class ChunkGenerationTask : IChunkGenerationTask, IDisposable
{
    private readonly ChunkGenerationCache2D _cache;
    private readonly ChunkStep _generationTargetStep;
    private readonly PersistedChunkStatusReader _readPersistedStatus;
    private readonly ChunkStatusStepScheduler _scheduleStep;
    private readonly HashSet<ulong> _scheduledDependencyIds = [];
    private ChunkStatus? _scheduledStatus;
    private ulong _nextDependencyId;
    private bool _needsGeneration;
    private bool _waitingForLayer;
    private int _markedForCancellation;
    private int _disposed;

    public ChunkGenerationTask(
        ChunkGenerationCache2D cache,
        ChunkStatus targetStatus,
        PersistedChunkStatusReader readPersistedStatus,
        ChunkStatusStepScheduler scheduleStep)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentNullException.ThrowIfNull(targetStatus);
        _readPersistedStatus = readPersistedStatus ??
            throw new ArgumentNullException(nameof(readPersistedStatus));
        _scheduleStep = scheduleStep ??
            throw new ArgumentNullException(nameof(scheduleStep));

        if (cache.Step.TargetStatus != targetStatus)
            throw new ArgumentException(
                "The retained dependency cache must be built from the generation step for the requested target status.",
                nameof(cache));

        _generationTargetStep = ChunkPyramid.Generation.GetStep(targetStatus);
        var worstCaseRadius = _generationTargetStep.GetAccumulatedRadiusOf(ChunkStatus.Empty);
        if (cache.Radius < worstCaseRadius)
            throw new ArgumentException(
                $"The retained dependency cache radius {cache.Radius} is smaller than the generation envelope {worstCaseRadius}.",
                nameof(cache));

        Position = cache.Center;
        TargetStatus = targetStatus;
    }

    public ChunkColumnCoord Position { get; }

    public ChunkStatus TargetStatus { get; }

    public ChunkStatus? ScheduledStatus => _scheduledStatus;

    public bool NeedsGeneration => _needsGeneration;

    public int ScheduledDependencyCount => _scheduledDependencyIds.Count;

    /// <summary>
    /// Creates the task and acquires the worst-case generation dependency
    /// cache. The caller transfers cache ownership to the returned task.
    /// </summary>
    public static ChunkGenerationTask Create(
        ChunkDistanceManager distanceManager,
        ChunkColumnCoord position,
        ChunkStatus targetStatus,
        PersistedChunkStatusReader readPersistedStatus,
        ChunkStatusStepScheduler scheduleStep)
    {
        ArgumentNullException.ThrowIfNull(distanceManager);
        ArgumentNullException.ThrowIfNull(targetStatus);
        var cache = distanceManager.AcquireGenerationCache2D(
            position,
            ChunkPyramid.Generation.GetStep(targetStatus));
        try
        {
            return new ChunkGenerationTask(
                cache,
                targetStatus,
                readPersistedStatus,
                scheduleStep);
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }

    /// <summary>Marks the task to stop after its current dependency layer settles.</summary>
    public void MarkForCancellation() =>
        Interlocked.Exchange(ref _markedForCancellation, 1);

    public ChunkGenerationTaskAdvanceResult Advance(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completedDependencies);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        if (_waitingForLayer)
            ConsumeCompletedLayer(completedDependencies);
        else if (completedDependencies.Count != 0)
            throw new InvalidOperationException(
                "A chunk generation task received completions without an outstanding dependency layer.");

        if (Volatile.Read(ref _markedForCancellation) != 0 ||
            _scheduledStatus == TargetStatus)
            return Complete();

        // Loading-pyramid steps can require no neighboring chunks at all.
        // Skip those empty layers inline, just as runUntilWait does in vanilla.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextStatus = GetNextStatus();
            if (nextStatus is null)
                return Complete();

            var operations = ScheduleLayer(nextStatus);
            _scheduledStatus = nextStatus;
            if (operations.Count == 0)
            {
                if (_scheduledStatus == TargetStatus)
                    return Complete();
                continue;
            }

            _waitingForLayer = true;
            foreach (var operation in operations)
            {
                if (!_scheduledDependencyIds.Add(operation.DependencyId))
                    throw new InvalidOperationException(
                        $"Chunk generation dependency ID was reused: {operation.DependencyId}");
            }

            return ChunkGenerationTaskAdvanceResult.WaitForFutures(operations);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _cache.Dispose();
    }

    private ChunkStatus? GetNextStatus()
    {
        if (_scheduledStatus is null)
            return ChunkStatus.Empty;

        if (_scheduledStatus == TargetStatus)
            return null;

        if (!_needsGeneration &&
            _scheduledStatus == ChunkStatus.Empty &&
            !CanLoadWithoutGeneration())
        {
            _needsGeneration = true;
            return ChunkStatus.Empty;
        }

        var nextIndex = _scheduledStatus.Index + 1;
        if ((uint)nextIndex >= (uint)ChunkStatus.All.Length)
            throw new InvalidOperationException(
                $"Chunk generation advanced beyond its target status {TargetStatus}.");

        return ChunkStatus.All[nextIndex];
    }

    private List<ChunkGenerationFutureOperation> ScheduleLayer(ChunkStatus status)
    {
        var targetPyramid = _needsGeneration
            ? ChunkPyramid.Generation
            : ChunkPyramid.Loading;
        var targetStep = targetPyramid.GetStep(TargetStatus);
        var radius = targetStep.GetAccumulatedRadiusOf(status);
        var operations = new List<ChunkGenerationFutureOperation>(
            radius < 0 ? 0 : checked((radius * 2 + 1) * (radius * 2 + 1)));

        if (radius < 0)
            return operations;

        for (var dz = -radius; dz <= radius; dz++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var holder = _cache.GetHolder(Position.X + dx, Position.Z + dz);
            var persistedStatus = _readPersistedStatus(holder);
            var generate = persistedStatus is not null &&
                status.IsAfter(persistedStatus);
            var stepPyramid = generate
                ? ChunkPyramid.Generation
                : ChunkPyramid.Loading;

            if (generate && !_needsGeneration)
                throw new InvalidOperationException(
                    $"Chunk {holder.Position} requires generation at {status}, but the target task was admitted for loading only.");

            var dependencyId = NextDependencyId();
            var operation = _scheduleStep(
                dependencyId,
                holder,
                stepPyramid.GetStep(status),
                _cache);
            if (operation.DependencyId != dependencyId)
                throw new InvalidOperationException(
                    $"Step scheduler returned dependency ID {operation.DependencyId}; expected {dependencyId}.");
            operations.Add(operation);
        }

        return operations;
    }

    private bool CanLoadWithoutGeneration()
    {
        if (TargetStatus == ChunkStatus.Empty)
            return true;

        var centerStatus = _readPersistedStatus(_cache.GetHolder(Position));
        if (centerStatus is null || centerStatus.Index < TargetStatus.Index)
            return false;

        var dependencies = ChunkPyramid.Loading.GetStep(TargetStatus)
            .AccumulatedDependencies;
        var radius = dependencies.Radius;
        if (radius < 0)
            return true;

        for (var dz = -radius; dz <= radius; dz++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var distance = Math.Max(Math.Abs(dx), Math.Abs(dz));
            var requiredStatus = dependencies.GetRequiredStatus(distance);
            if (requiredStatus is null)
                continue;

            var holder = _cache.GetHolder(Position.X + dx, Position.Z + dz);
            var persistedStatus = _readPersistedStatus(holder);
            if (persistedStatus is null ||
                persistedStatus.Index < requiredStatus.Index)
                return false;
        }

        return true;
    }

    private void ConsumeCompletedLayer(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies)
    {
        if (completedDependencies.Count != _scheduledDependencyIds.Count)
            throw new InvalidOperationException(
                $"Chunk status layer {_scheduledStatus} completed {completedDependencies.Count} of {_scheduledDependencyIds.Count} dependencies.");

        var observed = new HashSet<ulong>();
        var failed = false;
        foreach (var completion in completedDependencies)
        {
            completion.Validate();
            if (!_scheduledDependencyIds.Contains(completion.DependencyId) ||
                !observed.Add(completion.DependencyId))
                throw new InvalidOperationException(
                    $"Chunk status layer received a stale or duplicate dependency {completion.DependencyId}.");

            if (completion.Error is not null)
                throw completion.Error;

            if (completion.Result is not ChunkStatusFutureResult statusResult)
                throw new InvalidOperationException(
                    $"Chunk status dependency {completion.DependencyId} did not return a ChunkStatusFutureResult.");

            statusResult.Validate();
            if (statusResult.Status != _scheduledStatus)
                throw new InvalidOperationException(
                    $"Chunk status dependency returned {statusResult.Status}; expected {_scheduledStatus}.");

            failed |= !statusResult.Succeeded;
        }

        _scheduledDependencyIds.Clear();
        _waitingForLayer = false;
        if (failed)
            MarkForCancellation();
    }

    private ulong NextDependencyId()
    {
        if (_nextDependencyId == ulong.MaxValue)
            throw new InvalidOperationException(
                $"Chunk generation task exhausted dependency IDs: {Position}, {TargetStatus}.");
        return ++_nextDependencyId;
    }

    private ChunkGenerationTaskAdvanceResult Complete()
    {
        Dispose();
        return ChunkGenerationTaskAdvanceResult.Completed;
    }
}
