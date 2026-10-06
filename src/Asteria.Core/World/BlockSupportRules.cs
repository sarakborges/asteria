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
        BlockDefinition definition,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);
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

        var support =
            position + (0, -1, 0);

        if (!world.IsLoadedAt(support))
        {
            return BlockSupportState.Unloaded;
        }

        return world.GetCellOrEmpty(support).IsEmpty
            ? BlockSupportState.Unsupported
            : BlockSupportState.Supported;
    }
}
