namespace Asteria.Core.World;

/// <summary>
/// Immutable world-generation composition root. Biome placement, terrain,
/// materials, decorations and chunk materialization own their own decisions.
/// </summary>
public sealed class BiomeWorldGenerator :
    IChunkProvider,
    IChunkSurfaceRangeProvider
{
    private static readonly FluidRegistry EmptyFluids =
        new(Array.Empty<FluidDefinition>());

    private readonly SurfaceTerrainField _terrain;
    private readonly SurfaceTerrainColumnCache _surfaceColumns;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly SurfaceChunkMaterializer _materializer;
    private readonly BiomeField _surfaceBiomeQuery;
    private readonly IReadOnlyDictionary<string, BiomeFloatingFormationDefinition>
        _volumeBiomes;

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        BiomeRegistry biomes)
        : this(
            seed,
            dimension,
            blocks,
            EmptyFluids,
            biomes)
    {
    }

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(biomes);
        biomes.ValidateBlocks(blocks);

        DimensionId = dimension.Id;
        var activeBiomes = dimension.Biomes
            .Select(biomes.Get)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        Biomes = new BiomeField(seed, dimension, biomes);

        var volumeBiomes =
            activeBiomes
                .Where(definition =>
                    definition.Terrain3d?.FloatingFormation is not null)
                .ToArray();
        var surfaceBiomeIds =
            activeBiomes
                .Except(volumeBiomes)
                .Select(definition =>
                    definition.Id)
                .ToArray();

        _surfaceBiomeQuery =
            surfaceBiomeIds.Length > 0
                ? new BiomeField(
                    seed,
                    surfaceBiomeIds,
                    biomes)
                : Biomes;
        _volumeBiomes =
            volumeBiomes.ToDictionary(
                definition =>
                    definition.Id,
                definition =>
                    definition.Terrain3d!.FloatingFormation!,
                StringComparer.Ordinal);

        _terrain = new SurfaceTerrainField(
            seed, dimension, Biomes, activeBiomes);
        _surfaceColumns = new SurfaceTerrainColumnCache(_terrain);
        _generatedFluids =
            new GeneratedFluidField(
                dimension,
                fluids);
        var materials = new BiomeSurfaceMaterialField(
            seed, activeBiomes, blocks);
        var decorations = new SurfaceDecorationField(
            seed, activeBiomes, blocks);
        _materializer = new SurfaceChunkMaterializer(
            _surfaceColumns,
            _terrain,
            materials,
            decorations,
            _generatedFluids,
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

    public double DensityAt(int worldX, int worldY, int worldZ) =>
        _terrain.DensityAt(worldX, worldY, worldZ);

    public string EffectiveBiomeAt(
        int worldX,
        int worldY,
        int worldZ)
    {
        if (worldY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldY));
        }

        var generated =
            Biomes.Sample(
                worldX,
                worldZ);

        if (_volumeBiomes.TryGetValue(
                generated.Primary,
                out var volume) &&
            worldY >= volume.MinY &&
            worldY <= volume.MaxY)
        {
            return generated.Primary;
        }

        return _surfaceBiomeQuery
            .Sample(
                worldX,
                worldZ)
            .Primary;
    }

    public TerrainDensityVolume SampleDensityVolume(
        int originX, int originY, int originZ,
        int width, int height, int depth) =>
        _terrain.SampleDensityVolume(
            originX, originY, originZ, width, height, depth);

    public ChunkSurfaceRange GetSurfaceRange(int chunkX, int chunkZ) =>
        _generatedFluids.ExpandSurfaceRange(
            _surfaceColumns.Get(
                chunkX,
                chunkZ));
}
