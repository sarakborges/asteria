using System.Diagnostics;
using System.Numerics;
using Asteria.Client.Rendering;
using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

public readonly record struct BlockEntityFrameTimings(
    double WakeMilliseconds,
    double FallingMilliseconds,
    double DroppedMilliseconds,
    double PresentationMilliseconds);

public readonly record struct BlockEntityFrameReport(
    BlockPhysicsWakeResult Wake,
    int Landed,
    DroppedBlockAdvanceResult Dropped,
    int FallingCount,
    int DroppedCount,
    int PendingPhysicsUpdates,
    BlockEntityFrameTimings Timings)
{
    public bool HasChanges =>
        Wake.HasChanges ||
        Landed > 0 ||
        Dropped.Settled > 0 ||
        Dropped.Expired > 0;
}

public sealed class BlockEntityFrameController
{
    private readonly WorldTickClock _worldTicks;
    private readonly BlockPhysicsRuntime _blockPhysics;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private readonly BlockPhysicsUpdateQueue _updates;
    private readonly BlockEntityPresentationController _presentations;
    private Func<ChunkCoord, bool>? _isBlockTicking;
    private Func<ChunkCoord, bool>? _isEntityTicking;

    public BlockEntityFrameController(
        WorldTickClock worldTicks,
        BlockPhysicsRuntime blockPhysics,
        DroppedBlockRuntime droppedBlocks,
        BlockPhysicsUpdateQueue updates,
        BlockEntityPresentationController presentations)
    {
        _worldTicks =
            worldTicks ??
            throw new ArgumentNullException(nameof(worldTicks));
        _blockPhysics =
            blockPhysics ??
            throw new ArgumentNullException(nameof(blockPhysics));
        _droppedBlocks =
            droppedBlocks ??
            throw new ArgumentNullException(nameof(droppedBlocks));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
        _presentations =
            presentations ??
            throw new ArgumentNullException(nameof(presentations));
    }

    public void ConfigureSimulationEligibility(
        Func<ChunkCoord, bool> isBlockTicking,
        Func<ChunkCoord, bool> isEntityTicking)
    {
        _isBlockTicking = isBlockTicking ??
            throw new ArgumentNullException(nameof(isBlockTicking));
        _isEntityTicking = isEntityTicking ??
            throw new ArgumentNullException(nameof(isEntityTicking));
    }

    public int FallingCount =>
        _blockPhysics.ActiveCount;

    public int DroppedCount =>
        _droppedBlocks.ActiveCount;

    public int CollectNearby(
        Vector3 position,
        float radius,
        Func<InventoryStack, int> acceptUpTo) =>
        _droppedBlocks.CollectNearbyPartial(position, radius, acceptUpTo);

    public bool TryDropSelectedItem(
        PlayerInventory inventory, Vector3 position, Vector3 velocity) =>
        PlayerInventorySelectedDrop.TryDrop(
            inventory, _droppedBlocks, position, velocity);

    public int PendingPhysicsUpdates =>
        _updates.Count;

    public int WaitingPhysicsUpdates =>
        _updates.WaitingCount + _blockPhysics.WaitingGravityChunkCount;

    public int WaitingPhysicsSupportUpdates =>
        _updates.WaitingCount;

    public int WaitingPhysicsGravityUpdates =>
        _blockPhysics.WaitingGravityChunkCount;

    public BlockPhysicsWaitDistribution CaptureGravityWaitDistribution() =>
        _blockPhysics.CaptureGravityWaitDistribution();

    public int WaitingFallingCount =>
        _blockPhysics.WaitingFallingCount;

    public BlockEntityFrameReport Advance(
        double deltaSeconds,
        double gravityStrength)
    {
        var started = Stopwatch.GetTimestamp();
        if (_isBlockTicking is { } isBlockTicking)
            _blockPhysics.WakeSimulationChunks(isBlockTicking);
        var wake =
            _worldTicks.TicksThisFrame > 0
                ? _blockPhysics.ProcessWakeups(_isBlockTicking)
                : BlockPhysicsWakeResult.Empty;
        var wakeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        started = Stopwatch.GetTimestamp();
        var landed =
            _blockPhysics.Advance(
                deltaSeconds,
                gravityStrength,
                _isBlockTicking);
        var fallingMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        started = Stopwatch.GetTimestamp();
        var dropped =
            _droppedBlocks.Advance(
                deltaSeconds,
                gravityStrength,
                _isEntityTicking);
        var droppedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        started = Stopwatch.GetTimestamp();
        _presentations.SyncByIdOrder(
            _blockPhysics.ActiveBlocks,
            _droppedBlocks.ActiveBlocks);
        var presentationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return new BlockEntityFrameReport(
            wake,
            landed,
            dropped,
            _blockPhysics.ActiveCount,
            _droppedBlocks.ActiveCount,
            _updates.Count,
            new BlockEntityFrameTimings(
                wakeMs, fallingMs, droppedMs, presentationMs));
    }
}
