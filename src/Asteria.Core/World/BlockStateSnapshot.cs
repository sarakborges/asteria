namespace Asteria.Core.World;

public sealed record BlockStateSnapshot
{
    public BlockStateSnapshot(
        VoxelCell cell,
        MicroblockMask microblockMask)
    {
        if (cell.IsEmpty)
        {
            throw new ArgumentException(
                "A portable block snapshot cannot be empty.",
                nameof(cell));
        }

        Cell = new VoxelCell(
            cell.Block,
            cell.TextureRotation,
            cell.Orientation,
            cell.Facing,
            cell.State);
        MicroblockMask = microblockMask;
    }

    public VoxelCell Cell { get; }

    public MicroblockMask MicroblockMask { get; }

    public bool HasMicroblockGeometry =>
        !MicroblockMask.IsEmpty;

    public static BlockStateSnapshot Capture(
        VoxelWorld world,
        WorldVoxelCoord position,
        VoxelCell cell)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (cell.IsEmpty)
        {
            throw new ArgumentException(
                "Cannot capture an empty voxel as a block snapshot.",
                nameof(cell));
        }

        var mask =
            cell.HasMicroblockGeometry
                ? world.GetMicroblockMaskOrEmpty(position)
                : MicroblockMask.Empty;

        if (cell.HasMicroblockGeometry &&
            mask.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Voxel {position} references microblock geometry " +
                "that is missing from its resident chunk palette.");
        }

        return new BlockStateSnapshot(
            cell,
            mask);
    }

    public static BlockStateSnapshot FromCell(
        VoxelCell cell) =>
        new(
            cell,
            MicroblockMask.Empty);
}
