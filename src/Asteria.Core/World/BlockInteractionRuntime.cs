using System.Numerics;

namespace Asteria.Core.World;

public enum BlockBreakLootPolicy : byte
{
    DropSelf,
    Suppress,
}

public enum BlockPickupResult : byte
{
    Collected = 0,
    UnloadedOrEmpty = 1,
    NotPickupable = 2,
    WrongItem = 3,
    InventoryFull = 4,
    MutationRejected = 5,
}

public sealed class BlockInteractionRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _droppedBlocks;
    private readonly SpikeFormationRuntime _spikes;

    public BlockInteractionRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        DroppedBlockRuntime droppedBlocks)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _mutations =
            mutations ??
            throw new ArgumentNullException(nameof(mutations));
        _droppedBlocks =
            droppedBlocks ??
            throw new ArgumentNullException(nameof(droppedBlocks));
        _spikes = new SpikeFormationRuntime(
            _world, _blocks, _mutations, _droppedBlocks);
    }

    public BlockBreakDecision Break(
        VoxelWorldHit hit,
        BlockBreakLootPolicy lootPolicy = BlockBreakLootPolicy.DropSelf)
    {
        if (!Enum.IsDefined(lootPolicy))
            throw new ArgumentOutOfRangeException(nameof(lootPolicy));

        var decision =
            BlockInteractionResolver.ResolveBreak(
                _world,
                hit);

        if (!decision.Accepted)
        {
            return decision;
        }

        var definition =
            _blocks.GetDefinition(
                decision.Cell.Block);

        if (definition.Interaction == BlockInteractionKind.Pickup)
        {
            return BlockBreakDecision.Reject(
                decision.Position, BlockBreakRejection.PickupOnly);
        }

        if (definition.Mining.Unbreakable)
        {
            return BlockBreakDecision.Reject(
                decision.Position,
                BlockBreakRejection.Unbreakable);
        }

        if (definition.Shape.Kind == BlockShapeKind.Spike)
            return _spikes.Break(decision, lootPolicy);

        var snapshot =
            lootPolicy == BlockBreakLootPolicy.DropSelf &&
            definition.DropsSelf
                ? BlockStateSnapshot.Capture(
                    _world,
                    decision.Position,
                    decision.Cell)
                : null;

        if (!_spikes.RemoveSupport(decision.Position, lootPolicy))
        {
            return BlockBreakDecision.Reject(
                decision.Position,
                BlockBreakRejection.MutationRejected);
        }

        if (snapshot is not null)
        {
            _droppedBlocks.Spawn(
                snapshot,
                CenterOf(decision.Position));
        }

        return decision;
    }

    /// <summary>
    /// Atomically collects a pickup-only voxel into the authoritative inventory.
    /// No physical dropped entity is created.
    /// </summary>
    public BlockPickupResult Pickup(
        VoxelWorldHit hit,
        PlayerInventory inventory,
        InventoryEntry item)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(item);

        var target = BlockInteractionResolver.ResolveBreak(_world, hit);
        if (!target.Accepted)
            return BlockPickupResult.UnloadedOrEmpty;

        var definition = _blocks.GetDefinition(target.Cell.Block);
        if (definition.Interaction != BlockInteractionKind.Pickup)
            return BlockPickupResult.NotPickupable;

        if (item.Kind != InventoryEntryKind.Item ||
            !string.Equals(item.Id, definition.PickupItemId,
                StringComparison.Ordinal) ||
            item.Metadata.Count != 0)
        {
            return BlockPickupResult.WrongItem;
        }

        var stack = new InventoryStack(item);
        if (!inventory.CanInsert(stack))
            return BlockPickupResult.InventoryFull;

        if (!_mutations.SetCellAt(target.Position, VoxelCell.Empty, out _))
            return BlockPickupResult.MutationRejected;

        if (!inventory.TryInsert(stack))
            throw new InvalidOperationException(
                "Inventory capacity changed inside an accepted pickup mutation.");

        return BlockPickupResult.Collected;
    }

    public BlockPlacementDecision Place(
        VoxelWorldHit hit,
        VoxelCell cell,
        WorldAabb playerBounds)
    {
        var spike = !cell.IsEmpty &&
            _blocks.GetDefinition(cell.Block).Shape.Kind == BlockShapeKind.Spike;
        if (spike)
        {
            // Picked-up/generated segment metadata must never leak into a
            // newly placed formation.
            cell = new VoxelCell(
                cell.Block, cell.TextureRotation, cell.Orientation,
                cell.Facing,
                SpikeSegmentState.Encode(0, 1, hit.NormalY < 0));
        }

        var decision =
            BlockInteractionResolver.ResolvePlacement(
                _world,
                _blocks,
                hit,
                cell,
                playerBounds);

        if (!decision.Accepted)
        {
            return decision;
        }

        if (spike)
            return _spikes.Place(decision, hit.NormalY < 0);

        if (!_mutations.SetCellAt(
                decision.Position,
                decision.Cell,
                out _))
        {
            return BlockPlacementDecision.Reject(
                decision.Position,
                decision.Cell,
                BlockPlacementRejection.MutationRejected);
        }

        return decision;
    }

    private static Vector3 CenterOf(
        WorldVoxelCoord position) =>
        new(
            position.X + 0.5f,
            position.Y + 0.5f,
            position.Z + 0.5f);
}
