namespace Asteria.Core.World;

public enum BlockBreakRejection : byte
{
    None = 0,
    Unloaded = 1,
    Empty = 2,
}

public readonly record struct BlockBreakDecision(
    bool Accepted,
    WorldVoxelCoord Position,
    VoxelCell Cell,
    BlockBreakRejection Rejection)
{
    public static BlockBreakDecision Reject(
        WorldVoxelCoord position,
        BlockBreakRejection rejection) =>
        new(
            false,
            position,
            VoxelCell.Empty,
            rejection);
}

public enum BlockPlacementRejection : byte
{
    None = 0,
    InvalidSurfaceNormal = 1,
    BelowWorld = 2,
    Unloaded = 3,
    Occupied = 4,
    PlayerIntersection = 5,
    MissingSupport = 6,
    SupportUnloaded = 7,
}

public readonly record struct BlockPlacementDecision(
    bool Accepted,
    WorldVoxelCoord Position,
    VoxelCell Cell,
    BlockPlacementRejection Rejection)
{
    public static BlockPlacementDecision Reject(
        WorldVoxelCoord position,
        VoxelCell cell,
        BlockPlacementRejection rejection) =>
        new(
            false,
            position,
            cell,
            rejection);
}

public static class BlockInteractionResolver
{
    public static BlockBreakDecision ResolveBreak(
        VoxelWorld world,
        VoxelWorldHit hit)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!world.TryGetCell(
                hit.Voxel,
                out var cell))
        {
            return BlockBreakDecision.Reject(
                hit.Voxel,
                BlockBreakRejection.Unloaded);
        }

        if (cell.IsEmpty)
        {
            return BlockBreakDecision.Reject(
                hit.Voxel,
                BlockBreakRejection.Empty);
        }

        return new BlockBreakDecision(
            true,
            hit.Voxel,
            cell,
            BlockBreakRejection.None);
    }

    public static BlockPlacementDecision ResolvePlacement(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelWorldHit hit,
        VoxelCell cell,
        WorldAabb playerBounds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        var target =
            hit.Voxel +
            (
                hit.NormalX,
                hit.NormalY,
                hit.NormalZ);

        if (!IsCardinalNormal(hit))
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.InvalidSurfaceNormal);
        }

        if (target.Y < 0)
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.BelowWorld);
        }

        if (!world.IsLoadedAt(target))
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.Unloaded);
        }

        if (!world.GetCellOrEmpty(target).IsEmpty)
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.Occupied);
        }

        var definition =
            blocks.GetDefinition(cell.Block);
        var support =
            BlockSupportRules.Evaluate(
                world,
                definition,
                target);

        if (support == BlockSupportState.Unloaded)
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.SupportUnloaded);
        }

        if (support == BlockSupportState.Unsupported)
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.MissingSupport);
        }

        if (BlockGeometry.Intersects(
                definition,
                cell,
                MicroblockMask.Empty,
                target,
                playerBounds))
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.PlayerIntersection);
        }

        return new BlockPlacementDecision(
            true,
            target,
            cell,
            BlockPlacementRejection.None);
    }

    private static bool IsCardinalNormal(
        VoxelWorldHit hit) =>
        Math.Abs(hit.NormalX) +
        Math.Abs(hit.NormalY) +
        Math.Abs(hit.NormalZ) == 1 &&
        Math.Abs(hit.NormalX) <= 1 &&
        Math.Abs(hit.NormalY) <= 1 &&
        Math.Abs(hit.NormalZ) <= 1;
}
