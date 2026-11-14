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
        ChunkGenerationStatus? progressStatus = null)
    {
        if (progressStatus is { } status &&
            (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded))
            throw new ArgumentOutOfRangeException(nameof(progressStatus));

        Kind = kind;
        WorkerOperations = workerOperations;
        CompletionPayload = completionPayload;
        ProgressStatus = progressStatus;
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
/// Engine-agnostic, serial-coordinator-owned generation task. Advance must be a
/// bounded coordinator slice: it may inspect completed detached results and
/// produce worker operations, but must not perform long CPU work or block.
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
