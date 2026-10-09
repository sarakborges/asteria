namespace Asteria.Core.World;

public static class BlockPhysicsCapabilities
{
    public const string Gravity = "gravity";
    public const string SupportBelow = "support_below";
    public const string SupportAbove = "support_above";
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

        var needsBelow = definition.HasTag(
            BlockPhysicsCapabilities.SupportBelow);
        var needsAbove = definition.HasTag(
            BlockPhysicsCapabilities.SupportAbove);
        if (!needsBelow && !needsAbove)
            return BlockSupportState.Supported;
        if (needsBelow && needsAbove)
            throw new InvalidOperationException(
                "A dependent block cannot require both upper and lower support.");
        if ((needsBelow && position.Y <= 0) ||
            (needsAbove && position.Y == int.MaxValue))
            return BlockSupportState.Unsupported;

        var supportPosition = new WorldVoxelCoord(
            position.X, position.Y + (needsAbove ? 1 : -1), position.Z);

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
                needsAbove ? BlockFace.Bottom : BlockFace.Top,
                definition,
                cell,
                microblockMask,
                needsAbove ? BlockFace.Top : BlockFace.Bottom)
            ? BlockSupportState.Supported
            : BlockSupportState.Unsupported;
    }
}
