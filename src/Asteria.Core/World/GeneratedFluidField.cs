namespace Asteria.Core.World;

/// <summary>
/// Immutable generation-time fluid placement policy. It owns authored ocean
/// fill and shallow local surface-fluid presence. Terrain consumes only the
/// local cut depth; runtime fluid simulation remains a separate owner after
/// the chunk becomes resident.
/// </summary>
public sealed class GeneratedFluidField
{
    private readonly ulong _seed;
    private readonly OceanRule? _ocean;
    private readonly IReadOnlyDictionary<string, SurfaceRule> _surface;
    private readonly int _minimumInteriorY;
    private readonly int _maximumInteriorY;

    public GeneratedFluidField(
        ulong seed,
        DimensionDefinition dimension,
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            fluids);

        _seed = seed;
        _minimumInteriorY =
            dimension.Shell?.FloorY is { } floorY
                ? checked(
                    floorY +
                    1)
                : 0;
        _maximumInteriorY =
            dimension.Shell?.RoofY is { } roofY
                ? checked(
                    roofY -
                    1)
                : int.MaxValue;

        _surface =
            dimension.GeneratedSurfaceFluids
                .OrderBy(
                    definition =>
                        definition.Biome,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Biome,
                    definition =>
                        new SurfaceRule(
                            definition,
                            fluids.GetId(
                                definition.Fluid)),
                    StringComparer.Ordinal);

        if (dimension.GeneratedOcean is not
            { } ocean)
        {
            return;
        }

        var maximumY =
            Math.Min(
                dimension.SeaLevel,
                _maximumInteriorY);

        _ocean = new OceanRule(
            ocean.Biome,
            fluids.GetId(
                ocean.Fluid),
            _minimumInteriorY,
            maximumY);
    }

    public bool HasRules =>
        _ocean is not null ||
        _surface.Count > 0;

    public int SurfaceCutDepthAt(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        return _surface.TryGetValue(
                   sample.Primary,
                   out var rule) &&
               rule.Contains(
                   _seed,
                   worldX,
                   worldZ)
            ? rule.Depth
            : 0;
    }

    public bool TryGetColumnBounds(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        out int minimumY,
        out int maximumY)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        minimumY =
            int.MaxValue;
        maximumY =
            int.MinValue;

        if (surfaceCutDepth > 0 &&
            _surface.ContainsKey(
                sample.Primary))
        {
            var localMinimum =
                Math.Max(
                    (long)_minimumInteriorY,
                    (long)baseSurfaceY +
                    1L);
            var localMaximum =
                Math.Min(
                    (long)_maximumInteriorY,
                    (long)baseSurfaceY +
                    surfaceCutDepth);

            if (localMinimum <=
                localMaximum)
            {
                minimumY =
                    checked(
                        (int)localMinimum);
                maximumY =
                    checked(
                        (int)localMaximum);
            }
        }

        if (_ocean is
                { } ocean &&
            sample.Primary ==
                ocean.Biome &&
            ocean.MaximumY >=
                ocean.MinimumY &&
            baseSurfaceY <
                ocean.MaximumY)
        {
            var oceanMinimum =
                Math.Max(
                    ocean.MinimumY,
                    checked(
                        baseSurfaceY +
                        1));

            minimumY =
                Math.Min(
                    minimumY,
                    oceanMinimum);
            maximumY =
                Math.Max(
                    maximumY,
                    ocean.MaximumY);
        }

        if (minimumY <=
            maximumY)
        {
            return true;
        }

        minimumY = 0;
        maximumY = -1;
        return false;
    }

    public FluidCell FluidAtEmptyVoxel(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        int worldY)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        if (surfaceCutDepth > 0 &&
            _surface.TryGetValue(
                sample.Primary,
                out var local) &&
            worldY >
                baseSurfaceY &&
            (long)worldY <=
                (long)baseSurfaceY +
                surfaceCutDepth &&
            worldY >=
                _minimumInteriorY &&
            worldY <=
                _maximumInteriorY)
        {
            return FluidCell.Source(
                local.Fluid);
        }

        if (_ocean is not
                { } ocean ||
            sample.Primary !=
                ocean.Biome ||
            worldY <
                ocean.MinimumY ||
            worldY >
                ocean.MaximumY ||
            worldY <=
                baseSurfaceY)
        {
            return FluidCell.Empty;
        }

        return FluidCell.Source(
            ocean.Fluid);
    }

    public FluidCell FluidAt(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        int worldY,
        double terrainDensity) =>
        terrainDensity >= 0d
            ? FluidCell.Empty
            : FluidAtEmptyVoxel(
                sample,
                baseSurfaceY,
                surfaceCutDepth,
                worldY);

    public ChunkSurfaceRange ExpandSurfaceRange(
        SurfaceTerrainColumn column)
    {
        ArgumentNullException.ThrowIfNull(
            column);

        if (!HasRules)
        {
            return column.Range;
        }

        var maximum =
            column.Range.MaximumWorldY;

        for (var z = 0;
             z < Chunk.Size;
             z++)
        {
            for (var x = 0;
                 x < Chunk.Size;
                 x++)
            {
                var baseY =
                    column.BaseHeightAt(
                        x,
                        z);
                var cutDepth =
                    column.SurfaceFluidCutDepthAt(
                        x,
                        z);

                if (cutDepth > 0)
                {
                    maximum =
                        Math.Max(
                            maximum,
                            checked(
                                baseY +
                                cutDepth));
                }

                if (_ocean is
                        { } ocean &&
                    column.BiomeAt(
                            x,
                            z)
                        .Primary ==
                    ocean.Biome &&
                    baseY <
                    ocean.MaximumY)
                {
                    maximum =
                        Math.Max(
                            maximum,
                            ocean.MaximumY);
                }
            }
        }

        return maximum ==
               column.Range.MaximumWorldY
            ? column.Range
            : new ChunkSurfaceRange(
                column.Range.MinimumWorldY,
                maximum);
    }

    private sealed class SurfaceRule
    {
        private readonly int _spacing;
        private readonly long _radius;
        private readonly int _jitter;
        private readonly double _chance;
        private readonly GenerationDomain _presenceDomain;
        private readonly GenerationDomain _jitterXDomain;
        private readonly GenerationDomain _jitterZDomain;

        public SurfaceRule(
            DimensionGeneratedSurfaceFluidDefinition definition,
            FluidRuntimeId fluid)
        {
            Fluid = fluid;
            _spacing =
                definition.Spacing;
            _radius =
                definition.Radius;
            _jitter =
                definition.Jitter;
            _chance =
                definition.Chance;
            Depth =
                definition.Depth;
            _presenceDomain =
                GenerationDomain.Named(
                    $"generated-fluid/surface/presence/v1/{definition.Biome}");
            _jitterXDomain =
                GenerationDomain.Named(
                    $"generated-fluid/surface/jitter-x/v1/{definition.Biome}");
            _jitterZDomain =
                GenerationDomain.Named(
                    $"generated-fluid/surface/jitter-z/v1/{definition.Biome}");
        }

        public FluidRuntimeId Fluid { get; }

        public int Depth { get; }

        public bool Contains(
            ulong seed,
            int worldX,
            int worldZ)
        {
            var centerCellX =
                FloorDiv(
                    worldX,
                    _spacing);
            var centerCellZ =
                FloorDiv(
                    worldZ,
                    _spacing);
            var radiusSquared =
                (Int128)_radius *
                _radius;

            for (var deltaZ = -1;
                 deltaZ <= 1;
                 deltaZ++)
            {
                var cellZLong =
                    (long)centerCellZ +
                    deltaZ;
                if (cellZLong is <
                        int.MinValue or >
                        int.MaxValue)
                {
                    continue;
                }

                var cellZ =
                    (int)cellZLong;

                for (var deltaX = -1;
                     deltaX <= 1;
                     deltaX++)
                {
                    var cellXLong =
                        (long)centerCellX +
                        deltaX;
                    if (cellXLong is <
                            int.MinValue or >
                            int.MaxValue)
                    {
                        continue;
                    }

                    var cellX =
                        (int)cellXLong;
                    if (UnitProbability(
                            WorldGenerationEntropy.Sample2D(
                                seed,
                                _presenceDomain,
                                cellX,
                                cellZ)) >
                        _chance)
                    {
                        continue;
                    }

                    var patchX =
                        (long)cellX *
                        _spacing +
                        _spacing /
                        2L +
                        JitterOffset(
                            WorldGenerationEntropy.Sample2D(
                                seed,
                                _jitterXDomain,
                                cellX,
                                cellZ),
                            _jitter);
                    var patchZ =
                        (long)cellZ *
                        _spacing +
                        _spacing /
                        2L +
                        JitterOffset(
                            WorldGenerationEntropy.Sample2D(
                                seed,
                                _jitterZDomain,
                                cellX,
                                cellZ),
                            _jitter);
                    var deltaWorldX =
                        (Int128)(
                            (long)worldX -
                            patchX);
                    var deltaWorldZ =
                        (Int128)(
                            (long)worldZ -
                            patchZ);

                    if (deltaWorldX *
                            deltaWorldX +
                        deltaWorldZ *
                            deltaWorldZ <=
                        radiusSquared)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            var quotient =
                Math.DivRem(
                    value,
                    divisor,
                    out var remainder);
            return remainder < 0
                ? quotient - 1
                : quotient;
        }

        private static double UnitProbability(
            ulong value) =>
            value /
            (double)ulong.MaxValue;

        private static long JitterOffset(
            ulong value,
            int jitter)
        {
            if (jitter == 0)
            {
                return 0L;
            }

            var span =
                checked(
                    (ulong)(
                        jitter *
                        2 +
                        1));
            return checked(
                (long)(
                    value %
                    span) -
                jitter);
        }
    }

    private sealed record OceanRule(
        string Biome,
        FluidRuntimeId Fluid,
        int MinimumY,
        int MaximumY);
}
