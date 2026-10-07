namespace Asteria.Core.World;

/// <summary>
/// Single authoritative terrain field: a surface crossing plus optional
/// subtractive caves and additive biome-authored floating formations.
/// Biomes and height are sampled once per X/Z column, never per Y voxel.
/// </summary>
public sealed class SurfaceTerrainField
{
    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly int? _roofY;
    private readonly BiomeField _surfaceBiomes;
    private readonly VolumeBiomeField _volumeBiomes;
    private readonly IReadOnlyDictionary<string, SurfaceRule> _surfaceRules;
    private readonly IReadOnlyDictionary<string, FloatingRule> _floatingRules;
    private readonly OceanShoreRule? _oceanShore;
    private readonly CaveRule? _caves;

    public SurfaceTerrainField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeField surfaceBiomes,
        VolumeBiomeField volumeBiomes,
        IEnumerable<BiomeDefinition> surfaceDefinitions,
        IEnumerable<BiomeDefinition> volumeDefinitions)
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

        _seed = seed;
        _seaLevel = dimension.SeaLevel;
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
                        new SurfaceRule(
                            definition),
                    StringComparer.Ordinal);
        _floatingRules =
            volumeDefinitions
                .Where(
                    definition =>
                        definition.Terrain3d?.FloatingFormation is not null)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        new FloatingRule(
                            definition.Id,
                            definition.Terrain3d!.FloatingFormation!),
                    StringComparer.Ordinal);
        _oceanShore =
            dimension.GeneratedOcean is
                { } ocean
                ? new OceanShoreRule(
                    ocean.Biome,
                    ocean.Shore)
                : null;
        _caves =
            dimension.Caves is
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

    public int SurfaceHeight(int worldX, int worldZ)
    {
        var biome = _surfaceBiomes.Sample(worldX, worldZ);
        var baseY = BaseHeightAt(biome, worldX, worldZ);
        return FinalSurfaceHeight(biome, baseY, worldX, worldZ);
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

    /// <summary>
    /// Pure density query with already-sampled X/Z biome and base height.
    /// Used by the chunk materializer across all Y voxels in a column.
    /// </summary>
    public double DensityAt(
        BiomeSample biome,
        int baseY,
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        double density =
            baseY -
            (double)worldY;
        var volumeBiome =
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ);

        if (volumeBiome is not null &&
            _floatingRules.TryGetValue(
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

        if (_caves is not null &&
            density >= 0d &&
            worldY <= baseY)
        {
            var depth = (long)baseY - worldY;
            if (depth >= _caves.MinimumDepth &&
                depth <= _caves.MaximumDepth)
            {
                var voidDensity = _caves.VoidDensityAt(
                    _seed, worldX, worldY, worldZ, depth);
                if (voidDensity > 0d)
                {
                    density = Math.Min(density, -voidDensity);
                }
            }
        }

        return density;
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
                    worldZ) < 0d)
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
        var values = new double[checked(width * height * depth)];

        for (var z = 0; z < depth; z++)
        {
            var worldZ = originZ + z;
            for (var x = 0; x < width; x++)
            {
                var worldX = originX + x;
                var biome = _surfaceBiomes.Sample(worldX, worldZ);
                var baseY = BaseHeightAt(biome, worldX, worldZ);

                for (var y = 0; y < height; y++)
                {
                    values[(z * height + y) * width + x] =
                        DensityAt(
                            biome, baseY, worldX, originY + y, worldZ);
                }
            }
        }

        return new TerrainDensityVolume(width, height, depth, values);
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
        var baseHeights = new int[Chunk.Size * Chunk.Size];
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
                var baseY = BaseHeightAt(biome, worldX, worldZ);
                var surfaceY = FinalSurfaceHeight(
                    biome, baseY, worldX, worldZ);
                var index = z * Chunk.Size + x;
                baseHeights[index] = baseY;
                surfaceHeights[index] = surfaceY;
                minimum = Math.Min(minimum, surfaceY);
                maximum = Math.Max(maximum, surfaceY);
            }
        }

        return new SurfaceTerrainColumn(
            biomes,
            baseHeights,
            surfaceHeights,
            new ChunkSurfaceRange(minimum, maximum));
    }

    private int BaseHeightAt(
        BiomeSample sample,
        int worldX,
        int worldZ)
    {
        var offset = 0d;
        foreach (var influence in sample.Influences)
        {
            offset += _surfaceRules[influence.BiomeId].HeightOffsetAt(
                _seed, worldX, worldZ) * influence.Weight;
        }

        if (_oceanShore is not null)
        {
            offset =
                _oceanShore.AdjustHeightOffset(
                    sample,
                    offset);
        }

        var height =
            _seaLevel +
            offset;
        var surfaceY = checked((int)Math.Floor(height));
        return _roofY is { } roofY && surfaceY >= roofY
            ? roofY - 1
            : surfaceY;
    }

    private int FinalSurfaceHeight(
        BiomeSample surfaceBiome,
        int baseY,
        int worldX,
        int worldZ)
    {
        var volumeBiome =
            _volumeBiomes.SamplePlacement(
                worldX,
                worldZ);

        if (volumeBiome is null ||
            !_floatingRules.TryGetValue(
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
                    worldZ) >= 0d)
            {
                return y;
            }
        }

        return baseY;
    }

    private sealed class OceanShoreRule
    {
        private const double BoundaryDominance = 0.5d;

        private readonly string _biome;
        private readonly DimensionOceanShoreDefinition _definition;

        public OceanShoreRule(
            string biome,
            DimensionOceanShoreDefinition definition)
        {
            _biome = biome;
            _definition = definition;
        }

        public double AdjustHeightOffset(
            BiomeSample sample,
            double rawOffset)
        {
            var oceanWeight = 0d;
            var strongestOther = 0d;

            foreach (var influence in sample.Influences)
            {
                if (string.Equals(
                        influence.BiomeId,
                        _biome,
                        StringComparison.Ordinal))
                {
                    oceanWeight =
                        influence.Weight;
                }
                else
                {
                    strongestOther =
                        Math.Max(
                            strongestOther,
                            influence.Weight);
                }
            }

            if (oceanWeight <= 0d ||
                strongestOther <= 0d)
            {
                return rawOffset;
            }

            var dominance =
                oceanWeight /
                (oceanWeight +
                 strongestOther);
            var deep =
                _definition
                    .DeepWaterStartDominance;
            var shelf =
                _definition
                    .ShelfStartDominance;
            var beach =
                _definition
                    .BeachStartDominance;
            var shelfDepth =
                -(double)_definition
                    .ShelfDepth;
            var beachHeight =
                (double)_definition
                    .BeachHeight;

            if (dominance >= deep)
            {
                return rawOffset;
            }

            if (dominance >= shelf)
            {
                var progress =
                    SmoothRange(
                        deep,
                        shelf,
                        dominance);
                return Lerp(
                    rawOffset,
                    Math.Max(
                        rawOffset,
                        shelfDepth),
                    progress);
            }

            if (dominance >= beach)
            {
                var progress =
                    SmoothRange(
                        shelf,
                        beach,
                        dominance);
                var floor =
                    Lerp(
                        shelfDepth,
                        beachHeight,
                        progress);
                return Math.Max(
                    rawOffset,
                    floor);
            }

            if (dominance >=
                BoundaryDominance)
            {
                return Math.Max(
                    rawOffset,
                    beachHeight);
            }

            var landBeach =
                1d -
                beach;
            var landShelf =
                1d -
                shelf;
            var landDeep =
                1d -
                deep;

            if (dominance >= landBeach)
            {
                return Math.Max(
                    rawOffset,
                    beachHeight);
            }

            if (dominance >= landShelf)
            {
                var progress =
                    WorldGenerationEntropy
                        .SmoothStep(
                            (dominance -
                             landShelf) /
                            (landBeach -
                             landShelf));
                var floor =
                    beachHeight *
                    progress;
                return Math.Max(
                    rawOffset,
                    floor);
            }

            if (dominance <= landDeep)
            {
                return rawOffset;
            }

            var fade =
                WorldGenerationEntropy
                    .SmoothStep(
                        (dominance -
                         landDeep) /
                        (landShelf -
                         landDeep));
            return Lerp(
                rawOffset,
                Math.Max(
                    rawOffset,
                    0d),
                fade);
        }

        private static double SmoothRange(
            double start,
            double end,
            double value) =>
            WorldGenerationEntropy
                .SmoothStep(
                    (start - value) /
                    (start - end));

        private static double Lerp(
            double start,
            double end,
            double amount) =>
            start +
            (end - start) *
            amount;
    }

    private sealed class SurfaceRule
    {
        private readonly BiomeTerrainDefinition _terrain;
        private readonly GenerationDomain _macroDomain;
        private readonly GenerationDomain _detailDomain;

        public SurfaceRule(
            BiomeDefinition definition)
        {
            _terrain =
                definition.SurfaceTerrain ??
                throw new ArgumentException(
                    $"Surface biome {definition.Id} requires surfaceTerrain.");
            _macroDomain =
                GenerationDomain.Named(
                    $"terrain/base-surface/macro/v1/{definition.Id}");
            _detailDomain =
                GenerationDomain.Named(
                    $"terrain/base-surface/detail/v1/{definition.Id}");
        }

        public double HeightOffsetAt(
            ulong seed,
            int x,
            int z)
        {
            var macro =
                WorldGenerationEntropy.ValueNoise2D(
                    seed,
                    _macroDomain,
                    x,
                    z,
                    _terrain.MacroScale);
            var detail =
                WorldGenerationEntropy.ValueNoise2D(
                    seed,
                    _detailDomain,
                    x,
                    z,
                    _terrain.DetailScale);

            return _terrain.BaseHeightOffset +
                   macro *
                   _terrain.MacroAmplitude +
                   detail *
                   _terrain.DetailAmplitude;
        }
    }

    private sealed class FloatingRule
    {
        private readonly BiomeFloatingFormationDefinition _authored;
        private readonly GenerationDomain _maskDomain;
        private readonly GenerationDomain _detailDomain;

        public FloatingRule(
            string biomeId,
            BiomeFloatingFormationDefinition authored)
        {
            _authored = authored;
            _maskDomain = GenerationDomain.Named(
                $"terrain/density/floating/mask/v1/{biomeId}");
            _detailDomain = GenerationDomain.Named(
                $"terrain/density/floating/detail/v1/{biomeId}");
        }

        public int MinimumY => _authored.MinY;
        public int MaximumY => _authored.MaxY;

        public bool MayExistAt(
            ulong seed,
            int x,
            int z,
            float influenceWeight) =>
            HorizontalSupport(seed, x, z) +
            _authored.Roughness >
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
            var shape = HorizontalSupport(seed, x, z) - verticalDistance;
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
                    (1d - influenceWeight)) *
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
        private readonly DimensionCaveDefinition _authored;
        private readonly GenerationDomain _primary =
            GenerationDomain.Named("terrain/density/caves/primary/v1");
        private readonly GenerationDomain _secondary =
            GenerationDomain.Named("terrain/density/caves/secondary/v1");

        public CaveRule(DimensionCaveDefinition authored)
        {
            _authored = authored;
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
            var primary = Math.Abs(WorldGenerationEntropy.ValueNoise3D(
                seed,
                _primary,
                x,
                y,
                z,
                _authored.HorizontalScale,
                _authored.VerticalScale));
            if (primary >= _authored.NoiseHalfWidth)
            {
                return 0d;
            }

            var secondary = Math.Abs(WorldGenerationEntropy.ValueNoise3D(
                seed,
                _secondary,
                x,
                y,
                z,
                _authored.HorizontalScale,
                _authored.VerticalScale));
            var clearance =
                _authored.NoiseHalfWidth - Math.Max(primary, secondary);
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

            return clearance / _authored.NoiseHalfWidth *
                   Math.Min(1d, fade) *
                   _authored.DensityScale;
        }
    }
}

/// <summary>
/// Read-only X/Z biome/base/final terrain height snapshot shared by
/// vertical materializations and render queries.
/// </summary>
public sealed class SurfaceTerrainColumn
{
    private readonly BiomeSampleGrid _biomes;
    private readonly int[] _baseHeights;
    private readonly int[] _surfaceHeights;

    internal SurfaceTerrainColumn(
        BiomeSampleGrid biomes,
        int[] baseHeights,
        int[] surfaceHeights,
        ChunkSurfaceRange range)
    {
        _biomes = biomes;
        _baseHeights = baseHeights;
        _surfaceHeights = surfaceHeights;
        Range = range;
    }

    public ChunkSurfaceRange Range { get; }

    public BiomeSample BiomeAt(int localX, int localZ) =>
        _biomes[localX, localZ];

    public int BaseHeightAt(int localX, int localZ) =>
        _baseHeights[Index(localX, localZ)];

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

    internal TerrainDensityVolume(
        int width,
        int height,
        int depth,
        double[] densities)
    {
        Width = width;
        Height = height;
        Depth = depth;
        _densities = densities;
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    public double DensityAt(int localX, int localY, int localZ)
    {
        if ((uint)localX >= Width ||
            (uint)localY >= Height ||
            (uint)localZ >= Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(localX));
        }

        return _densities[
            (localZ * Height + localY) * Width + localX];
    }
}
