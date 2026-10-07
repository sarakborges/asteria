namespace Asteria.Core.World;

public readonly record struct GeneratedSurfaceDestination(
    int X,
    int Y,
    int Z);

/// <summary>
/// Query-only destination preparation for untouched/generated world space.
/// This capability never materializes chunks or inspects mutable runtime state.
/// </summary>
internal sealed class GeneratedSurfaceDestinationQuery
{
    private readonly SurfaceTerrainField _terrain;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly SurfaceStructureField _structures;
    private readonly int? _floorY;
    private readonly int? _roofY;

    public GeneratedSurfaceDestinationQuery(
        DimensionDefinition dimension,
        SurfaceTerrainField terrain,
        GeneratedFluidField generatedFluids,
        SurfaceStructureField structures)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        _terrain =
            terrain ??
            throw new ArgumentNullException(
                nameof(terrain));
        _generatedFluids =
            generatedFluids ??
            throw new ArgumentNullException(
                nameof(generatedFluids));
        _structures =
            structures ??
            throw new ArgumentNullException(
                nameof(structures));
        _floorY =
            dimension.Shell?.FloorY;
        _roofY =
            dimension.Shell?.RoofY;
    }

    public GeneratedSurfaceDestination?
        Find(
            int preferredX,
            int preferredZ,
            int maxRadius,
            Func<int, int, bool>? acceptsColumn = null)
    {
        if (maxRadius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRadius),
                "Destination search radius must be non-negative.");
        }

        var searchBounds =
            SearchBounds(
                preferredX,
                preferredZ,
                maxRadius);
        var structureBounds =
            _structures
                .PlacementsIntersecting(
                    searchBounds.MinimumX,
                    searchBounds.MinimumZ,
                    searchBounds.Width,
                    searchBounds.Depth)
                .Select(result =>
                    (
                        result.MinimumX,
                        result.MaximumX,
                        result.MinimumZ,
                        result.MaximumZ))
                .ToArray();

        foreach (var column in
                 SquareRings(
                     preferredX,
                     preferredZ,
                     maxRadius))
        {
            if (acceptsColumn is not null &&
                !acceptsColumn(
                    column.X,
                    column.Z))
            {
                continue;
            }

            if (structureBounds.Any(structure =>
                    column.X >=
                        structure.MinimumX &&
                    column.X <=
                        structure.MaximumX &&
                    column.Z >=
                        structure.MinimumZ &&
                    column.Z <=
                        structure.MaximumZ))
            {
                continue;
            }

            var destination =
                GeneratedSurfaceFeetAt(
                    column.X,
                    column.Z);
            if (destination is not null)
            {
                return destination;
            }
        }

        return null;
    }

    private GeneratedSurfaceDestination?
        GeneratedSurfaceFeetAt(
            int worldX,
            int worldZ)
    {
        var surfaceY =
            _terrain.SurfaceHeight(
                worldX,
                worldZ);
        if (surfaceY < 0)
        {
            return null;
        }

        var feetYLong =
            (long)surfaceY +
            1L;
        var headYLong =
            feetYLong +
            1L;
        if (headYLong >
                int.MaxValue ||
            feetYLong <
                0L)
        {
            return null;
        }

        var feetY =
            (int)feetYLong;
        var headY =
            (int)headYLong;

        if (_floorY is
                { } floorY &&
            surfaceY <=
                floorY)
        {
            return null;
        }

        if (_roofY is
                { } roofY &&
            (feetY >=
                 roofY ||
             headY >=
                 roofY))
        {
            return null;
        }

        if (_terrain.DensityAt(
                worldX,
                surfaceY,
                worldZ) <
            0d ||
            _terrain.DensityAt(
                worldX,
                feetY,
                worldZ) >=
            0d ||
            _terrain.DensityAt(
                worldX,
                headY,
                worldZ) >=
            0d)
        {
            return null;
        }

        var surface =
            _terrain
                .SampleBaseSurfaceWithGeneratedFluid(
                    worldX,
                    worldZ);

        if (!_generatedFluids
                .FluidAtEmptyVoxel(
                    surface.Biome,
                    surface.BaseY,
                    surface.SurfaceFluidCutDepth,
                    feetY)
                .IsEmpty ||
            !_generatedFluids
                .FluidAtEmptyVoxel(
                    surface.Biome,
                    surface.BaseY,
                    surface.SurfaceFluidCutDepth,
                    headY)
                .IsEmpty)
        {
            return null;
        }

        return new GeneratedSurfaceDestination(
            worldX,
            feetY,
            worldZ);
    }

    private static (
        int MinimumX,
        int MinimumZ,
        int Width,
        int Depth)
        SearchBounds(
            int centerX,
            int centerZ,
            int radius)
    {
        var minimumX =
            Math.Max(
                (long)int.MinValue,
                (long)centerX -
                radius);
        var maximumX =
            Math.Min(
                (long)int.MaxValue,
                (long)centerX +
                radius);
        var minimumZ =
            Math.Max(
                (long)int.MinValue,
                (long)centerZ -
                radius);
        var maximumZ =
            Math.Min(
                (long)int.MaxValue,
                (long)centerZ +
                radius);
        var width =
            maximumX -
            minimumX +
            1L;
        var depth =
            maximumZ -
            minimumZ +
            1L;

        if (width >
                int.MaxValue ||
            depth >
                int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Destination search area is too large.");
        }

        return (
            (int)minimumX,
            (int)minimumZ,
            (int)width,
            (int)depth);
    }

    private static IEnumerable<(int X, int Z)>
        SquareRings(
            int centerX,
            int centerZ,
            int maxRadius)
    {
        yield return (
            centerX,
            centerZ);

        for (var ring = 1;
             ring <= maxRadius;
             ring++)
        {
            for (var dx = -ring;
                 dx <= ring;
                 dx++)
            {
                if (TryOffset(
                        centerX,
                        centerZ,
                        dx,
                        -ring,
                        out var point))
                {
                    yield return point;
                }
            }

            for (var dz = -ring + 1;
                 dz <= ring;
                 dz++)
            {
                if (TryOffset(
                        centerX,
                        centerZ,
                        ring,
                        dz,
                        out var point))
                {
                    yield return point;
                }
            }

            for (var dx = ring - 1;
                 dx >= -ring;
                 dx--)
            {
                if (TryOffset(
                        centerX,
                        centerZ,
                        dx,
                        ring,
                        out var point))
                {
                    yield return point;
                }
            }

            for (var dz = ring - 1;
                 dz >= -ring + 1;
                 dz--)
            {
                if (TryOffset(
                        centerX,
                        centerZ,
                        -ring,
                        dz,
                        out var point))
                {
                    yield return point;
                }
            }
        }
    }

    private static bool TryOffset(
        int centerX,
        int centerZ,
        int offsetX,
        int offsetZ,
        out (int X, int Z) point)
    {
        var x =
            (long)centerX +
            offsetX;
        var z =
            (long)centerZ +
            offsetZ;
        if (x is <
                int.MinValue or >
                int.MaxValue ||
            z is <
                int.MinValue or >
                int.MaxValue)
        {
            point =
                default;
            return false;
        }

        point =
            (
                (int)x,
                (int)z);
        return true;
    }
}
