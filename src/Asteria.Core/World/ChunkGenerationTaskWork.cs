using System.Collections.Immutable;

namespace Asteria.Core.World;

/// <summary>How a logical column task finished its current coordinator slice.</summary>
public enum ChunkGenerationTaskAdvanceKind : byte
{
    /// <summary>The target status is ready and no further task progression is required.</summary>
    Completed,
    /// <summary>The task has more coordinator-owned transitions but no external wait.</summary>
    Yield,
    /// <summary>The task forked independent CPU operations and must wait for their results.</summary>
    WaitForWorkers,
    /// <summary>The task registered non-blocking continuations on holder/status futures.</summary>
    WaitForFutures,
}

/// <summary>One vertical section reached a new generation status.</summary>
public readonly record struct ChunkSectionGenerationProgress(
    ChunkCoord Coord,
    ChunkGenerationStatus Status,
    Chunk? Product = null)
{
    public ChunkSectionGenerationProgress Validate()
    {
        if (!Enum.IsDefined(Status) || Status == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(Status));
        return this;
    }
}

/// <summary>
/// One holder/status future requested by a generation task. The dispatcher
/// observes completion without blocking a worker and returns the detached
/// result through the same dependency mailbox used by worker forks.
/// </summary>
public readonly record struct ChunkGenerationFutureOperation
{
    private readonly Task<ChunkGenerationFutureResult>? _legacyFuture;
    private readonly Func<object?>? _readResult;

    public ChunkGenerationFutureOperation(
        ulong dependencyId,
        Task<ChunkGenerationFutureResult> future,
        Action? requestProducer = null)
    {
        if (dependencyId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(dependencyId), "Dependency ID zero is reserved.");
        _legacyFuture = future ?? throw new ArgumentNullException(nameof(future));
        UntypedFuture = future;
        _readResult = () => future.GetAwaiter().GetResult();
        DependencyId = dependencyId;
        RequestProducer = requestProducer;
    }

    private ChunkGenerationFutureOperation(
        ulong dependencyId,
        Task future,
        Func<object?> readResult,
        Action? requestProducer)
    {
        if (dependencyId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(dependencyId), "Dependency ID zero is reserved.");
        UntypedFuture = future ?? throw new ArgumentNullException(nameof(future));
        _readResult = readResult ?? throw new ArgumentNullException(nameof(readResult));
        DependencyId = dependencyId;
        RequestProducer = requestProducer;
        _legacyFuture = null;
    }

    public ulong DependencyId { get; }

    /// <summary>Strongly typed future retained for legacy materialization steps.</summary>
    public Task<ChunkGenerationFutureResult> Future =>
        _legacyFuture ?? throw new InvalidOperationException(
            "This step uses a typed ChunkStatus future; read it through GetResult after UntypedFuture completes.");

    /// <summary>Untyped completion signal used by the shared dispatcher.</summary>
    public Task UntypedFuture { get; }

    /// <summary>
    /// Idempotent producer admission request executed by the serial dispatcher
    /// before it subscribes to an incomplete future. It must only enqueue or
    /// raise the target holder's requested status; it must not block.
    /// </summary>
    public Action? RequestProducer { get; }

    public object? GetResult() =>
        _readResult is null
            ? throw new InvalidOperationException("The default future operation has no result reader.")
            : _readResult();

    public static ChunkGenerationFutureOperation From<T>(
        ulong dependencyId,
        Task<T> future,
        Action? requestProducer = null) =>
        new(
            dependencyId,
            future,
            () => future.GetAwaiter().GetResult(),
            requestProducer);
}

/// <summary>
/// One worker operation requested by a generation task. Dependency IDs are
/// unique within a wait batch and later identify the corresponding completion.
/// </summary>
public readonly record struct ChunkGenerationWorkerOperation
{
    public ChunkGenerationWorkerOperation(
        ulong dependencyId,
        Func<CancellationToken, object?> execute,
        ChunkGenerationExecutionPolicy executionPolicy =
            ChunkGenerationExecutionPolicy.SharedWorkerPool)
    {
        if (dependencyId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(dependencyId), "Dependency ID zero is reserved.");
        ArgumentNullException.ThrowIfNull(execute);
        if (executionPolicy is not (
            ChunkGenerationExecutionPolicy.SharedWorkerPool or
            ChunkGenerationExecutionPolicy.LightExecutor))
            throw new ArgumentOutOfRangeException(nameof(executionPolicy));

        DependencyId = dependencyId;
        Execute = execute;
        ExecutionPolicy = executionPolicy;
    }

    public ulong DependencyId { get; }

    public Func<CancellationToken, object?> Execute { get; }

    /// <summary>Serial executor lane that owns this asynchronous operation.</summary>
    public ChunkGenerationExecutionPolicy ExecutionPolicy { get; }
}

/// <summary>
/// Immutable outcome of one bounded call to IChunkGenerationTask.Advance.
/// The dispatcher assigns wait tokens and task identity only after accepting
/// the returned worker operations.
/// </summary>
public sealed class ChunkGenerationTaskAdvanceResult
{
    private static readonly ChunkGenerationTaskAdvanceResult CompletedResult =
        new(ChunkGenerationTaskAdvanceKind.Completed, ImmutableArray<ChunkGenerationWorkerOperation>.Empty, null);
    private static readonly ChunkGenerationTaskAdvanceResult YieldResult =
        new(ChunkGenerationTaskAdvanceKind.Yield, ImmutableArray<ChunkGenerationWorkerOperation>.Empty, null);

    private ChunkGenerationTaskAdvanceResult(
        ChunkGenerationTaskAdvanceKind kind,
        ImmutableArray<ChunkGenerationWorkerOperation> workerOperations,
        object? completionPayload,
        ChunkGenerationStatus? progressStatus = null,
        ImmutableArray<ChunkSectionGenerationProgress> sectionProgress = default,
        ImmutableArray<ChunkGenerationFutureOperation> futureOperations = default)
    {
        if (progressStatus is { } status &&
            (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded))
            throw new ArgumentOutOfRangeException(nameof(progressStatus));

        Kind = kind;
        WorkerOperations = workerOperations;
        CompletionPayload = completionPayload;
        ProgressStatus = progressStatus;
        SectionProgress = sectionProgress.IsDefault
            ? ImmutableArray<ChunkSectionGenerationProgress>.Empty
            : sectionProgress;
        foreach (var progress in SectionProgress)
            progress.Validate();
        FutureOperations = futureOperations.IsDefault
            ? ImmutableArray<ChunkGenerationFutureOperation>.Empty
            : futureOperations;
    }

    public ChunkGenerationTaskAdvanceKind Kind { get; }

    public ImmutableArray<ChunkGenerationWorkerOperation> WorkerOperations { get; }

    /// <summary>Detached result published when the logical task completes.</summary>
    public object? CompletionPayload { get; }

    /// <summary>
    /// Highest generation status newly achieved by this advance. The owner
    /// thread publishes it to the corresponding ChunkHolder futures.
    /// </summary>
    public ChunkGenerationStatus? ProgressStatus { get; }

    /// <summary>
    /// Per-section status transitions carried to the owning coordinator. The
    /// worker task never mutates a holder; the dispatcher publishes these
    /// detached transitions on its coordinator thread.
    /// </summary>
    public ImmutableArray<ChunkSectionGenerationProgress> SectionProgress { get; }

    public ImmutableArray<ChunkGenerationFutureOperation> FutureOperations { get; }

    public ChunkGenerationTaskAdvanceResult WithSectionProgress(
        IEnumerable<ChunkSectionGenerationProgress> sectionProgress)
    {
        ArgumentNullException.ThrowIfNull(sectionProgress);
        var updates = sectionProgress.ToImmutableArray();
        if (updates.IsDefaultOrEmpty)
            return this;

        var combined = SectionProgress.AddRange(updates);
        return new ChunkGenerationTaskAdvanceResult(
            Kind, WorkerOperations, CompletionPayload, ProgressStatus, combined,
            FutureOperations);
    }

    public static ChunkGenerationTaskAdvanceResult Completed => CompletedResult;

    public static ChunkGenerationTaskAdvanceResult CompletedWith(
        object payload,
        ChunkGenerationStatus? progressStatus = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new ChunkGenerationTaskAdvanceResult(
            ChunkGenerationTaskAdvanceKind.Completed,
            ImmutableArray<ChunkGenerationWorkerOperation>.Empty,
            payload,
            progressStatus);
    }

    public static ChunkGenerationTaskAdvanceResult Yield => YieldResult;

    public static ChunkGenerationTaskAdvanceResult YieldWithStatus(
        ChunkGenerationStatus progressStatus) =>
        new(
            ChunkGenerationTaskAdvanceKind.Yield,
            ImmutableArray<ChunkGenerationWorkerOperation>.Empty,
            null,
            progressStatus);

    public static ChunkGenerationTaskAdvanceResult WaitForFutures(
        IEnumerable<ChunkGenerationFutureOperation> futureOperations,
        ChunkGenerationStatus? progressStatus = null)
    {
        ArgumentNullException.ThrowIfNull(futureOperations);
        var operations = futureOperations.ToImmutableArray();
        if (operations.IsDefaultOrEmpty)
            throw new ArgumentException(
                "A task can wait only when it has at least one future dependency.",
                nameof(futureOperations));

        var dependencyIds = new HashSet<ulong>();
        foreach (var operation in operations)
        {
            if (operation.DependencyId == 0)
                throw new ArgumentException(
                    "Future dependency ID zero is reserved.",
                    nameof(futureOperations));
            ArgumentNullException.ThrowIfNull(operation.Future);
            if (!dependencyIds.Add(operation.DependencyId))
                throw new ArgumentException(
                    $"Future dependency ID {operation.DependencyId} is duplicated.",
                    nameof(futureOperations));
        }

        return new ChunkGenerationTaskAdvanceResult(
            ChunkGenerationTaskAdvanceKind.WaitForFutures,
            ImmutableArray<ChunkGenerationWorkerOperation>.Empty,
            null,
            progressStatus,
            futureOperations: operations);
    }

    public static ChunkGenerationTaskAdvanceResult WaitForWorkers(
        IEnumerable<ChunkGenerationWorkerOperation> workerOperations,
        ChunkGenerationStatus? progressStatus = null)
    {
        ArgumentNullException.ThrowIfNull(workerOperations);
        var operations = workerOperations.ToImmutableArray();
        if (operations.IsDefaultOrEmpty)
            throw new ArgumentException(
                "A task can wait only when it has at least one worker operation.",
                nameof(workerOperations));

        var dependencyIds = new HashSet<ulong>();
        foreach (var operation in operations)
        {
            if (operation.DependencyId == 0)
                throw new ArgumentException(
                    "Worker operation dependency ID zero is reserved.",
                    nameof(workerOperations));
            ArgumentNullException.ThrowIfNull(operation.Execute);
            if (!dependencyIds.Add(operation.DependencyId))
                throw new ArgumentException(
                    $"Worker dependency ID {operation.DependencyId} is duplicated.",
                    nameof(workerOperations));
        }

        return new ChunkGenerationTaskAdvanceResult(
            ChunkGenerationTaskAdvanceKind.WaitForWorkers,
            operations,
            null,
            progressStatus);
    }
}

/// <summary>
/// Engine-agnostic generation task advanced by its dimension's serial
/// continuation executor on the shared worker pool. The owning dispatcher
/// remains responsible for lifecycle, tickets, priority and result collection.
/// Advance must be bounded: it may inspect detached dependency results, perform
/// one coordinator/ordered stage quantum, or produce worker operations, but
/// must never block on an incomplete future or mutate the live world.
/// </summary>
public interface IChunkGenerationTask
{
    ChunkGenerationTaskAdvanceResult Advance(
        IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
        CancellationToken cancellationToken);
}

/// <summary>Terminal disposition of a logical generation task.</summary>
public enum ChunkGenerationTaskOutcomeKind : byte
{
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// Result returned from the serial dispatcher to the owning world loop.
/// Completed payloads are detached from the live world and may be committed
/// only after the caller revalidates that the target column is still desired.
/// </summary>
public readonly record struct ChunkGenerationTaskOutcome(
    ChunkGenerationTaskInstance Task,
    ChunkGenerationTaskOutcomeKind Kind,
    object? Result,
    Exception? Error);
