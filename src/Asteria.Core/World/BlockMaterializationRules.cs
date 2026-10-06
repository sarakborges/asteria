namespace Asteria.Core.World;

public enum BlockMaterializationState : byte
{
    Available = 0,
    Occupied = 1,
    Unloaded = 2,
    BelowWorld = 3,
}

public static class BlockMaterializationRules
{
    public static BlockMaterializationState Evaluate(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (position.Y < 0)
        {
            return BlockMaterializationState.BelowWorld;
        }

        if (!world.IsLoadedAt(position))
        {
            return BlockMaterializationState.Unloaded;
        }

        return world.GetCellOrEmpty(position).IsEmpty
            ? BlockMaterializationState.Available
            : BlockMaterializationState.Occupied;
    }
}
