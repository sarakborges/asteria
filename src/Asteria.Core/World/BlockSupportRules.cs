namespace Asteria.Core.World;

public static class BlockPhysicsCapabilities
{
    public const string Gravity = "gravity";
    public const string SupportBelow = "support_below";
    public const string SupportAbove = "support_above";
    public const string SupportWall = "support_wall";
}

public enum BlockSupportState : byte
{
    Supported = 0,
    Unsupported = 1,
    Unloaded = 2,
}

public static class BlockSupportRules
{
    private enum AdditionalSupportRequirement : byte
    {
        None = 0,
        Below = 1,
    }

    public static BlockSupportState Evaluate(
        VoxelWorld world,
        BlockRegistry blocks,
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position) =>
        Evaluate(world, blocks, definition, cell, microblockMask, position,
            out _, AdditionalSupportRequirement.None);

    // Placement requires a gravity block to have a base before insertion.
    // Runtime evaluation intentionally omits this extra requirement so the
    // physics system can detach the block and simulate its fall.
    public static BlockSupportState EvaluateForPlacement(
        VoxelWorld world,
        BlockRegistry blocks,
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var additionalRequirement =
            definition.HasTag(BlockPhysicsCapabilities.Gravity)
                ? AdditionalSupportRequirement.Below
                : AdditionalSupportRequirement.None;
        return Evaluate(world, blocks, definition, cell, microblockMask, position,
            out _, additionalRequirement);
    }

    // The missing dependency is returned by the same support calculation,
    // so physics wakeups can suspend until that chunk becomes resident.
    public static BlockSupportState Evaluate(
        VoxelWorld world,
        BlockRegistry blocks,
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position,
        out WorldVoxelCoord missingSupport) =>
        Evaluate(world, blocks, definition, cell, microblockMask, position,
            out missingSupport, AdditionalSupportRequirement.None);

    private static BlockSupportState Evaluate(
        VoxelWorld world,
        BlockRegistry blocks,
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position,
        out WorldVoxelCoord missingSupport,
        AdditionalSupportRequirement additionalRequirement)
    {
        missingSupport = default;
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(definition);

        var needsBelow =
            additionalRequirement == AdditionalSupportRequirement.Below ||
            definition.HasTag(BlockPhysicsCapabilities.SupportBelow);
        var needsAbove = definition.HasTag(
            BlockPhysicsCapabilities.SupportAbove);
        var needsWall = definition.HasTag(
            BlockPhysicsCapabilities.SupportWall);
        if (!needsBelow && !needsAbove && !needsWall)
            return BlockSupportState.Supported;
        if ((needsBelow ? 1 : 0) +
            (needsAbove ? 1 : 0) +
            (needsWall ? 1 : 0) > 1)
            throw new InvalidOperationException(
                "A dependent block must require only one support direction.");
        if ((needsBelow && position.Y <= 0) ||
            (needsAbove && position.Y == int.MaxValue))
            return BlockSupportState.Unsupported;

        var (dx, dy, dz, supportFace, dependentFace) = needsWall
            ? cell.Facing switch
            {
                // Facing is the wall's outward normal. The solid
                // support is always behind the dependent sprite.
                HorizontalFacing.North => (0, 0, 1, BlockFace.Back, BlockFace.Front),
                HorizontalFacing.South => (0, 0, -1, BlockFace.Front, BlockFace.Back),
                HorizontalFacing.East => (-1, 0, 0, BlockFace.Right, BlockFace.Left),
                HorizontalFacing.West => (1, 0, 0, BlockFace.Left, BlockFace.Right),
                _ => throw new ArgumentOutOfRangeException(nameof(cell)),
            }
            : needsAbove
                ? (0, 1, 0, BlockFace.Bottom, BlockFace.Top)
                : (0, -1, 0, BlockFace.Top, BlockFace.Bottom);
        var supportPosition = position + (dx, dy, dz);

        if (!world.IsLoadedAt(
                supportPosition))
        {
            missingSupport = supportPosition;
            return BlockSupportState.Unloaded;
        }

        var supportCell =
            world.GetCellOrEmpty(
                supportPosition);

        if (supportCell.IsEmpty)
        {
            return BlockSupportState.Unsupported;
        }

        var supportDefinition =
            blocks.GetDefinition(
                supportCell.Block);
        var supportMask =
            world.GetMicroblockMaskOrEmpty(
                supportPosition);

        return BlockGeometry.SupportsFaceCoverage(
                supportDefinition,
                supportCell,
                supportMask,
                supportFace,
                definition,
                cell,
                microblockMask,
                dependentFace)
            ? BlockSupportState.Supported
            : BlockSupportState.Unsupported;
    }
}
