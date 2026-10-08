namespace Asteria.Core.World;

public enum BlockBreakRejection : byte
{
    None = 0,
    Unloaded = 1,
    Empty = 2,
    Unbreakable = 3,
    MutationRejected = 4,
    PickupOnly = 5,
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
    UnsupportedPlacementFace = 2,
    BelowWorld = 3,
    Unloaded = 4,
    Occupied = 5,
    PlayerIntersection = 6,
    MissingSupport = 7,
    SupportUnloaded = 8,
    MutationRejected = 9,
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

        switch (BlockMaterializationRules.Evaluate(
                    world,
                    target))
        {
            case BlockMaterializationState.BelowWorld:
                return BlockPlacementDecision.Reject(
                    target,
                    cell,
                    BlockPlacementRejection.BelowWorld);

            case BlockMaterializationState.Unloaded:
                return BlockPlacementDecision.Reject(
                    target,
                    cell,
                    BlockPlacementRejection.Unloaded);

            case BlockMaterializationState.Occupied:
                return BlockPlacementDecision.Reject(
                    target,
                    cell,
                    BlockPlacementRejection.Occupied);

            case BlockMaterializationState.Available:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        var definition =
            blocks.GetDefinition(cell.Block);
        var placementFace =
            FaceFromNormal(hit);

        if (!definition.SupportsPlacementFace(
                placementFace))
        {
            return BlockPlacementDecision.Reject(
                target,
                cell,
                BlockPlacementRejection.UnsupportedPlacementFace);
        }

        var support =
            BlockSupportRules.Evaluate(
                world,
                blocks,
                definition,
                cell,
                MicroblockMask.Empty,
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

        if (definition.IsCollidable &&
            BlockGeometry.Intersects(
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

    private static BlockFace FaceFromNormal(
        VoxelWorldHit hit) =>
        (hit.NormalX, hit.NormalY, hit.NormalZ)
            switch
            {
                (0, 1, 0) => BlockFace.Top,
                (0, -1, 0) => BlockFace.Bottom,
                (-1, 0, 0) => BlockFace.Left,
                (1, 0, 0) => BlockFace.Right,
                (0, 0, 1) => BlockFace.Front,
                (0, 0, -1) => BlockFace.Back,
                _ => throw new InvalidOperationException(
                    "Placement face requires a cardinal hit normal."),
            };

    private static bool IsCardinalNormal(
        VoxelWorldHit hit) =>
        Math.Abs(hit.NormalX) +
        Math.Abs(hit.NormalY) +
        Math.Abs(hit.NormalZ) == 1 &&
        Math.Abs(hit.NormalX) <= 1 &&
        Math.Abs(hit.NormalY) <= 1 &&
        Math.Abs(hit.NormalZ) <= 1;
}
