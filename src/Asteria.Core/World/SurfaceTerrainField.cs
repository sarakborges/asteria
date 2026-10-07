namespace Asteria.Core.World;

/// <summary>
/// Authoritative immutable base-surface height queries.
/// Samples the biome ownership field once per X/Z when evaluating an area.
/// </summary>
public sealed class SurfaceTerrainField
{
    private readonly ulong _seed;
    private readonly int _seaLevel;
    private readonly int? _roofY;
    private readonly BiomeField _biomes;
    private readonly IReadOnlyDictionary<string, TerrainRule> _rules;

    public SurfaceTerrainField(
        ulong seed,
        DimensionDefinition dimension,
        BiomeField biomes,
        IEnumerable<BiomeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(definitions);
        _biomes = biomes ??
            throw new ArgumentNullException(nameof(biomes));

        _seed = seed;
        _seaLevel = dimension.SeaLevel;
        _roofY = dimension.Shell?.RoofY;
        _rules = definitions
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToDictionary(
                definition => definition.Id,
                definition => new TerrainRule(definition),
                StringComparer.Ordinal);

        if (_rules.Count == 0)
        {
            throw new ArgumentException(
                "Surface terrain requires at least one biome.",
                nameof(definitions));
        }
    }

    public int SurfaceHeight(int worldX, int worldZ) =>
        HeightAt(
            _biomes.Sample(worldX, worldZ),
            worldX,
            worldZ);

    public SurfaceTerrainColumn SampleColumn(int chunkX, int chunkZ)
    {
        var (originX, _, originZ) =
            VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));
        var biomes = _biomes.SampleGrid(
            originX,
            originZ,
            Chunk.Size,
            Chunk.Size);
        var heights = new int[Chunk.Size * Chunk.Size];
        var minimum = int.MaxValue;
        var maximum = int.MinValue;

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                var height = HeightAt(
                    biomes[x, z],
                    checked(originX + x),
                    checked(originZ + z));
                heights[z * Chunk.Size + x] = height;
                minimum = Math.Min(minimum, height);
                maximum = Math.Max(maximum, height);
            }
        }

        return new SurfaceTerrainColumn(
            biomes,
            heights,
            new ChunkSurfaceRange(minimum, maximum));
    }

    private int HeightAt(BiomeSample sample, int worldX, int worldZ)
    {
        var height = (double)_seaLevel;
        foreach (var influence in sample.Influences)
        {
            height += _rules[influence.BiomeId].HeightOffsetAt(
                _seed,
                worldX,
                worldZ) * influence.Weight;
        }

        var surfaceY = checked((int)Math.Floor(height));
        return _roofY is { } roofY && surfaceY >= roofY
            ? roofY - 1
            : surfaceY;
    }

    private sealed class TerrainRule
    {
        private readonly BiomeTerrainDefinition _terrain;
        private readonly GenerationDomain _macroDomain;
        private readonly GenerationDomain _detailDomain;

        public TerrainRule(BiomeDefinition definition)
        {
            _terrain = definition.SurfaceTerrain;
            _macroDomain = GenerationDomain.Named(
                $"terrain/base-surface/macro/v1/{definition.Id}");
            _detailDomain = GenerationDomain.Named(
                $"terrain/base-surface/detail/v1/{definition.Id}");
        }

        public double HeightOffsetAt(ulong seed, int x, int z)
        {
            var macro = WorldGenerationEntropy.ValueNoise2D(
                seed,
                _macroDomain,
                x,
                z,
                _terrain.MacroScale);
            var detail = WorldGenerationEntropy.ValueNoise2D(
                seed,
                _detailDomain,
                x,
                z,
                _terrain.DetailScale);

            return _terrain.BaseHeightOffset +
                   macro * _terrain.MacroAmplitude +
                   detail * _terrain.DetailAmplitude;
        }
    }
}

/// <summary>
/// Read-only world-space biome and surface snapshot for one 32x32 X/Z column.
/// May be reused for any vertical chunk with the same X/Z chunk coordinates.
/// </summary>
public sealed class SurfaceTerrainColumn
{
    private readonly BiomeSampleGrid _biomes;
    private readonly int[] _heights;

    internal SurfaceTerrainColumn(
        BiomeSampleGrid biomes,
        int[] heights,
        ChunkSurfaceRange range)
    {
        _biomes = biomes;
        _heights = heights;
        Range = range;
    }

    public ChunkSurfaceRange Range { get; }

    public BiomeSample BiomeAt(int localX, int localZ) =>
        _biomes[localX, localZ];

    public int HeightAt(int localX, int localZ)
    {
        if ((uint)localX >= Chunk.Size ||
            (uint)localZ >= Chunk.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(localX));
        }

        return _heights[localZ * Chunk.Size + localX];
    }
}
