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

    private readonly DimensionDefinition _dimension;
    private readonly WorldGenerationOptions _generation;
    private readonly VoidSpawnPlatform? _voidSpawnPlatform;
    private readonly SurfaceTerrainField _terrain;
    private readonly SurfaceTerrainColumnCache _surfaceColumns;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly GeneratedSurfaceDestinationQuery _destinations;
    private readonly SurfaceChunkMaterializer _materializer;
    private readonly SurfaceStructureField _surfaceStructures;
    private readonly VolumeBiomeField _volumeBiomes;

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
        StructureRegistry structures,
        StructureSetRegistry? structureSets = null,
        WorldGenerationOptions? generation = null)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(structures);
        structureSets ??=
            StructureSetRegistry.Empty;
        generation ??= new WorldGenerationOptions();
        biomes.ValidateBlocks(blocks);
        biomes.ValidateFluids(fluids);
        structures.ValidateBlocks(blocks);
        structures.ValidateFluids(fluids);
        structureSets.ValidateStructures(
            structures);

        _dimension =
            dimension;
        DimensionId =
            dimension.Id;
        var surfaceIds = dimension.SurfaceBiomes
            .Where(id => generation.SpawnOceans ||
                !string.Equals(id, dimension.GeneratedOcean?.Biome,
                    StringComparison.Ordinal))
            .ToArray();
        if (generation.SpawnBiome is { } requestedBiome &&
            generation.SingleBiome &&
            !surfaceIds.Contains(requestedBiome, StringComparer.Ordinal))
            throw new ArgumentException(
                "Single Biome is not authored in this Sphere.",
                nameof(generation));
        if (generation.SingleBiome)
            surfaceIds = [generation.SpawnBiome!];
        if (surfaceIds.Length == 0)
            throw new ArgumentException(
                "No surface biomes remain with this generation configuration.",
                nameof(generation));

        var surfaceDefinitions =
            surfaceIds
                .Select(
                    biomes.Get)
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();
        _voidSpawnPlatform = generation.Mode == WorldGenerationMode.Void
            ? new VoidSpawnPlatform(dimension, blocks, surfaceDefinitions[0])
            : null;
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
                .Concat(volumeDefinitions)
                .OrderBy(definition => definition.Id, StringComparer.Ordinal)
                .ToArray();
        var decorationDefinitions = materialDefinitions;

        _generation = generation;
        Biomes =
            new BiomeField(
                seed,
                surfaceIds,
                biomes,
                blending: dimension.BiomeBlending,
                biomeSizeMultiplier: generation.BiomeSizeMultiplier);
        _volumeBiomes =
            new VolumeBiomeField(
                seed,
                dimension,
                biomes);
        // One immutable sample owner feeds block, fluid and surface-cut
        // decisions. Flat/Void modes deliberately bypass the mosaic.
        var mosaic = generation.Mode == WorldGenerationMode.Normal
            ? new SurfaceMosaicField(seed, surfaceDefinitions, blocks, fluids)
            : null;
        _generatedFluids =
            new GeneratedFluidField(
                seed,
                dimension,
                fluids,
                surfaceDefinitions,
                spawnOceans: generation.SpawnOceans,
                generatedFluidsEnabled: generation.Mode != WorldGenerationMode.Void,
                mosaic: mosaic);
        _terrain =
            new SurfaceTerrainField(
                seed,
                dimension,
                Biomes,
                _volumeBiomes,
                _generatedFluids,
                surfaceDefinitions,
                volumeDefinitions,
                spawnCaves: generation.SpawnCaves,
                spawnOceans: generation.SpawnOceans,
                mode: generation.Mode);
        _surfaceColumns =
            new SurfaceTerrainColumnCache(
                _terrain);
        var materials =
            new BiomeSurfaceMaterialField(
                seed,
                materialDefinitions,
                blocks,
                _terrain,
                mosaic);
        var habitats = new SurfaceHabitatField(seed, surfaceDefinitions);
        _surfaceStructures =
            new SurfaceStructureField(
                seed,
                dimension,
                structures,
                structureSets,
                blocks,
                fluids,
                Biomes,
                _terrain,
                materials,
                _generatedFluids,
                habitats,
                spawnStructures: generation.SpawnStructures &&
                    generation.Mode != WorldGenerationMode.Void);
        _destinations =
            new GeneratedSurfaceDestinationQuery(
                dimension,
                _terrain,
                _generatedFluids,
                _surfaceStructures);
        var decorations =
            new SurfaceDecorationField(
                seed,
                decorationDefinitions,
                blocks,
                _terrain,
                habitats,
                generateDecorations: generation.Mode != WorldGenerationMode.Void,
                fluids: fluids);
        var volumeStructures = new VolumeStructureField(
            seed, volumeDefinitions, structures, blocks,
            _terrain, _volumeBiomes, materials,
            enabled: generation.SpawnStructures &&
                     generation.Mode == WorldGenerationMode.Normal);
        var caveSpikes = new CaveSpikeField(
            seed, volumeDefinitions.Where(biome =>
                biome.VolumeLayout!.Placement == VolumeBiomePlacement.CarvedVoid),
            blocks, _terrain);
        _materializer =
            new SurfaceChunkMaterializer(
                _surfaceColumns,
                _terrain,
                materials,
                decorations,
                _generatedFluids,
                _surfaceStructures,
                dimension,
                blocks,
                _volumeBiomes,
                _voidSpawnPlatform,
                caveSpikes,
            volumeStructures);
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

    public BiomeTintField Tints { get; }

    public GeneratedSurfaceDestination?
        FindGeneratedSpawn(
            int maxBiomeDistance,
            int maxLocalRadius)
    {
        if (_voidSpawnPlatform is not null)
            return _voidSpawnPlatform.Spawn;

        var spawn =
            _dimension.Spawn;
        var targetBiome =
            _generation.SpawnBiome is { } chosen &&
            _dimension.SurfaceBiomes.Contains(chosen, StringComparer.Ordinal)
                ? chosen
                : Biomes.SelectSpawnBiome(
                    _generation.SpawnOceans
                        ? _dimension.GeneratedOcean?.Biome : null);

        if (targetBiome is not null)
        {
            var target =
                Biomes.FindNearestSurfaceBiome(
                    targetBiome,
                    spawn.X,
                    spawn.Z,
                    maxBiomeDistance);

            if (target is not null)
            {
                var preferred =
                    _destinations.Find(
                        target.X,
                        target.Z,
                        maxLocalRadius,
                        (x, z) =>
                            string.Equals(
                                Biomes.Sample(
                                        x,
                                        z)
                                    .Primary,
                                targetBiome,
                                StringComparison.Ordinal));

                if (preferred is not null)
                {
                    return preferred;
                }
            }
        }

        return _destinations.Find(
            spawn.X,
            spawn.Z,
            maxLocalRadius);
    }

    public SurfaceBiomeSearchResult?
        FindNearestSurfaceBiome(
            string biomeId,
            int originX,
            int originZ,
            int maxDistance) =>
        Biomes.FindNearestSurfaceBiome(
            biomeId,
            originX,
            originZ,
            maxDistance);

    public GeneratedSurfaceDestination?
        FindGeneratedSurfaceDestination(
            int preferredX,
            int preferredZ,
            int maxRadius,
            Func<int, int, bool>? acceptsColumn = null) =>
        _voidSpawnPlatform is { } platform
            ? platform.IsNear(preferredX, preferredZ, maxRadius) &&
              (acceptsColumn is null || acceptsColumn(platform.Spawn.X, platform.Spawn.Z))
                ? platform.Spawn : null
            : _destinations.Find(
                preferredX,
                preferredZ,
                maxRadius,
                acceptsColumn);

    public GeneratedSurfaceDestination?
        FindGeneratedDestination(
            int preferredX,
            int preferredY,
            int preferredZ,
            int maxRadius,
            Func<int, int, bool>? acceptsColumn = null) =>
        _voidSpawnPlatform is { } platform
            ? platform.IsNear(preferredX, preferredZ, maxRadius) &&
              (acceptsColumn is null || acceptsColumn(platform.Spawn.X, platform.Spawn.Z))
                ? platform.Spawn : null
            : _destinations.FindNear(
                preferredX,
                preferredY,
                preferredZ,
                maxRadius,
                acceptsColumn);

    internal bool TryPrepareManualStructure(
        string reference, int? variation,
        int anchorX, int anchorZ,
        ManualStructurePlacementLedger committed,
        out ManualStructurePlacementPlan? plan) =>
        _surfaceStructures.TryPrepareManualPlacement(
            reference, variation, anchorX, anchorZ, committed, out plan);

    public bool HasGeneratedSurfaceStructure(string reference) =>
        _surfaceStructures.HasReference(reference);

    public IReadOnlyList<SurfaceStructureQueryResult>
        SurfaceStructuresIntersecting(
            int originX,
            int originZ,
            int width,
            int depth) =>
        _surfaceStructures.PlacementsIntersecting(
            originX,
            originZ,
            width,
            depth);

    public SurfaceStructureQueryResult?
        FindNearestSurfaceStructure(
            string reference,
            int originX,
            int originZ,
            int maxDistance,
            string? structureId = null) =>
        _surfaceStructures.FindNearest(
            reference,
            originX,
            originZ,
            maxDistance,
            structureId);

    public Chunk Materialize(ChunkCoord coord) =>
        _materializer.Materialize(coord);

    public int SurfaceHeight(int worldX, int worldZ) =>
        _voidSpawnPlatform?.SurfaceHeight(worldX, worldZ) ??
        _terrain.SurfaceHeight(worldX, worldZ);

    public double DensityAt(int worldX, int worldY, int worldZ) =>
        _voidSpawnPlatform?.IsSolidAt(worldX, worldY, worldZ) == true
            ? 1d : _terrain.DensityAt(worldX, worldY, worldZ);

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
            var cave = _volumeBiomes.SampleCave(worldX, worldZ);
            if (cave is not null)
                return cave.Primary;
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
        if (_voidSpawnPlatform is not null)
            return _voidSpawnPlatform.SurfaceRange(chunkX, chunkZ);

        var column =
            _surfaceColumns.Get(
                chunkX,
                chunkZ);
        var withFluid =
            _generatedFluids
                .ExpandSurfaceRange(
                    column,
                    chunkX,
                    chunkZ);

        return _surfaceStructures
            .ExpandSurfaceRange(
                withFluid,
                chunkX,
                chunkZ);
    }
}
