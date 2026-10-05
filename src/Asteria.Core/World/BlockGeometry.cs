namespace Asteria.Core.World;

public static class BlockGeometry
{
    // Partial block geometry is sampled only for non-cube cells and cube faces
    // neighboring them. 32 keeps centered 1/16 layers symmetric around 0.5.
    public const int Resolution = 32;

    public static bool RequiresFineMeshing(BlockDefinition definition, VoxelCell cell) =>
        cell.HasMicroblockGeometry || definition.Shape.Kind != BlockShapeKind.Cube;

    public static bool IsOccupied(
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        int x,
        int y,
        int z)
    {
        if (cell.IsEmpty ||
            (uint)x >= Resolution ||
            (uint)y >= Resolution ||
            (uint)z >= Resolution)
        {
            return false;
        }

        if (cell.HasMicroblockGeometry)
        {
            var scale = Resolution / MicroblockMask.Edge;
            return microblockMask.Contains(x / scale, y / scale, z / scale);
        }

        var (sourceX, sourceY, sourceZ) = ToSourceCoordinates(cell.Orientation, x, y, z);

        return definition.Shape.Kind switch
        {
            BlockShapeKind.Cube => true,
            BlockShapeKind.Layer => LayerContains(definition.Shape, sourceY),
            BlockShapeKind.Hollow => HollowContains(definition.Shape, sourceX, sourceZ),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Shape.Kind, "Unknown block shape."),
        };
    }

    private static bool LayerContains(BlockShapeDefinition shape, int sourceY)
    {
        var thickness = Math.Clamp(
            (int)MathF.Ceiling(shape.Thickness * Resolution),
            1,
            Resolution);

        if (shape.LayerPlacement == BlockLayerPlacement.Surface)
        {
            return sourceY < thickness;
        }

        var start = (Resolution - thickness) / 2;
        return sourceY >= start && sourceY < start + thickness;
    }

    private static bool HollowContains(BlockShapeDefinition shape, int sourceX, int sourceZ)
    {
        var wall = Math.Clamp(
            (int)MathF.Ceiling(shape.WallThickness * Resolution),
            1,
            Resolution / 2);

        return sourceX < wall ||
               sourceX >= Resolution - wall ||
               sourceZ < wall ||
               sourceZ >= Resolution - wall;
    }

    private static (int X, int Y, int Z) ToSourceCoordinates(
        BlockOrientation orientation,
        int x,
        int y,
        int z) => orientation switch
    {
        BlockOrientation.Y => (x, y, z),

        // Source +Y points toward world +Z.
        BlockOrientation.Z => (
            x,
            z,
            Resolution - 1 - y),

        // Source +Y points toward world +X.
        BlockOrientation.X => (
            Resolution - 1 - y,
            x,
            z),

        _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
    };
}
