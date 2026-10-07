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
    private readonly SurfaceStructureField _surfaceStructures;
    private readonly VolumeBiomeField _volumeBiomes;
    private readonly UndergroundBiomeField _undergroundBiomes;

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
            biomes,
            StructureRegistry.Empty)
    {
    }

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeRegistry biomes)
        : this(
            seed,
            dimension,
            blocks,
            fluids,
            biomes,
            StructureRegistry.Empty)
    {
    }

    public BiomeWorldGenerator(
        ulong seed,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeRegistry biomes,
        StructureRegistry structures)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(structures);
        biomes.ValidateBlocks(blocks);
        structures.ValidateBlocks(blocks);

        DimensionId =
            dimension.Id;
        var surfaceDefinitions =
            dimension.SurfaceBiomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();
        var volumeDefinitions =
            dimension.VolumeBiomes
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();
        var materialDefinitions =
            surfaceDefinitions
                .Concat(
                    volumeDefinitions)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();

        Biomes =
            new BiomeField(
                seed,
                dimension,
                biomes);
        _volumeBiomes =
            new VolumeBiomeField(
                seed,
                dimension,
                biomes);
        _undergroundBiomes =
            new UndergroundBiomeField(
                seed,
                dimension,
                biomes);
        _terrain =
            new SurfaceTerrainField(
                seed,
                dimension,
                Biomes,
                _volumeBiomes,
                surfaceDefinitions,
                volumeDefinitions);
        _surfaceColumns =
            new SurfaceTerrainColumnCache(
                _terrain);
        _generatedFluids =
            new GeneratedFluidField(
                dimension,
                fluids);
        var materials =
            new BiomeSurfaceMaterialField(
                seed,
                materialDefinitions,
                blocks);
        _surfaceStructures =
            new SurfaceStructureField(
                seed,
                dimension,
                structures,
                blocks,
                Biomes,
                _surfaceColumns,
                materials,
                _generatedFluids);
        var decorations =
            new SurfaceDecorationField(
                seed,
                materialDefinitions,
                blocks);
        _materializer =
            new SurfaceChunkMaterializer(
                _surfaceColumns,
                _terrain,
                materials,
                decorations,
                _generatedFluids,
                _surfaceStructures,
                dimension,
                blocks);
        Tints =
            new BiomeTintField(
                Biomes,
                surfaceDefinitions,
                _surfaceColumns);
    }

    public DimensionId DimensionId { get; }

    public BiomeField Biomes { get; }

    public VolumeBiomeField VolumeBiomes =>
        _volumeBiomes;

    public UndergroundBiomeField UndergroundBiomes =>
        _undergroundBiomes;

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

        var volume =
            _volumeBiomes.Sample(
                worldX,
                worldY,
                worldZ);

        if (volume is not null)
        {
            return volume.Primary;
        }

        if (_terrain.IsCaveVoidAt(
                worldX,
                worldY,
                worldZ))
        {
            var underground =
                _undergroundBiomes.Sample(
                    worldX,
                    worldZ);

            if (underground is not null)
            {
                return underground.Primary;
            }
        }

        return Biomes.Sample(
                worldX,
                worldZ)
            .Primary;
    }

    public bool IsCaveVoidAt(
        int worldX,
        int worldY,
        int worldZ) =>
        _terrain.IsCaveVoidAt(
            worldX,
            worldY,
            worldZ);

    public TerrainDensityVolume SampleDensityVolume(
        int originX, int originY, int originZ,
        int width, int height, int depth) =>
        _terrain.SampleDensityVolume(
            originX, originY, originZ, width, height, depth);

    public ChunkSurfaceRange GetSurfaceRange(
        int chunkX,
        int chunkZ)
    {
        var column =
            _surfaceColumns.Get(
                chunkX,
                chunkZ);
        var withFluid =
            _generatedFluids
                .ExpandSurfaceRange(
                    column);

        return _surfaceStructures
            .ExpandSurfaceRange(
                withFluid,
                chunkX,
                chunkZ);
    }
}
