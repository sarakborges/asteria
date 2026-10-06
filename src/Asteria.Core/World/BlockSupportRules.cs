namespace Asteria.Core.World;

public static class BlockPhysicsCapabilities
{
    public const string Gravity = "gravity";
    public const string SupportBelow = "support_below";
}

public enum BlockSupportState : byte
{
    Supported = 0,
    Unsupported = 1,
    Unloaded = 2,
}

public static class BlockSupportRules
{
    public static BlockSupportState Evaluate(
        VoxelWorld world,
        BlockRegistry blocks,
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.HasTag(
                BlockPhysicsCapabilities.SupportBelow))
        {
            return BlockSupportState.Supported;
        }

        if (position.Y <= 0)
        {
            return BlockSupportState.Unsupported;
        }

        var supportPosition =
            position + (0, -1, 0);

        if (!world.IsLoadedAt(
                supportPosition))
        {
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
                BlockFace.Top,
                definition,
                cell,
                microblockMask,
                BlockFace.Bottom)
            ? BlockSupportState.Supported
            : BlockSupportState.Unsupported;
    }
}
