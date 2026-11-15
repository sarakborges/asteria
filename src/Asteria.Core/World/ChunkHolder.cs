namespace Asteria.Core.World;

/// <summary>
/// Result delivered by one of a chunk holder's asynchronous status futures.
/// Failed promotions carry a reason and are completed as values, allowing
/// callers to observe demotion without faulted continuation chains.
/// </summary>
public readonly record struct ChunkHolderFutureResult(
    bool Succeeded,
    ChunkFullStatus Status,
    string? FailureReason)
{
    public static ChunkHolderFutureResult Success(ChunkFullStatus status) =>
        new(true, status, null);

    public static ChunkHolderFutureResult Unloaded(string reason) =>
        new(false, ChunkFullStatus.Inaccessible, reason);
}


/// <summary>
/// Gameplay-accessible chunk holder layered over generation state. It adds the
/// FULL, BLOCK_TICKING and ENTITY_TICKING promotion futures while inheriting
/// the ticket ceiling and generation futures from GenerationChunkHolder.
/// </summary>
public sealed class ChunkHolder : GenerationChunkHolder
{
    private TaskCompletionSource<ChunkHolderFutureResult> _fullChunkFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _blockTickingFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _entityTickingFuture = NewFuture();
    private int _dependencyClaimCount;

    internal ChunkHolder(ChunkColumnCoord position, int ticketLevel)
        : base(position, ticketLevel)
    {
        FullStatus = ChunkLevel.GetFullStatus(ticketLevel);
        ReconcilePromotionFutures();
    }

    /// <summary>Maximum full-chunk status permitted by the loading ticket level.</summary>
    public ChunkFullStatus FullStatus { get; private set; }

    public ChunkFullStatus CompletedFullStatus
    {
        get
        {
            if (Succeeded(_entityTickingFuture))
                return ChunkFullStatus.EntityTicking;
            if (Succeeded(_blockTickingFuture))
                return ChunkFullStatus.BlockTicking;
            if (Succeeded(_fullChunkFuture))
                return ChunkFullStatus.Full;
            return ChunkFullStatus.Inaccessible;
        }
    }

    /// <summary>
    /// A timed ticket may count down while its holder exists only after FULL
    /// promotion has completed and no generation task retains the holder.
    /// </summary>
    public bool IsReadyForSaving =>
        DependencyClaimCount == 0 &&
        CompletedFullStatus >= ChunkFullStatus.Full;

    public Task<ChunkHolderFutureResult> FullChunkFuture =>
        _fullChunkFuture.Task;

    public Task<ChunkHolderFutureResult> BlockTickingFuture =>
        _blockTickingFuture.Task;

    public Task<ChunkHolderFutureResult> EntityTickingFuture =>
        _entityTickingFuture.Task;

    public bool IsLoading => TicketLevel < ChunkLevel.Unloaded;

    /// <summary>Generation tasks retaining this holder as a dependency.</summary>
    public int DependencyClaimCount => Volatile.Read(ref _dependencyClaimCount);

    internal void AddDependencyClaim() =>
        Interlocked.Increment(ref _dependencyClaimCount);

    internal void RemoveDependencyClaim()
    {
        if (Interlocked.Decrement(ref _dependencyClaimCount) < 0)
            throw new InvalidOperationException(
                $"Chunk holder dependency claim count underflowed: {Position}");
    }

    internal override bool UpdateTicketLevel(int ticketLevel)
    {
        if (!base.UpdateTicketLevel(ticketLevel))
            return false;

        FullStatus = ChunkLevel.GetFullStatus(ticketLevel);
        ReconcilePromotionFutures();
        return true;
    }

    /// <summary>
    /// Invalidates generation products and the corresponding full-status
    /// promotions as one holder lifecycle transition.
    /// </summary>
    public bool ResetGenerationProgress(string reason)
    {
        if (!ResetGenerationProgressCore(reason))
            return false;

        ResetPromotionFuture(ref _fullChunkFuture, reason);
        ResetPromotionFuture(ref _blockTickingFuture, reason);
        ResetPromotionFuture(ref _entityTickingFuture, reason);
        return true;
    }

    /// <summary>
    /// Completes every promotion future covered by the supplied achieved
    /// status. A future is never completed successfully above the current
    /// loading-ticket ceiling.
    /// </summary>
    public bool CompleteFullStatus(ChunkFullStatus completedStatus)
    {
        if (completedStatus <= ChunkFullStatus.Inaccessible)
            throw new ArgumentOutOfRangeException(nameof(completedStatus));
        if (completedStatus > FullStatus)
            return false;

        var changed = false;
        if (completedStatus >= ChunkFullStatus.Full)
            changed |= _fullChunkFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.Full));
        if (completedStatus >= ChunkFullStatus.BlockTicking)
            changed |= _blockTickingFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.BlockTicking));
        if (completedStatus >= ChunkFullStatus.EntityTicking)
            changed |= _entityTickingFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.EntityTicking));
        return changed;
    }

    /// <summary>
    /// Completes pending promotion futures with a failed ChunkResult. The
    /// ticket stays installed, so a later retry may arm fresh futures.
    /// </summary>
    public bool FailPendingPromotions(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var failure = ChunkHolderFutureResult.Unloaded(reason);
        var changed = false;
        if (FullStatus >= ChunkFullStatus.Full)
            changed |= _fullChunkFuture.TrySetResult(failure);
        if (FullStatus >= ChunkFullStatus.BlockTicking)
            changed |= _blockTickingFuture.TrySetResult(failure);
        if (FullStatus >= ChunkFullStatus.EntityTicking)
            changed |= _entityTickingFuture.TrySetResult(failure);
        return changed;
    }

    private void ReconcilePromotionFutures()
    {
        ReconcileFuture(
            ref _fullChunkFuture,
            FullStatus >= ChunkFullStatus.Full,
            "Chunk is no longer allowed to remain FULL.");
        ReconcileFuture(
            ref _blockTickingFuture,
            FullStatus >= ChunkFullStatus.BlockTicking,
            "Chunk is no longer allowed to tick blocks.");
        ReconcileFuture(
            ref _entityTickingFuture,
            FullStatus >= ChunkFullStatus.EntityTicking,
            "Chunk is no longer allowed to tick entities.");
    }

    private static TaskCompletionSource<ChunkHolderFutureResult> NewFuture() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool Succeeded(
        TaskCompletionSource<ChunkHolderFutureResult> future) =>
        future.Task.IsCompletedSuccessfully &&
        future.Task.Result.Succeeded;

    private static void ResetPromotionFuture(
        ref TaskCompletionSource<ChunkHolderFutureResult> future,
        string reason)
    {
        future.TrySetResult(ChunkHolderFutureResult.Unloaded(reason));
        future = NewFuture();
    }

    private static void ReconcileFuture(
        ref TaskCompletionSource<ChunkHolderFutureResult> future,
        bool allowed,
        string demotionReason)
    {
        if (allowed)
        {
            if (future.Task.IsCompletedSuccessfully &&
                !future.Task.Result.Succeeded)
                future = NewFuture();
            return;
        }

        if (future.Task.IsCompletedSuccessfully)
        {
            if (future.Task.Result.Succeeded)
            {
                future = NewFuture();
                future.TrySetResult(
                    ChunkHolderFutureResult.Unloaded(demotionReason));
            }
            return;
        }

        future.TrySetResult(
            ChunkHolderFutureResult.Unloaded(demotionReason));
    }
}
