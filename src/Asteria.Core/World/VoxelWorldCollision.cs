namespace Asteria.Core.World;

public enum VoxelWorldCollisionState : byte
{
    Clear = 0,
    Blocked = 1,
    Unloaded = 2,
}

public readonly record struct VoxelWorldCollisionResult(
    VoxelWorldCollisionState State,
    WorldVoxelCoord? Voxel)
{
    public bool IsClear =>
        State == VoxelWorldCollisionState.Clear;
}

public static class VoxelWorldCollision
{
    private const float BoundaryEpsilon = 0.00001f;

    public static VoxelWorldCollisionResult QueryDetailed(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldAabb bounds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        var minimum = bounds.Minimum;
        var maximum = bounds.Maximum;

        var minimumX =
            (int)MathF.Floor(minimum.X);
        var minimumY =
            (int)MathF.Floor(minimum.Y);
        var minimumZ =
            (int)MathF.Floor(minimum.Z);
        var maximumX =
            (int)MathF.Floor(
                maximum.X - BoundaryEpsilon);
        var maximumY =
            (int)MathF.Floor(
                maximum.Y - BoundaryEpsilon);
        var maximumZ =
            (int)MathF.Floor(
                maximum.Z - BoundaryEpsilon);

        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var z = minimumZ; z <= maximumZ; z++)
            {
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var position =
                        new WorldVoxelCoord(
                            x,
                            y,
                            z);

                    if (y < 0)
                    {
                        return new VoxelWorldCollisionResult(
                            VoxelWorldCollisionState.Blocked,
                            position);
                    }

                    if (!world.IsLoadedAt(position))
                    {
                        return new VoxelWorldCollisionResult(
                            VoxelWorldCollisionState.Unloaded,
                            position);
                    }

                    var cell =
                        world.GetCellOrEmpty(position);

                    if (cell.IsEmpty)
                    {
                        continue;
                    }

                    var definition =
                        blocks.GetDefinition(cell.Block);

                    if (BlockGeometry.Intersects(
                            definition,
                            cell,
                            world.GetMicroblockMaskOrEmpty(position),
                            position,
                            bounds))
                    {
                        return new VoxelWorldCollisionResult(
                            VoxelWorldCollisionState.Blocked,
                            position);
                    }
                }
            }
        }

        return new VoxelWorldCollisionResult(
            VoxelWorldCollisionState.Clear,
            null);
    }

    public static VoxelWorldCollisionState Query(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldAabb bounds) =>
        QueryDetailed(
            world,
            blocks,
            bounds).State;

    public static bool IsClear(
        VoxelWorld world,
        BlockRegistry blocks,
        WorldAabb bounds) =>
        QueryDetailed(
            world,
            blocks,
            bounds).IsClear;
}
