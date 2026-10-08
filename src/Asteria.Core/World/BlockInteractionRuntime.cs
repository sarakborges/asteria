using System.Numerics;

namespace Asteria.Core.World;

public enum BlockBreakLootPolicy : byte
{
    DropSelf,
    Suppress,
}

public sealed class BlockInteractionRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly DroppedBlockRuntime _droppedBlocks;

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

        if (definition.Mining.Unbreakable)
        {
            return BlockBreakDecision.Reject(
                decision.Position,
                BlockBreakRejection.Unbreakable);
        }

        var snapshot =
            lootPolicy == BlockBreakLootPolicy.DropSelf &&
            definition.DropsSelf
                ? BlockStateSnapshot.Capture(
                    _world,
                    decision.Position,
                    decision.Cell)
                : null;

        if (!_mutations.SetCellAt(
                decision.Position,
                VoxelCell.Empty,
                out _))
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

    public BlockPlacementDecision Place(
        VoxelWorldHit hit,
        VoxelCell cell,
        WorldAabb playerBounds)
    {
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
