namespace Asteria.Core.World;

/// <summary>
/// Single authoritative terrain field: a surface crossing plus optional
/// subtractive cave layers and additive biome-authored density formations.
/// Biomes and height are sampled once per X/Z column, never per Y voxel.
/// </summary>
public sealed class SurfaceTerrainField
{
    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly int? _floorY;
    private readonly int? _roofY;
    private readonly BiomeField _surfaceBiomes;
    private readonly VolumeBiomeField _volumeBiomes;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly IReadOnlyDictionary<string, SurfaceTerrainRule> _surfaceRules;
    private readonly IReadOnlyDictionary<string, AdditiveRuleSet> _additiveRules;
    private readonly CoastProfileRule? _coastProfile;
    private readonly CaveRule? _caves;
    private readonly WorldGenerationMode _mode;
    private readonly int _planarSurfaceY;
    private readonly string? _flatOceanBiome;

    public SurfaceTerrainField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeField surfaceBiomes,
        VolumeBiomeField volumeBiomes,
        GeneratedFluidField generatedFluids,
        IEnumerable<BiomeDefinition> surfaceDefinitions,
        IEnumerable<BiomeDefinition> volumeDefinitions,
        bool spawnCaves = true,
        bool spawnOceans = true,
        WorldGenerationMode mode = WorldGenerationMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(
            dimension);
        ArgumentNullException.ThrowIfNull(
            surfaceDefinitions);
        ArgumentNullException.ThrowIfNull(
            volumeDefinitions);
        _surfaceBiomes =
            surfaceBiomes ??
            throw new ArgumentNullException(
                nameof(surfaceBiomes));
        _volumeBiomes =
            volumeBiomes ??
            throw new ArgumentNullException(
                nameof(volumeBiomes));
        _generatedFluids =
            generatedFluids ??
            throw new ArgumentNullException(
                nameof(generatedFluids));

        _seed = seed;
        _mode = mode;
        _seaLevel = dimension.SeaLevel;
        var planarMinimum = Math.Max(1, (dimension.Shell?.FloorY ?? 0) + 2);
        var planarMaximum = dimension.Shell?.RoofY is { } roof
            ? Math.Max(planarMinimum, roof - 2)
            : int.MaxValue;
        _planarSurfaceY = Math.Clamp(
            dimension.SeaLevel, planarMinimum, planarMaximum);
        _flatOceanBiome = spawnOceans
            ? dimension.GeneratedOcean?.Biome : null;
        _floorY = dimension.Shell?.FloorY;
        _roofY = dimension.Shell?.RoofY;
        _surfaceRules =
            surfaceDefinitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        new SurfaceTerrainRule(
                            definition),
                    StringComparer.Ordinal);
        _additiveRules =
            volumeDefinitions
                .Where(
                    definition =>
                        mode == WorldGenerationMode.Normal &&
                        definition.Terrain3d?.Additive.Count > 0)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        new AdditiveRuleSet(
                            definition.Id,
                            definition.Terrain3d!.Additive),
                    StringComparer.Ordinal);
        _coastProfile =
            spawnOceans && dimension.GeneratedOcean is
                { } ocean
                ? new CoastProfileRule(
                    ocean.Biome,
                    ocean.Shore)
                : null;
        _caves =
            mode != WorldGenerationMode.Void &&
            spawnCaves && dimension.Caves is
                { } definition
                ? new CaveRule(
                    definition)
                : null;

        if (_surfaceRules.Count == 0)
        {
            throw new ArgumentException(
                "Surface terrain requires at least one surface biome.",
                nameof(surfaceDefinitions));
        }
    }

    // Reuse the calling materialization's biome assignments for adjacent
    // columns. Each caller owns and discards its sampler with the chunk.
    internal int SampleBaseHeight(
        BiomeField.Sampler sampler,
        int worldX,
        int worldZ) =>
        BaseHeightAt(sampler.Sample(worldX, worldZ), worldX, worldZ);

    internal (BiomeSample Biome, int BaseY) SampleBaseSurface(
        int worldX,
        int worldZ)
    {
        var biome =
            _surfaceBiomes.Sample(
                worldX,
                worldZ);

        return (
            biome,
            BaseHeightAt(
                biome,
                worldX,
                worldZ));
    }

    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth)
        SampleBaseSurfaceWithGeneratedFluid(
            int worldX,
            int worldZ)
    {
        var biome =
            _surfaceBiomes.Sample(
                worldX,
                worldZ);
        var resolved =
            BaseHeightAndSurfaceFluidCutAt(
                biome,
                worldX,
                worldZ);

        return (
            biome,
            resolved.BaseY,
            resolved.SurfaceFluidCutDepth);
    }

    internal BiomeField.Sampler CreateBiomeSampler() =>
        _surfaceBiomes.CreateSampler();

    internal (
        BiomeSample Biome,
        int BaseY,
        int SurfaceFluidCutDepth,
        int SurfaceY,
        BiomeSample? Volume)
        SampleDestinationColumn(
            BiomeField.Sampler sampler,
            int worldX,
            int worldZ,
            bool resolveSurfaceHeight)
    {
        var biome = sampler.Sample(worldX, worldZ);
        var (baseY, cutDepth) =
            BaseHeightAndSurfaceFluidCutAt(biome, worldX, worldZ);
        var volume = _volumeBiomes.SamplePlacement(worldX, worldZ);
        var surfaceY = resolveSurfaceHeight
            ? FinalSurfaceHeight(biome, baseY, worldX, worldZ, volume)
            : baseY;
        return (biome, baseY, cutDepth, surfaceY, volume);
    }

    public int SurfaceHeight(int worldX, int worldZ)
    {
        var biome = _surfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return FinalSurfaceHeight(
            biome,
            baseY,
            worldX,
            worldZ,
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ));
    }

    public double DensityAt(int worldX, int worldY, int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        var biome = _surfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return DensityAt(biome, baseY, worldX, worldY, worldZ);
    }

    public BiomeSample? VolumeBiomeAt(
        int worldX,
        int worldY,
        int worldZ) =>
        _volumeBiomes.Sample(
            worldX,
            worldY,
            worldZ);

    public bool IsCaveVoidAt(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldY));
        }

        if (_caves is null ||
            worldY == _floorY ||
            worldY == _roofY)
        {
            return false;
        }

        var biome =
            _surfaceBiomes.Sample(
                worldX,
                worldZ);
        var baseY =
            BaseHeightAt(
                biome,
                worldX,
                worldZ);

        return IsCaveVoidAt(
            baseY,
            new WorldVoxelCoord(worldX, worldY, worldZ));
    }

    // Caller-sampled base height avoids resampling the 2D biome field at
    // every boundary voxel of a vertical chunk.
    internal bool IsCaveVoidAt(
        int baseY,
        WorldVoxelCoord position) =>
        _caves is not null &&
        position.Y > 0 &&
        position.Y != _floorY &&
        position.Y != _roofY &&
        position.Y <= baseY &&
        CaveVoidDensityAt(
            baseY,
            position.X,
            position.Y,
            position.Z) > 0d;

    /// <summary>
    /// Pure density query with already-sampled X/Z biome and base height.
    /// Used by the chunk materializer across all Y voxels in a column.
    /// </summary>
    public double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ) =>
        DensityAt(
            biome,
            baseY,
            worldX,
            worldY,
            worldZ,
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ));

    internal double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        BiomeSample? volumeBiome)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        double density =
            baseY -
            (double)worldY;

        if (volumeBiome is not null &&
            _additiveRules.TryGetValue(
                volumeBiome.Primary,
                out var floating) &&
            worldY >= floating.MinimumY &&
            worldY <= floating.MaximumY)
        {
            density =
                Math.Max(
                    density,
                    floating.DensityAt(
                        _seed,
                        worldX,
                        worldY,
                        worldZ,
                        volumeBiome.PrimaryWeight));
        }

        if (density >= 0d &&
            worldY <= baseY)
        {
            var voidDensity =
                CaveVoidDensityAt(
                    baseY,
                    worldX,
                    worldY,
                    worldZ);

            if (voidDensity > 0d)
            {
                density =
                    Math.Min(
                        density,
                        -voidDensity);
            }
        }

        return density;
    }

    private double CaveVoidDensityAt(
        int baseY,
        int worldX,
        int worldY,
        int worldZ)
    {
        if (_caves is null ||
            worldY == _floorY ||
            worldY == _roofY)
        {
            return 0d;
        }

        var depth =
            (long)baseY -
            worldY;

        if (depth < _caves.MinimumDepth ||
            depth > _caves.MaximumDepth)
        {
            return 0d;
        }

        return _caves.VoidDensityAt(
            _seed,
            worldX,
            worldY,
            worldZ,
            depth);
    }

    /// <summary>
    /// Number of contiguous additive solid voxels immediately above y,
    /// capped at finite authored material depth. A gap restarts surface
    /// material layering even when a higher floating mass exists.
    /// </summary>
    public uint AdditiveDepthAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ,
        uint finiteDepth)
    {
        var volumeBiome =
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ);
        uint depth = 0;

        while (depth < finiteDepth)
        {
            var above = (long)worldY + depth + 1L;
            if (above > int.MaxValue ||
                DensityAt(
                    biome,
                    baseY,
                    worldX,
                    (int)above,
                    worldZ,
                    volumeBiome) < 0d)
            {
                break;
            }

            depth++;
        }

        return depth;
    }

    public TerrainDensityVolume SampleDensityVolume(
        int originX,
        int originY,
        int originZ,
        int width,
        int height,
        int depth)
    {
        if (originY < 0 ||
            width is < 1 or > Chunk.Size ||
            height is < 1 or > Chunk.Size ||
            depth is < 1 or > Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Density samples must be non-negative-Y and bounded to one chunk.");
        }

        _ = checked(originX + width - 1);
        _ = checked(originY + height - 1);
        _ = checked(originZ + depth - 1);

        var biomes =
            _surfaceBiomes.SampleGrid(
                originX,
                originZ,
                width,
                depth);
        var baseHeights =
            new int[
                checked(width * depth)];

        for (var z = 0; z < depth; z++)
        {
            var worldZ = originZ + z;
            for (var x = 0; x < width; x++)
            {
                var worldX = originX + x;
                baseHeights[
                    z * width +
                    x] =
                    BaseHeightAt(
                        biomes[x, z],
                        worldX,
                        worldZ);
            }
        }

        var volumeBiomes =
            _volumeBiomes.SamplePlacementGrid(
                originX,
                originZ,
                width,
                depth);
        var values =
            new double[
                checked(width * height * depth)];

        for (var z = 0; z < depth; z++)
        {
            var worldZ = originZ + z;
            for (var y = 0; y < height; y++)
            {
                var worldY = originY + y;
                for (var x = 0; x < width; x++)
                {
                    var worldX = originX + x;
                    values[
                        (z * height + y) *
                        width +
                        x] =
                        DensityAt(
                            biomes[x, z],
                            baseHeights[
                                z * width +
                                x],
                            worldX,
                            worldY,
                            worldZ,
                            volumeBiomes[x, z]);
                }
            }
        }

        return new TerrainDensityVolume(
            width,
            height,
            depth,
            values,
            volumeBiomes,
            _volumeBiomes);
    }

    internal TerrainDensityVolume SampleDensityVolume(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ)
    {
        ArgumentNullException.ThrowIfNull(
            column);

        if (originY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(originY));
        }

        // The horizontal placement is immutable and was already sampled
        // with the surface column. Reuse it across vertical chunk layers.
        var volumeBiomes = column.VolumeBiomes;
        var values =
            new double[
                Chunk.Size *
                Chunk.Size *
                Chunk.Size];
        var maximumY = checked(originY + Chunk.Size - 1);

        for (var localZ = 0;
             localZ < Chunk.Size;
             localZ++)
        {
            var worldZ =
                checked(
                    originZ +
                    localZ);

            for (var localX = 0;
                 localX < Chunk.Size;
                 localX++)
            {
                var worldX =
                    checked(originX + localX);
                var baseY =
                    column.BaseHeightAt(localX, localZ);
                var biome =
                    column.BiomeAt(localX, localZ);
                var volumeBiome =
                    volumeBiomes[localX, localZ];

                // Most vertical chunk bands cannot contain caves or
                // additive formations. Their signed density is exactly
                // the base-surface distance; no 3D noise is needed.
                var mayContainCaves =
                    _caves is not null &&
                    (long)originY <= (long)baseY - _caves.MinimumDepth &&
                    (long)maximumY >= (long)baseY - _caves.MaximumDepth;
                var mayContainAdditive =
                    volumeBiome is not null &&
                    _additiveRules.TryGetValue(
                        volumeBiome.Primary,
                        out var additive) &&
                    originY <= additive.MaximumY &&
                    maximumY >= additive.MinimumY;

                for (var localY = 0;
                     localY < Chunk.Size;
                     localY++)
                {
                    var worldY = originY + localY;
                    values[
                        (localZ * Chunk.Size + localY) *
                        Chunk.Size + localX] =
                        mayContainCaves || mayContainAdditive
                            ? DensityAt(
                                biome,
                                baseY,
                                worldX,
                                worldY,
                                worldZ,
                                volumeBiome)
                            : baseY - (double)worldY;
                }
            }
        }

        return new TerrainDensityVolume(
            Chunk.Size,
            Chunk.Size,
            Chunk.Size,
            values,
            volumeBiomes,
            _volumeBiomes);
    }

    public SurfaceTerrainColumn SampleColumn(int chunkX, int chunkZ)
    {
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));
        var biomes = _surfaceBiomes.SampleGrid(
            originX,
            originZ,
            Chunk.Size,
            Chunk.Size);
        var volumeBiomes =
            _volumeBiomes.SamplePlacementGrid(
                originX,
                originZ,
                Chunk.Size,
                Chunk.Size);
        var baseHeights = new int[Chunk.Size * Chunk.Size];
        var surfaceFluidCutDepths =
            new int[baseHeights.Length];
        var surfaceHeights = new int[baseHeights.Length];
        var minimum = int.MaxValue;
        var maximum = int.MinValue;

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                var worldX = checked(originX + x);
                var worldZ = checked(originZ + z);
                var biome = biomes[x, z];
                var resolved =
                    BaseHeightAndSurfaceFluidCutAt(
                        biome,
                        worldX,
                        worldZ);
                var surfaceY = FinalSurfaceHeight(
                    biome,
                    resolved.BaseY,
                    worldX,
                    worldZ,
                    volumeBiomes[x, z]);
                var index = z * Chunk.Size + x;
                baseHeights[index] =
                    resolved.BaseY;
                surfaceFluidCutDepths[index] =
                    resolved.SurfaceFluidCutDepth;
                surfaceHeights[index] = surfaceY;
                minimum = Math.Min(minimum, surfaceY);
                maximum = Math.Max(maximum, surfaceY);
            }
        }

        return new SurfaceTerrainColumn(
            biomes,
            volumeBiomes,
            baseHeights,
            surfaceFluidCutDepths,
            surfaceHeights,
            new ChunkSurfaceRange(minimum, maximum));
    }

    private int BaseHeightAt(
        BiomeSample sample,
        int worldX,
        int worldZ) =>
        BaseHeightAndSurfaceFluidCutAt(
            sample,
            worldX,
            worldZ)
        .BaseY;

    private double HeightOffsetAt(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        // A higher neighboring biome must not raise terrain outside its
        // own boundary. Its slope grows only after it becomes primary.
        var primaryHeight =
            _surfaceRules[sample.Primary].HeightOffsetAt(
                _seed,
                worldX,
                worldZ,
                sample.PrimaryTerrainStrength);
        var lowerHeightSum = 0d;
        var lowerWeightSum = 0d;
        var strongestLowerWeight = 0d;

        foreach (var influence in sample.Influences)
        {
            if (influence.Weight <= 0f ||
                string.Equals(
                    influence.BiomeId,
                    sample.Primary,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // A higher landform starts rising inside its own biome.
            // The ocean contributes its authored shore elevation (not
            // the deep seabed) to the boundary; this does not raise any
            // ocean-owned column to match a neighboring mountain.
            var neighborHeight =
                _coastProfile is { } coast &&
                coast.IsOcean(influence.BiomeId)
                    ? coast.LandBoundaryHeightOffset
                    : _surfaceRules[influence.BiomeId].HeightOffsetAt(
                        _seed, worldX, worldZ, influence.TerrainStrength);
            if (neighborHeight >= primaryHeight)
            {
                continue;
            }

            lowerHeightSum += neighborHeight * influence.Weight;
            lowerWeightSum += influence.Weight;
            strongestLowerWeight = Math.Max(
                strongestLowerWeight,
                influence.Weight);
        }

        if (lowerWeightSum <= 0d)
        {
            return primaryHeight;
        }

        // Equal dominance is the biome boundary: start at the lower
        // terrain there and build the entire uphill grade inside the
        // elevated primary biome, regardless of the height difference.
        var dominance = Math.Clamp(
            sample.PrimaryWeight - strongestLowerWeight,
            0d,
            1d);
        var slope = WorldGenerationEntropy.SmoothStep(dominance);
        var lowerHeight = lowerHeightSum / lowerWeightSum;
        return lowerHeight + (primaryHeight - lowerHeight) * slope;
    }

    private (
        int BaseY,
        int SurfaceFluidCutDepth)
        BaseHeightAndSurfaceFluidCutAt(
            BiomeSample sample,
            int worldX,
            int worldZ)
    {
        if (_mode == WorldGenerationMode.Flat)
        {
            // The surface is planar, but enabling oceans forms shallow flat
            // basins with gradual biome-weighted shores beneath sea level.
            var oceanWeight = _flatOceanBiome is null ? 0d :
                sample.Influences
                    .Where(influence => string.Equals(
                        influence.BiomeId, _flatOceanBiome,
                        StringComparison.Ordinal))
                    .Sum(influence => (double)influence.Weight);
            var depth = checked((int)Math.Round(
                Math.Clamp(oceanWeight, 0d, 1d) * 6d));
            return (Math.Max((_floorY ?? 0) + 1,
                _planarSurfaceY - depth), 0);
        }
        if (_mode == WorldGenerationMode.Void)
            return (-1, 0); // No solid surface; Y voxels still start at zero.

        var offset =
            HeightOffsetAt(
                sample,
                worldX,
                worldZ);

        if (_coastProfile is not null)
        {
            offset =
                _coastProfile.AdjustHeightOffset(
                    sample,
                    offset,
                    _seed,
                    worldX,
                    worldZ,
                    _surfaceRules);
        }

        var height =
            _seaLevel +
            offset;
        var authoredSurfaceY =
            checked(
                (int)Math.Floor(
                    height));
        if (_roofY is
                { } roofY &&
            authoredSurfaceY >=
                roofY)
        {
            authoredSurfaceY =
                roofY -
                1;
        }

        var requestedCutDepth =
            _generatedFluids
                .SurfaceCutDepthAt(
                    sample,
                    worldX,
                    worldZ);
        if (requestedCutDepth <= 0)
        {
            return (
                authoredSurfaceY,
                0);
        }

        var minimumSurfaceY =
            _floorY is
                { } floorY
                ? (long)floorY +
                  1L
                : 0L;
        var availableDepth =
            Math.Max(
                0L,
                (long)authoredSurfaceY -
                minimumSurfaceY);
        var actualCutDepth =
            checked(
                (int)Math.Min(
                    requestedCutDepth,
                    availableDepth));

        return (
            checked(
                authoredSurfaceY -
                actualCutDepth),
            actualCutDepth);
    }

    private int FinalSurfaceHeight(
        BiomeSample surfaceBiome,
        int baseY,
        int worldX,
        int worldZ,
        BiomeSample? volumeBiome)
    {
        if (volumeBiome is null ||
            !_additiveRules.TryGetValue(
                volumeBiome.Primary,
                out var rule) ||
            rule.MaximumY <= baseY ||
            !rule.MayExistAt(
                _seed,
                worldX,
                worldZ,
                volumeBiome.PrimaryWeight))
        {
            return baseY;
        }

        var lowestCandidate =
            rule.MinimumY;
        var highestCandidate =
            Math.Max(
                baseY,
                rule.MaximumY);

        if (_roofY is { } roofY)
        {
            highestCandidate =
                Math.Min(
                    highestCandidate,
                    roofY - 1);
        }

        for (var y = highestCandidate;
             y > baseY &&
             y >= lowestCandidate;
             y--)
        {
            if (DensityAt(
                    surfaceBiome,
                    baseY,
                    worldX,
                    y,
                    worldZ,
                    volumeBiome) >= 0d)
            {
                return y;
            }
        }

        return baseY;
    }

    private sealed class CoastProfileRule
    {
        private readonly string _biome;
        private readonly IReadOnlyList<DimensionShoreSampleDefinition> _samples;

        public CoastProfileRule(
            string biome,
            DimensionShoreProfileDefinition definition)
        {
            _biome = biome;
            _samples = definition.Samples;
            // The two biomes share one authored boundary elevation; never
            // borrow a mountain's potentially unbounded height on the ocean
            // side. Dominance = 0.5 is the ownership boundary.
            LandBoundaryHeightOffset = SampleBoundaryHeight(_samples);
        }

        public double LandBoundaryHeightOffset { get; }

        private static double SampleBoundaryHeight(
            IReadOnlyList<DimensionShoreSampleDefinition> samples)
        {
            for (var i = 1; i < samples.Count; i++)
            {
                if (samples[i].Dominance < 0.5d)
                    continue;
                var left = samples[i - 1];
                var right = samples[i];
                var delta = right.Dominance - left.Dominance;
                var t = delta > 0d
                    ? WorldGenerationEntropy.SmoothStep(
                        (0.5d - left.Dominance) / delta)
                    : 0d;
                return left.MinimumHeight +
                    (right.MinimumHeight - left.MinimumHeight) * t;
            }

            return samples[^1].MinimumHeight;
        }

        public bool IsOcean(string biomeId) =>
            string.Equals(biomeId, _biome, StringComparison.Ordinal);

        public double AdjustHeightOffset(
            BiomeSample sample,
            double rawOffset,
            ulong seed,
            int worldX,
            int worldZ,
            IReadOnlyDictionary<string, SurfaceTerrainRule> terrainRules)
        {
            // The coast belongs entirely to the ocean. A shared transition
            // must never raise (or lower) terrain outside that biome.
            if (!IsOcean(sample.Primary))
            {
                return rawOffset;
            }

            var coastWeight = sample.PrimaryWeight;
            var strongestOther = 0d;

            foreach (var influence in sample.Influences)
            {
                if (!IsOcean(influence.BiomeId))
                    strongestOther = Math.Max(strongestOther, influence.Weight);
            }

            if (strongestOther <= 0d)
                return rawOffset;

            var dominance = coastWeight /
                (coastWeight + strongestOther);
            // Shore height is authored by the Sphere, independent of the
            // elevation of adjoining Plains, Alps or Mountains. High
            // terrain transitions upward only within the high biome.

            // The strictly increasing authored knots cover [0,1].
            // Interpolation is deterministic, allocation-free, and
            // continuous at all profile boundaries.
            for (var i = 1; i < _samples.Count; i++)
            {
                var right = _samples[i];
                if (dominance > right.Dominance)
                {
                    continue;
                }

                var left = _samples[i - 1];
                var t = WorldGenerationEntropy.SmoothStep(
                    (dominance - left.Dominance) /
                    (right.Dominance - left.Dominance));
                var floor = left.MinimumHeight +
                    (right.MinimumHeight - left.MinimumHeight) * t;
                var strength = left.Strength +
                    (right.Strength - left.Strength) * t;
                return rawOffset +
                    (Math.Max(rawOffset, floor) -
                     rawOffset) * strength;
            }

            return rawOffset;
        }
    }

    private sealed class AdditiveRuleSet
    {
        private readonly AdditiveRule[] _rules;

        public AdditiveRuleSet(
            string biomeId,
            IReadOnlyList<BiomeAdditiveDensityDefinition> definitions)
        {
            _rules = definitions
                .Select((definition, index) =>
                    new AdditiveRule(biomeId, index, definition))
                .ToArray();
            MinimumY = _rules.Min(rule => rule.MinimumY);
            MaximumY = _rules.Max(rule => rule.MaximumY);
        }

        public int MinimumY { get; }
        public int MaximumY { get; }

        public bool MayExistAt(
            ulong seed,
            int x,
            int z,
            float influenceWeight)
        {
            foreach (var rule in _rules)
            {
                if (rule.MayExistAt(seed, x, z, influenceWeight))
                {
                    return true;
                }
            }

            return false;
        }

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight)
        {
            var density = double.NegativeInfinity;
            foreach (var rule in _rules)
            {
                if (y >= rule.MinimumY && y <= rule.MaximumY)
                {
                    density = Math.Max(
                        density,
                        rule.DensityAt(seed, x, y, z, influenceWeight));
                }
            }

            return density;
        }
    }

    private sealed class AdditiveRule
    {
        private readonly BiomeAdditiveDensityDefinition _authored;
        private readonly GenerationDomain _maskDomain;
        private readonly GenerationDomain _detailDomain;

        public AdditiveRule(
            string biomeId,
            int index,
            BiomeAdditiveDensityDefinition authored)
        {
            _authored = authored;
            _maskDomain = GenerationDomain.Named(
                $"terrain/density/additive/mask/v1/{biomeId}/{index}");
            _detailDomain = GenerationDomain.Named(
                $"terrain/density/additive/detail/v1/{biomeId}/{index}");
        }

        public int MinimumY => _authored.MinY;
        public int MaximumY => _authored.MaxY;

        public bool MayExistAt(
            ulong seed,
            int x,
            int z,
            float influenceWeight) =>
            Math.Pow(
                HorizontalSupport(seed, x, z),
                _authored.HorizontalFalloff) +
            _authored.Roughness -
            _authored.DensityBias >
            1d - influenceWeight;

        public double DensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            float influenceWeight)
        {
            var center = (_authored.MinY + (double)_authored.MaxY) * 0.5d;
            var halfSpan = (_authored.MaxY - (double)_authored.MinY) * 0.5d;
            var verticalDistance = Math.Abs((y - center) / halfSpan);
            var shape = Math.Pow(
                HorizontalSupport(seed, x, z),
                _authored.HorizontalFalloff) -
                Math.Pow(verticalDistance, _authored.VerticalFalloff);
            var detail = WorldGenerationEntropy.ValueNoise3D(
                seed,
                _detailDomain,
                x,
                y,
                z,
                _authored.DetailScale,
                _authored.DetailScale);
            return (shape +
                    detail * _authored.Roughness -
                    (1d - influenceWeight) -
                    _authored.DensityBias) *
                   _authored.DensityScale;
        }

        private double HorizontalSupport(
            ulong seed,
            int x,
            int z)
        {
            var mask = WorldGenerationEntropy.ValueNoise2D(
                seed,
                _maskDomain,
                x,
                z,
                _authored.HorizontalScale);
            var unit = mask * 0.5d + 0.5d;
            var threshold = 1d - _authored.Coverage;
            return Math.Clamp(
                (unit - threshold) / _authored.Coverage,
                0d,
                1d);
        }
    }

    private sealed class CaveRule
    {
        private readonly CaveLayerRule[] _layers;

        public CaveRule(DimensionCaveDefinition authored)
        {
            _layers = authored.Layers
                .Select((layer, index) => new CaveLayerRule(index, layer))
                .ToArray();
            MinimumDepth = authored.MinimumDepth;
            MaximumDepth = authored.MaximumDepth;
        }

        public uint MinimumDepth { get; }
        public uint MaximumDepth { get; }

        public double VoidDensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            long depth)
        {
            var result = 0d;
            foreach (var layer in _layers)
            {
                if (depth < layer.MinimumDepth ||
                    depth > layer.MaximumDepth)
                {
                    continue;
                }

                result = Math.Max(
                    result,
                    layer.VoidDensityAt(seed, x, y, z, depth));
            }

            return result;
        }
    }

    private sealed class CaveLayerRule
    {
        private readonly DimensionCaveLayerDefinition _authored;
        private readonly CaveNoiseChannel[] _channels;
        private readonly GenerationDomain _chamberDomain;

        public CaveLayerRule(
            int layerIndex,
            DimensionCaveLayerDefinition authored)
        {
            _authored = authored;
            _chamberDomain = GenerationDomain.Named(
                $"terrain/density/caves/layer/v1/{layerIndex}/chambers");
            _channels = authored.Channels
                .Select((channel, index) =>
                    new CaveNoiseChannel(
                        GenerationDomain.Named(
                            $"terrain/density/caves/layer/v1/{layerIndex}/{index}"),
                        channel.HorizontalScale,
                        channel.VerticalScale))
                .ToArray();
        }

        public uint MinimumDepth => _authored.MinDepth;
        public uint MaximumDepth => _authored.MaxDepth;

        public double VoidDensityAt(
            ulong seed,
            int x,
            int y,
            int z,
            long depth)
        {
            var intersection =
                _authored.Combination == CaveNoiseCombination.Intersection;
            var score = intersection ? 0d : double.PositiveInfinity;
            foreach (var channel in _channels)
            {
                var noise = Math.Abs(WorldGenerationEntropy.ValueNoise3D(
                    seed,
                    channel.Domain,
                    x,
                    y,
                    z,
                    channel.HorizontalScale,
                    channel.VerticalScale));
                score = intersection
                    ? Math.Max(score, noise)
                    : Math.Min(score, noise);
            }

            var halfWidth = (double)_authored.NoiseHalfWidth;
            if (_authored.Chambers is { } chambers &&
                score < chambers.MaxNoiseHalfWidth)
            {
                var signal = WorldGenerationEntropy.ValueNoise3D(
                    seed,
                    _chamberDomain,
                    x,
                    y,
                    z,
                    chambers.HorizontalScale,
                    chambers.VerticalScale);
                var strength = Math.Clamp(
                    (signal - chambers.ActivationStart) /
                    (chambers.ActivationFull - chambers.ActivationStart),
                    0d,
                    1d);
                strength = strength * strength * (3d - 2d * strength);
                halfWidth +=
                    (chambers.MaxNoiseHalfWidth - halfWidth) * strength;
            }

            var clearance = halfWidth - score;
            if (clearance <= 0d)
            {
                return 0d;
            }

            var fade = Math.Min(
                ((double)depth - _authored.MinDepth) / _authored.BoundaryFade,
                ((double)_authored.MaxDepth - depth) / _authored.BoundaryFade);
            if (fade <= 0d)
            {
                return 0d;
            }

            return clearance / halfWidth *
                   Math.Min(1d, fade) *
                   _authored.DensityScale;
        }
    }

    private readonly record struct CaveNoiseChannel(
        GenerationDomain Domain,
        uint HorizontalScale,
        uint VerticalScale);
}

/// <summary>
/// Read-only X/Z biome/base/final terrain height snapshot shared by
/// vertical materializations and render queries.
/// </summary>
public sealed class SurfaceTerrainColumn
{
    private readonly BiomeSampleGrid _biomes;
    private readonly VolumeBiomePlacementGrid _volumeBiomes;
    private readonly int[] _baseHeights;
    private readonly int[] _surfaceFluidCutDepths;
    private readonly int[] _surfaceHeights;

    internal SurfaceTerrainColumn(
        BiomeSampleGrid biomes,
        VolumeBiomePlacementGrid volumeBiomes,
        int[] baseHeights,
        int[] surfaceFluidCutDepths,
        int[] surfaceHeights,
        ChunkSurfaceRange range)
    {
        _biomes = biomes;
        _volumeBiomes = volumeBiomes;
        _baseHeights = baseHeights;
        _surfaceFluidCutDepths =
            surfaceFluidCutDepths;
        _surfaceHeights = surfaceHeights;
        Range = range;
    }

    public ChunkSurfaceRange Range { get; }

    internal VolumeBiomePlacementGrid VolumeBiomes => _volumeBiomes;

    public BiomeSample BiomeAt(int localX, int localZ) =>
        _biomes[localX, localZ];

    public int BaseHeightAt(int localX, int localZ) =>
        _baseHeights[Index(localX, localZ)];

    public int SurfaceFluidCutDepthAt(
        int localX,
        int localZ) =>
        _surfaceFluidCutDepths[
            Index(
                localX,
                localZ)];

    public int HeightAt(int localX, int localZ) =>
        _surfaceHeights[Index(localX, localZ)];

    private static int Index(int x, int z)
    {
        if ((uint)x >= Chunk.Size ||
            (uint)z >= Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return z * Chunk.Size + x;
    }
}

/// <summary>
/// Immutable bounded 3D query snapshot with Z/Y/X row major storage.
/// </summary>
public sealed class TerrainDensityVolume
{
    private readonly double[] _densities;
    private readonly VolumeBiomePlacementGrid? _volumeBiomes;
    private readonly VolumeBiomeField? _volumeBiomeField;

    internal TerrainDensityVolume(
        int width,
        int height,
        int depth,
        double[] densities,
        VolumeBiomePlacementGrid? volumeBiomes = null,
        VolumeBiomeField? volumeBiomeField = null)
    {
        if ((volumeBiomes is null) !=
            (volumeBiomeField is null))
        {
            throw new ArgumentException(
                "Volume biome snapshot metadata must be supplied together.");
        }

        Width = width;
        Height = height;
        Depth = depth;
        _densities = densities;
        _volumeBiomes = volumeBiomes;
        _volumeBiomeField = volumeBiomeField;
    }

    public int Width { get; }

    public int Height { get; }

    public int Depth { get; }

    public double DensityAt(
        int localX,
        int localY,
        int localZ)
    {
        if ((uint)localX >= Width ||
            (uint)localY >= Height ||
            (uint)localZ >= Depth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localX));
        }

        return _densities[
            (localZ * Height + localY) *
            Width +
            localX];
    }

    internal BiomeSample? VolumePlacementAt(
        int localX,
        int localZ)
    {
        if (_volumeBiomes is null)
        {
            throw new InvalidOperationException(
                "Density volume does not carry volume-biome placement metadata.");
        }

        return _volumeBiomes[
            localX,
            localZ];
    }

    internal BiomeSample? VolumeBiomeAt(
        int localX,
        int worldY,
        int localZ)
    {
        if (_volumeBiomeField is null)
        {
            throw new InvalidOperationException(
                "Density volume does not carry volume-biome placement metadata.");
        }

        return _volumeBiomeField.SampleAtY(
            VolumePlacementAt(
                localX,
                localZ),
            worldY);
    }
}
