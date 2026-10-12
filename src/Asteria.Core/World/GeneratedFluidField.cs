namespace Asteria.Core.World;

/// <summary>
/// Immutable generation-time fluid placement policy. It owns authored ocean
/// fill, terrain-driven surface fluids and optional bounded local surface
/// fluid patches. Runtime fluid simulation remains the owner after residency.
/// </summary>
public sealed class GeneratedFluidField
{
    private readonly ulong _seed;
    private readonly OceanRule? _ocean;
    private readonly IReadOnlyDictionary<string, SurfaceRule> _surface;
    private readonly HashSet<string> _staticSeaBiomes;
    private readonly SurfaceMosaicField? _mosaic;
    private readonly IReadOnlyDictionary<string, CalderaFillRule> _calderaFills;
    private readonly int _minimumInteriorY;
    private readonly int _maximumInteriorY;

    public GeneratedFluidField(
        ulong seed,
        DimensionDefinition dimension,
        FluidRegistry fluids)
        : this(
            seed,
            dimension,
            fluids,
            Array.Empty<BiomeDefinition>())
    {
    }

    public GeneratedFluidField(
        ulong seed,
        DimensionDefinition dimension,
        FluidRegistry fluids,
        IEnumerable<BiomeDefinition> surfaceDefinitions,
        bool spawnOceans = true,
        bool generatedFluidsEnabled = true,
        SurfaceMosaicField? mosaic = null)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            fluids);
        ArgumentNullException.ThrowIfNull(
            surfaceDefinitions);

        _seed = seed;
        _mosaic = generatedFluidsEnabled ? mosaic : null;
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
            (generatedFluidsEnabled
                ? dimension.GeneratedSurfaceFluids
                : Array.Empty<DimensionGeneratedSurfaceFluidDefinition>())
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

        var definitions =
            surfaceDefinitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();

        _staticSeaBiomes =
            definitions
                .Where(
                    definition =>
                        generatedFluidsEnabled &&
                        definition.SurfaceTerrain?.FillToSeaLevel == true)
                .Select(
                    definition =>
                        definition.Id)
                .ToHashSet(
                    StringComparer.Ordinal);

        _calderaFills =
            definitions
                .Where(definition =>
                    generatedFluidsEnabled &&
                    definition.SurfaceTerrain?.Shape is
                        BiomeCalderaTerrainShapeDefinition { FluidFill: not null })
                .ToDictionary(
                    definition => definition.Id,
                    definition =>
                    {
                        var shape = (BiomeCalderaTerrainShapeDefinition)
                            definition.SurfaceTerrain!.Shape;
                        return new CalderaFillRule(
                            shape,
                            fluids.GetId(shape.FluidFill!.Fluid),
                            dimension.SeaLevel);
                    },
                    StringComparer.Ordinal);

        if (!generatedFluidsEnabled || !spawnOceans || dimension.GeneratedOcean is not
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
        _surface.Count > 0 ||
        _calderaFills.Count > 0 ||
        _mosaic?.HasRules == true;

    public int SurfaceCutDepthAt(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        var cut = _surface.TryGetValue(
                      sample.Primary,
                      out var rule) &&
                  rule.Contains(_seed, worldX, worldZ)
            ? rule.Depth : 0;
        return _mosaic?.Sample(sample, worldX, worldZ).IsFluid == true
            ? Math.Max(1, cut) : cut;
    }

    public bool TryGetColumnBounds(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        out int minimumY,
        out int maximumY) =>
        TryGetColumnBounds(
            sample,
            baseSurfaceY,
            surfaceCutDepth,
            0,
            0,
            out minimumY,
            out maximumY);

    public bool TryGetColumnBounds(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        int worldX,
        int worldZ,
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
            AddBounds(
                checked(
                    baseSurfaceY +
                    1),
                checked(
                    baseSurfaceY +
                    surfaceCutDepth),
                ref minimumY,
                ref maximumY);
        }

        if (surfaceCutDepth > 0 &&
            _mosaic?.Sample(sample, worldX, worldZ).IsFluid == true)
        {
            AddBounds(
                checked(baseSurfaceY + 1),
                checked(baseSurfaceY + 1),
                ref minimumY, ref maximumY);
        }

        if (_ocean is
                { } ocean &&
            UsesStaticSea(
                sample,
                ocean) &&
            baseSurfaceY <
                ocean.MaximumY)
        {
            AddBounds(
                Math.Max(
                    ocean.MinimumY,
                    checked(
                        baseSurfaceY +
                        1)),
                ocean.MaximumY,
                ref minimumY,
                ref maximumY);
        }

        if (_calderaFills.TryGetValue(
                sample.Primary,
                out var caldera))
        {
            caldera.AddBounds(
                sample.PrimaryTerrainStrength,
                baseSurfaceY,
                _minimumInteriorY,
                _maximumInteriorY,
                ref minimumY,
                ref maximumY);
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
        int worldY) =>
        FluidAtEmptyVoxel(
            sample,
            baseSurfaceY,
            surfaceCutDepth,
            0,
            worldY,
            0);

    public FluidCell FluidAtEmptyVoxel(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        int worldX,
        int worldY,
        int worldZ)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        if (worldY <
                _minimumInteriorY ||
            worldY >
                _maximumInteriorY)
        {
            return FluidCell.Empty;
        }

        if (surfaceCutDepth > 0 &&
            _surface.TryGetValue(
                sample.Primary,
                out var local) &&
            worldY >
                baseSurfaceY &&
            (long)worldY <=
                (long)baseSurfaceY +
                surfaceCutDepth)
        {
            return FluidCell.Source(
                local.Fluid);
        }

        if (surfaceCutDepth > 0 &&
            worldY == (long)baseSurfaceY + 1 &&
            _mosaic?.Sample(sample, worldX, worldZ) is
                { IsFluid: true } selected)
            return FluidCell.Source(selected.Fluid);

        if (_calderaFills.TryGetValue(
                sample.Primary,
                out var caldera))
        {
            var fluid = caldera.FluidAt(
                sample.PrimaryTerrainStrength,
                baseSurfaceY,
                worldY);

            if (!fluid.IsEmpty)
            {
                return fluid;
            }
        }

        if (_ocean is not
                { } ocean ||
            !UsesStaticSea(
                sample,
                ocean) ||
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
        FluidAt(
            sample,
            baseSurfaceY,
            surfaceCutDepth,
            0,
            worldY,
            0,
            terrainDensity);

    public FluidCell FluidAt(
        BiomeSample sample,
        int baseSurfaceY,
        int surfaceCutDepth,
        int worldX,
        int worldY,
        int worldZ,
        double terrainDensity) =>
        terrainDensity >= 0d
            ? FluidCell.Empty
            : FluidAtEmptyVoxel(
                sample,
                baseSurfaceY,
                surfaceCutDepth,
                worldX,
                worldY,
                worldZ);

    public ChunkSurfaceRange ExpandSurfaceRange(
        SurfaceTerrainColumn column) =>
        ExpandSurfaceRange(
            column,
            0,
            0);

    public ChunkSurfaceRange ExpandSurfaceRange(
        SurfaceTerrainColumn column,
        int chunkX,
        int chunkZ)
    {
        ArgumentNullException.ThrowIfNull(
            column);

        if (!HasRules)
        {
            return column.Range;
        }

        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(
                    chunkX,
                    0,
                    chunkZ));
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

                if (TryGetColumnBounds(
                        column.BiomeAt(
                            x,
                            z),
                        baseY,
                        cutDepth,
                        checked(
                            originX +
                            x),
                        checked(
                            originZ +
                            z),
                        out _,
                        out var fluidMaximum))
                {
                    maximum =
                        Math.Max(
                            maximum,
                            fluidMaximum);
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

    private bool UsesStaticSea(
        BiomeSample sample,
        OceanRule ocean) =>
        string.Equals(
            sample.Primary,
            ocean.Biome,
            StringComparison.Ordinal) ||
        _staticSeaBiomes.Contains(
            sample.Primary);

    private static void AddBounds(
        int minimum,
        int maximum,
        ref int currentMinimum,
        ref int currentMaximum)
    {
        if (minimum >
            maximum)
        {
            return;
        }

        currentMinimum =
            Math.Min(
                currentMinimum,
                minimum);
        currentMaximum =
            Math.Max(
                currentMaximum,
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

    private sealed class CalderaFillRule
    {
        private readonly double _rimStrength;
        private readonly double _fluidTop;

        public CalderaFillRule(
            BiomeCalderaTerrainShapeDefinition shape,
            FluidRuntimeId fluid,
            int seaLevel)
        {
            _rimStrength = shape.RimStrength;
            _fluidTop = seaLevel + shape.FluidFill!.TopLevel;
            Fluid = fluid;
        }

        public FluidRuntimeId Fluid { get; }

        public void AddBounds(
            float terrainStrength,
            int baseSurfaceY,
            int minimumInteriorY,
            int maximumInteriorY,
            ref int minimumY,
            ref int maximumY)
        {
            if (terrainStrength < _rimStrength)
                return;

            var minimum = Math.Max(minimumInteriorY, checked(baseSurfaceY + 1));
            var maximum = Math.Min(
                maximumInteriorY,
                checked((int)Math.Ceiling(_fluidTop) - 1));
            GeneratedFluidField.AddBounds(
                minimum, maximum, ref minimumY, ref maximumY);
        }

        public FluidCell FluidAt(
            float terrainStrength,
            int baseSurfaceY,
            int worldY)
        {
            if (terrainStrength < _rimStrength ||
                worldY <= baseSurfaceY)
                return FluidCell.Empty;

            var coverage = _fluidTop - worldY;
            if (coverage <= 0d)
                return FluidCell.Empty;

            var level = checked((byte)Math.Clamp(
                (int)Math.Ceiling(
                    Math.Clamp(coverage, 0d, 1d) * FluidCell.MaxLevel),
                FluidCell.MinLevel,
                FluidCell.MaxLevel));
            return FluidCell.Source(Fluid, level);
        }
    }

    private sealed record OceanRule(
        string Biome,
        FluidRuntimeId Fluid,
        int MinimumY,
        int MaximumY);
}
