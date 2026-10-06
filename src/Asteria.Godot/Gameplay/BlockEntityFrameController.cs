using Asteria.Client.Rendering;
using Asteria.Core.World;

namespace Asteria.Client.Gameplay;

public readonly record struct BlockEntityFrameReport(
    BlockPhysicsWakeResult Wake,
    int Landed,
    DroppedBlockAdvanceResult Dropped,
    int FallingCount,
    int DroppedCount,
    int PendingPhysicsUpdates)
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

    public int FallingCount =>
        _blockPhysics.ActiveCount;

    public int DroppedCount =>
        _droppedBlocks.ActiveCount;

    public int PendingPhysicsUpdates =>
        _updates.Count;

    public BlockEntityFrameReport Advance(
        double deltaSeconds,
        double gravityStrength)
    {
        var wake =
            _worldTicks.TicksThisFrame > 0
                ? _blockPhysics.ProcessWakeups()
                : BlockPhysicsWakeResult.Empty;

        var landed =
            _blockPhysics.Advance(
                deltaSeconds,
                gravityStrength);
        var dropped =
            _droppedBlocks.Advance(
                deltaSeconds,
                gravityStrength);

        _presentations.Sync(
            _blockPhysics.ActiveBlocks,
            _droppedBlocks.ActiveBlocks);

        return new BlockEntityFrameReport(
            wake,
            landed,
            dropped,
            _blockPhysics.ActiveCount,
            _droppedBlocks.ActiveCount,
            _updates.Count);
    }
}
