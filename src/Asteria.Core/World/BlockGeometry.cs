using System.Numerics;

namespace Asteria.Core.World;

public static class BlockGeometry
{
    // Partial block geometry is sampled only for non-cube cells and cube faces
    // neighboring them. 32 keeps centered 1/16 layers symmetric around 0.5.
    public const int Resolution = 32;

    public static bool RequiresFineMeshing(BlockDefinition definition, VoxelCell cell) =>
        cell.HasMicroblockGeometry || definition.Shape.Kind != BlockShapeKind.Cube;

    public static bool Intersects(
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        WorldVoxelCoord position,
        WorldAabb bounds)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (cell.IsEmpty ||
            !definition.IsCollidable)
        {
            return false;
        }

        var blockBounds =
            new WorldAabb(
                new Vector3(
                    position.X,
                    position.Y,
                    position.Z),
                new Vector3(
                    position.X + 1f,
                    position.Y + 1f,
                    position.Z + 1f));

        if (!blockBounds.Intersects(bounds))
        {
            return false;
        }

        if (!cell.HasMicroblockGeometry &&
            definition.Shape.Kind ==
                BlockShapeKind.Cube)
        {
            return true;
        }

        var localMinimum =
            Vector3.Max(
                bounds.Minimum -
                new Vector3(
                    position.X,
                    position.Y,
                    position.Z),
                Vector3.Zero);
        var localMaximum =
            Vector3.Min(
                bounds.Maximum -
                new Vector3(
                    position.X,
                    position.Y,
                    position.Z),
                Vector3.One);

        var minX =
            FineMinimum(localMinimum.X);
        var minY =
            FineMinimum(localMinimum.Y);
        var minZ =
            FineMinimum(localMinimum.Z);
        var maxX =
            FineMaximum(localMaximum.X);
        var maxY =
            FineMaximum(localMaximum.Y);
        var maxZ =
            FineMaximum(localMaximum.Z);

        for (var y = minY; y <= maxY; y++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (IsOccupied(
                            definition,
                            cell,
                            microblockMask,
                            x,
                            y,
                            z))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool TouchesFace(
        BlockDefinition definition,
        VoxelCell cell,
        MicroblockMask microblockMask,
        BlockFace face)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (cell.IsEmpty ||
            !definition.IsCollidable)
        {
            return false;
        }

        if (!RequiresFineMeshing(
                definition,
                cell))
        {
            return true;
        }

        for (var v = 0;
             v < Resolution;
             v++)
        {
            for (var u = 0;
                 u < Resolution;
                 u++)
            {
                var (x, y, z) =
                    FacePosition(
                        face,
                        u,
                        v);

                if (IsOccupied(
                        definition,
                        cell,
                        microblockMask,
                        x,
                        y,
                        z))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool SupportsFaceCoverage(
        BlockDefinition supportDefinition,
        VoxelCell supportCell,
        MicroblockMask supportMask,
        BlockFace supportFace,
        BlockDefinition dependentDefinition,
        VoxelCell dependentCell,
        MicroblockMask dependentMask,
        BlockFace dependentFace)
    {
        ArgumentNullException.ThrowIfNull(
            supportDefinition);
        ArgumentNullException.ThrowIfNull(
            dependentDefinition);

        if (supportCell.IsEmpty ||
            !supportDefinition.IsCollidable ||
            dependentCell.IsEmpty)
        {
            return false;
        }

        var hasDependentFootprint = false;

        for (var v = 0;
             v < Resolution;
             v++)
        {
            for (var u = 0;
                 u < Resolution;
                 u++)
            {
                var dependentPosition =
                    FacePosition(
                        dependentFace,
                        u,
                        v);

                if (!IsOccupied(
                        dependentDefinition,
                        dependentCell,
                        dependentMask,
                        dependentPosition.X,
                        dependentPosition.Y,
                        dependentPosition.Z))
                {
                    continue;
                }

                hasDependentFootprint =
                    true;

                var supportPosition =
                    FacePosition(
                        supportFace,
                        u,
                        v);

                if (!IsOccupied(
                        supportDefinition,
                        supportCell,
                        supportMask,
                        supportPosition.X,
                        supportPosition.Y,
                        supportPosition.Z))
                {
                    return false;
                }
            }
        }

        return hasDependentFootprint;
    }

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
            BlockShapeKind.Spike => SpikeContains(
                definition.Shape, cell.State, sourceX, sourceY, sourceZ),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Shape.Kind, "Unknown block shape."),
        };
    }

    private static int FineMinimum(
        float value) =>
        Math.Clamp(
            (int)MathF.Floor(
                value * Resolution),
            0,
            Resolution - 1);

    private static int FineMaximum(
        float value) =>
        Math.Clamp(
            (int)MathF.Ceiling(
                value * Resolution) - 1,
            0,
            Resolution - 1);

    private static (int X, int Y, int Z)
        FacePosition(
            BlockFace face,
            int u,
            int v) =>
        face switch
        {
            BlockFace.Top =>
                (u, Resolution - 1, v),
            BlockFace.Bottom =>
                (u, 0, v),
            BlockFace.Right =>
                (Resolution - 1, v, u),
            BlockFace.Left =>
                (0, v, u),
            BlockFace.Front =>
                (u, v, Resolution - 1),
            BlockFace.Back =>
                (u, v, 0),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

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

    private static bool SpikeContains(
        BlockShapeDefinition shape,
        ushort state,
        int x,
        int y,
        int z)
    {
        var radius = SpikeSegmentState.RadiusAt(
            shape, state, (y + 0.5f) / Resolution);
        var dx = (x + 0.5f) / Resolution - 0.5f;
        var dz = (z + 0.5f) / Resolution - 0.5f;
        // World-space per-side irregularity is only available to the mesh
        // builder. Use its conservative radial envelope for gameplay hits.
        radius = MathF.Min(0.5f, radius * (1f + shape.SpikeIrregularity));
        return dx * dx + dz * dz <= radius * radius;
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
