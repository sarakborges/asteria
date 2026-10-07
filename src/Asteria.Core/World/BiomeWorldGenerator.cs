namespace Asteria.Core.World;

/// <summary>
/// Immutable world-generation composition root. Biome placement, terrain,
/// materials, decorations and chunk materialization own their own decisions.
/// </summary>
public sealed class BiomeWorldGenerator :
    IChunkProvider,
    IChunkSurfaceRangeProvider
{
    private readonly SurfaceTerrainField _terrain;
    private readonly SurfaceTerrainColumnCache _surfaceColumns;
    private readonly SurfaceChunkMaterializer _materializer;

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(biomes);
        biomes.ValidateBlocks(blocks);

        DimensionId = dimension.Id;
        var activeBiomes = dimension.Biomes
            .Select(biomes.Get)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        Biomes = new BiomeField(seed, dimension, biomes);
        _terrain = new SurfaceTerrainField(
            seed, dimension, Biomes, activeBiomes);
        _surfaceColumns = new SurfaceTerrainColumnCache(_terrain);
        var materials = new BiomeSurfaceMaterialField(
            seed, activeBiomes, blocks);
        var decorations = new SurfaceDecorationField(
            seed, activeBiomes, blocks);
        _materializer = new SurfaceChunkMaterializer(
            _surfaceColumns,
            materials,
            decorations,
            dimension,
            blocks);
        Tints = new BiomeTintField(
            Biomes, activeBiomes, _surfaceColumns);
    }

    public DimensionId DimensionId { get; }

    public BiomeField Biomes { get; }

    public BiomeTintField Tints { get; }

    public Chunk Materialize(ChunkCoord coord) =>
        _materializer.Materialize(coord);

    public int SurfaceHeight(int worldX, int worldZ) =>
        _terrain.SurfaceHeight(worldX, worldZ);

    public ChunkSurfaceRange GetSurfaceRange(int chunkX, int chunkZ) =>
        _surfaceColumns.Get(chunkX, chunkZ).Range;
}
