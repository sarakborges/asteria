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

        return EvaluateBelow(
            world,
            blocks,
            position);
    }

    public static BlockSupportState EvaluateBelow(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        if (position.Y <= 0)
        {
            return BlockSupportState.Unsupported;
        }

        var support =
            position + (0, -1, 0);

        if (!world.IsLoadedAt(support))
        {
            return BlockSupportState.Unloaded;
        }

        var cell =
            world.GetCellOrEmpty(support);

        if (cell.IsEmpty)
        {
            return BlockSupportState.Unsupported;
        }

        var definition =
            blocks.GetDefinition(cell.Block);
        var mask =
            world.GetMicroblockMaskOrEmpty(
                support);

        return BlockGeometry.TouchesFace(
                definition,
                cell,
                mask,
                BlockFace.Top)
            ? BlockSupportState.Supported
            : BlockSupportState.Unsupported;
    }
}
