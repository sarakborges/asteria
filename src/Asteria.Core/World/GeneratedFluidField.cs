namespace Asteria.Core.World;

/// <summary>
/// Immutable generation-time fluid placement policy. It decides which already
/// empty procedural voxels receive authored initial fluid. Runtime fluid
/// simulation remains a separate owner after the chunk becomes resident.
/// </summary>
public sealed class GeneratedFluidField
{
    private readonly OceanRule? _ocean;

    public GeneratedFluidField(
        DimensionDefinition dimension,
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(fluids);

        if (dimension.GeneratedOcean is not { } ocean)
        {
            return;
        }

        var maximumY =
            dimension.Shell?.RoofY is { } roofY
                ? Math.Min(dimension.SeaLevel, roofY - 1)
                : dimension.SeaLevel;
        var minimumY =
            dimension.Shell?.FloorY is { } floorY
                ? checked(floorY + 1)
                : 0;

        _ocean = new OceanRule(
            ocean.Biome,
            fluids.GetId(ocean.Fluid),
            minimumY,
            maximumY);
    }

    public bool HasRules => _ocean is not null;

    public bool TryGetColumnBounds(
        BiomeSample sample,
        int baseSurfaceY,
        out int minimumY,
        out int maximumY)
    {
        if (_ocean is not { } ocean ||
            sample.Primary != ocean.Biome ||
            ocean.MaximumY < ocean.MinimumY ||
            baseSurfaceY >= ocean.MaximumY)
        {
            minimumY = 0;
            maximumY = -1;
            return false;
        }

        minimumY = Math.Max(
            ocean.MinimumY,
            checked(baseSurfaceY + 1));
        maximumY = ocean.MaximumY;
        return minimumY <= maximumY;
    }

    public FluidCell FluidAt(
        BiomeSample sample,
        int baseSurfaceY,
        int worldY,
        double terrainDensity)
    {
        if (terrainDensity >= 0d ||
            !TryGetColumnBounds(
                sample,
                baseSurfaceY,
                out var minimumY,
                out var maximumY) ||
            worldY < minimumY ||
            worldY > maximumY)
        {
            return FluidCell.Empty;
        }

        return FluidCell.Source(_ocean!.Fluid);
    }

    public ChunkSurfaceRange ExpandSurfaceRange(
        SurfaceTerrainColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (_ocean is not { } ocean ||
            ocean.MaximumY < ocean.MinimumY)
        {
            return column.Range;
        }

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                if (column.BiomeAt(x, z).Primary != ocean.Biome ||
                    column.BaseHeightAt(x, z) >= ocean.MaximumY)
                {
                    continue;
                }

                return new ChunkSurfaceRange(
                    column.Range.MinimumWorldY,
                    Math.Max(
                        column.Range.MaximumWorldY,
                        ocean.MaximumY));
            }
        }

        return column.Range;
    }

    private sealed record OceanRule(
        string Biome,
        FluidRuntimeId Fluid,
        int MinimumY,
        int MaximumY);
}
