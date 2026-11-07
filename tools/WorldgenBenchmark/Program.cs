using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Asteria.Core.Content;
using Asteria.Core.Diagnostics;
using Asteria.Core.World;

var options = Arguments.Parse(args);
var selection = new PackSelection(options.Pack);
var root = Path.Combine(options.ProjectRoot, "packs", selection.Name, "data");

static IEnumerable<string> Documents(string root, string kind) =>
    Directory.EnumerateFiles(
            Path.Combine(root, kind),
            "*.json")
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(File.ReadAllText);

static IEnumerable<string> OptionalDocuments(
    string root,
    string kind)
{
    var directory =
        Path.Combine(
            root,
            kind);

    return Directory.Exists(
            directory)
        ? Directory
            .EnumerateFiles(
                directory,
                "*.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText)
        : Array.Empty<string>();
}

var blocks = BlockRegistry.FromJson(Documents(root, "blocks"));
var fluids = FluidRegistry.FromJson(Documents(root, "fluids"));
var sourceBiomeDefinitions = Documents(root, "biomes")
    .Select(BiomeDefinitionJson.Parse)
    .ToArray();
var biomes = new BiomeRegistry(sourceBiomeDefinitions);
var structures = StructureRegistry.FromJson(OptionalDocuments(root, "structures"));
var structureSets =
    StructureSetRegistry.FromJson(
        OptionalDocuments(
            root,
            "structure_sets"));
var dimensions = DimensionRegistry.FromJson(Documents(root, "dimensions"));
dimensions.ValidateBlocks(blocks);
dimensions.ValidateFluids(fluids);
dimensions.ValidateBiomes(biomes);
dimensions.ValidateStructures(
    structures,
    structureSets);
structureSets.ValidateStructures(
    structures);
biomes.ValidateBlocks(blocks);
structures.ValidateBlocks(blocks);
structures.ValidateFluids(fluids);

var dimension = dimensions.Get(new DimensionId(options.Dimension));
var dimensionSeed = DimensionSeed.Derive(options.Seed, dimension.Id);
BiomeWorldGenerator NewGenerator(
    WorldGenerationOptions? generation = null,
    BiomeRegistry? biomeRegistry = null) => new(
    dimensionSeed,
    dimension,
    blocks,
    fluids,
    biomeRegistry ?? biomes,
    structures,
    structureSets,
    generation);

var targetProbeGenerator =
    NewGenerator();
var centerBiome =
    targetProbeGenerator.Biomes.Sample(
        options.CenterX,
        options.CenterZ)
    .Primary;
var biomeSearchTarget =
    dimension.SurfaceBiomes
        .OrderBy(
            id => id,
            StringComparer.Ordinal)
        .FirstOrDefault(id =>
            !string.Equals(
                id,
                centerBiome,
                StringComparison.Ordinal)) ??
    centerBiome;
var structureSearchTarget =
    dimension.GeneratedSurfaceStructures
        .Select(definition =>
            definition.Structure)
        .OrderBy(
            reference => reference,
            StringComparer.Ordinal)
        .FirstOrDefault();
var farX =
    OffsetWorldAxis(
        options.CenterX,
        1_000_000);
var farZ =
    OffsetWorldAxis(
        options.CenterZ,
        -1_000_000);
var searchRadius =
    options.SearchRadius;

var metrics = new SortedDictionary<string, Measurement>(
    StringComparer.Ordinal);

var initialAreaDiagnostics = new
{
    SpawnX = 0,
    SpawnY = 0,
    SpawnZ = 0,
    CenterChunkX = 0,
    CenterChunkY = 0,
    CenterChunkZ = 0,
    DesiredChunkCount = 0,
    SurfaceColumnCount = 0,
    ChunksByY = new SortedDictionary<int, int>(),
};

object? coldMaterializationDiagnostics = null;
object? warmMaterializationDiagnostics = null;
object? coldInitialAreaTimingDiagnostics = null;
var warmInitialAreaTimingDiagnostics = new List<object>();
var initialAreaInvocation = 0;

metrics.Add("biomeScalar", Measure(
    options.Samples,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Samples; i++)
        {
            var (x, z) = Position(options, i);
            digest = HashText(digest, generator.Biomes.Sample(x, z).Primary);
        }

        return digest;
    }));

metrics.Add("biomeArea", Measure(
    1,
    generator =>
    {
        var sample = generator.Biomes.SampleGrid(
            options.CenterX,
            options.CenterZ,
            options.AreaSize,
            options.AreaSize);
        ulong digest = 14695981039346656037UL;
        for (var z = 0; z < sample.Depth; z++)
        {
            for (var x = 0; x < sample.Width; x++)
            {
                digest = HashText(digest, sample[x, z].Primary);
            }
        }

        return digest;
    }));

metrics.Add("biomeMap", Measure(
    checked(
        options.MapSize *
        options.MapSize),
    generator =>
    {
        var raster =
            BiomeMapDiagnostic.Render(
                generator.Biomes,
                options.CenterX,
                options.CenterZ,
                options.MapSize,
                options.MapSize,
                options.MapStep,
                BiomeMapMode.Influences);
        ulong digest =
            14695981039346656037UL;

        foreach (var pixel in
                 raster.Pixels)
        {
            digest =
                HashNumber(
                    digest,
                    pixel.Red);
            digest =
                HashNumber(
                    digest,
                    pixel.Green);
            digest =
                HashNumber(
                    digest,
                    pixel.Blue);
        }

        return digest;
    }));

metrics.Add("surfaceScalar", Measure(
    options.Samples,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Samples; i++)
        {
            var (x, z) = Position(options, i);
            digest = HashNumber(
                digest,
                unchecked((ulong)generator.SurfaceHeight(x, z)));
        }

        return digest;
    }));

metrics.Add("surfaceArea", Measure(
    options.Chunks,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Chunks; i++)
        {
            var range = generator.GetSurfaceRange(
                options.CenterX / Chunk.Size + i,
                options.CenterZ / Chunk.Size);
            digest = HashNumber(
                digest,
                unchecked((ulong)range.MinimumWorldY));
            digest = HashNumber(
                digest,
                unchecked((ulong)range.MaximumWorldY));
        }

        return digest;
    }));

metrics.Add("densityVolume", Measure(
    4 * 8 * 4,
    generator =>
    {
        var surfaceY = generator.SurfaceHeight(
            options.CenterX,
            options.CenterZ);
        var volume = generator.SampleDensityVolume(
            options.CenterX,
            Math.Max(0, surfaceY - 24),
            options.CenterZ,
            width: 4,
            height: 8,
            depth: 4);
        ulong digest = 14695981039346656037UL;
        for (var z = 0; z < volume.Depth; z++)
        {
            for (var y = 0; y < volume.Height; y++)
            {
                for (var x = 0; x < volume.Width; x++)
                {
                    digest = HashNumber(
                        digest,
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(
                            volume.DensityAt(x, y, z))));
                }
            }
        }

        return digest;
    }));

metrics.Add("biomeSearchNear", Measure(
    1,
    generator =>
        HashBiomeSearch(
            generator.FindNearestSurfaceBiome(
                biomeSearchTarget,
                options.CenterX,
                options.CenterZ,
                searchRadius))));

metrics.Add("biomeSearchFar", Measure(
    1,
    generator =>
        HashBiomeSearch(
            generator.FindNearestSurfaceBiome(
                biomeSearchTarget,
                farX,
                farZ,
                searchRadius))));

metrics.Add("destinationNear", Measure(
    1,
    generator =>
        HashDestination(
            generator.FindGeneratedSurfaceDestination(
                options.CenterX,
                options.CenterZ,
                maxRadius: 64))));

metrics.Add("destinationFar", Measure(
    1,
    generator =>
        HashDestination(
            generator.FindGeneratedSurfaceDestination(
                farX,
                farZ,
                maxRadius: 64))));

if (structureSearchTarget is not null)
{
    metrics.Add("structureSearchNear", Measure(
        1,
        generator =>
            HashStructureSearch(
                generator.FindNearestSurfaceStructure(
                    structureSearchTarget,
                    options.CenterX,
                    options.CenterZ,
                    searchRadius))));

    metrics.Add("structureSearchFar", Measure(
        1,
        generator =>
            HashStructureSearch(
                generator.FindNearestSurfaceStructure(
                    structureSearchTarget,
                    farX,
                    farZ,
                    searchRadius))));
}

metrics.Add("initialSpawnSearch", Measure(
    1,
    generator =>
    {
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64);
        return HashDestination(spawn);
    }));

metrics.Add("initialDesiredChunks", Measure(
    1,
    generator =>
    {
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64) ??
            throw new InvalidOperationException(
                "Benchmark dimension has no safe generated spawn within 64 blocks.");
        var center =
            VoxelCoordinates.FromWorld(
                    spawn.X,
                    spawn.Y,
                    spawn.Z)
                .Chunk;
        var desired =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                center,
                horizontalRadius: 2,
                generator);
        var chunksByY = new SortedDictionary<int, int>();
        foreach (var coord in desired)
        {
            chunksByY[coord.Y] = chunksByY.GetValueOrDefault(coord.Y) + 1;
        }

        initialAreaDiagnostics = new
        {
            SpawnX = spawn.X,
            SpawnY = spawn.Y,
            SpawnZ = spawn.Z,
            CenterChunkX = center.X,
            CenterChunkY = center.Y,
            CenterChunkZ = center.Z,
            DesiredChunkCount = desired.Count,
            SurfaceColumnCount = desired.Select(coord => (coord.X, coord.Z)).Distinct().Count(),
            ChunksByY = chunksByY,
        };

        var digest = 14695981039346656037UL;
        foreach (var coord in desired
                     .OrderBy(coord => coord.Z)
                     .ThenBy(coord => coord.X)
                     .ThenBy(coord => coord.Y))
        {
            digest = HashNumber(digest, unchecked((ulong)coord.X));
            digest = HashNumber(digest, unchecked((ulong)coord.Y));
            digest = HashNumber(digest, unchecked((ulong)coord.Z));
        }

        return HashNumber(digest, (ulong)desired.Count);
    }));

metrics.Add("chunkSynthesis", Measure(
    options.Chunks,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        var chunkX = Math.DivRem(
            options.CenterX,
            Chunk.Size,
            out var remainderX);
        var chunkZ = Math.DivRem(
            options.CenterZ,
            Chunk.Size,
            out var remainderZ);
        if (remainderX < 0) chunkX--;
        if (remainderZ < 0) chunkZ--;

        for (var i = 0; i < options.Chunks; i++)
        {
            var x = checked(chunkX + i);
            var range = generator.GetSurfaceRange(x, chunkZ);
            var topY = Math.Max(0, range.MaximumWorldY);
            var chunkY = topY / Chunk.Size;
            var chunk = generator.Materialize(
                new ChunkCoord(x, chunkY, chunkZ));
            chunk.VisitBlockCells(
                (localX, localY, localZ, cell) =>
                {
                    digest = HashNumber(digest, (ulong)localX);
                    digest = HashNumber(digest, (ulong)localY);
                    digest = HashNumber(digest, (ulong)localZ);
                    digest = HashNumber(digest, cell.Block.Value);
                });
        }

        return digest;
    }));

metrics.Add("initialAreaSynthesis", Measure(
    1,
    generator =>
    {
        var invocation = ++initialAreaInvocation;
        var totalTimer = Stopwatch.StartNew();
        var preparationTimer = Stopwatch.StartNew();
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64) ??
            throw new InvalidOperationException(
                "Benchmark dimension has no safe generated spawn within 64 blocks.");
        var center =
            VoxelCoordinates.FromWorld(
                    spawn.X,
                    spawn.Y,
                    spawn.Z)
                .Chunk;
        var desired =
            ChunkStreamingSelection
                .DesiredSurfaceChunks(
                    center,
                    horizontalRadius: 2,
                    generator)
                .OrderBy(
                    coord =>
                        coord.Z)
                .ThenBy(
                    coord =>
                        coord.X)
                .ThenBy(
                    coord =>
                        coord.Y)
                .ToArray();
        preparationTimer.Stop();
        ulong digest =
            14695981039346656037UL;
        var materializeMsByY = new SortedDictionary<int, double>();
        var materializeBytesByY = new SortedDictionary<int, long>();
        var chunkCountByY = new SortedDictionary<int, int>();
        var totalMaterializeMs = 0d;
        var totalMaterializeBytes = 0L;
        var totalDigestMs = 0d;
        var totalDigestBytes = 0L;

        foreach (var coord in
                 desired)
        {
            var beforeChunkBytes = GC.GetAllocatedBytesForCurrentThread();
            var chunkTimer = Stopwatch.StartNew();
            var chunk =
                generator.Materialize(
                    coord);
            chunkTimer.Stop();
            var chunkMs = chunkTimer.Elapsed.TotalMilliseconds;
            var chunkBytes = checked(
                GC.GetAllocatedBytesForCurrentThread() - beforeChunkBytes);
            totalMaterializeMs += chunkMs;
            totalMaterializeBytes += chunkBytes;
            materializeMsByY[coord.Y] =
                materializeMsByY.GetValueOrDefault(coord.Y) + chunkMs;
            materializeBytesByY[coord.Y] =
                materializeBytesByY.GetValueOrDefault(coord.Y) + chunkBytes;
            chunkCountByY[coord.Y] =
                chunkCountByY.GetValueOrDefault(coord.Y) + 1;

            var beforeDigestBytes = GC.GetAllocatedBytesForCurrentThread();
            var digestTimer = Stopwatch.StartNew();
            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.X));
            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.Y));
            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.Z));
            chunk.VisitBlockCells(
                (localX, localY, localZ, cell) =>
                {
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localX);
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localY);
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localZ);
                    digest =
                        HashNumber(
                            digest,
                            cell.Block.Value);
                });
            digestTimer.Stop();
            totalDigestMs += digestTimer.Elapsed.TotalMilliseconds;
            totalDigestBytes += checked(
                GC.GetAllocatedBytesForCurrentThread() - beforeDigestBytes);
        }

        var materializationByY = new SortedDictionary<int, object>();
        foreach (var pair in chunkCountByY)
        {
            var y = pair.Key;
            materializationByY[y] = new
            {
                ChunkCount = pair.Value,
                MaterializeMs = materializeMsByY[y],
                AllocatedBytes = materializeBytesByY[y],
            };
        }

        var materializationSnapshot = new
        {
            ChunkCount = desired.Length,
            TotalMaterializeMs = totalMaterializeMs,
            TotalAllocatedBytes = totalMaterializeBytes,
            ByY = materializationByY,
        };
        if (invocation == 1)
            coldMaterializationDiagnostics = materializationSnapshot;
        else
            warmMaterializationDiagnostics = materializationSnapshot;

        var finalDigestTimer = Stopwatch.StartNew();
        var beforeFinalDigestBytes = GC.GetAllocatedBytesForCurrentThread();
        digest =
            HashNumber(
                digest,
                (ulong)desired.Length);
        finalDigestTimer.Stop();
        totalDigestMs += finalDigestTimer.Elapsed.TotalMilliseconds;
        totalDigestBytes += checked(
            GC.GetAllocatedBytesForCurrentThread() - beforeFinalDigestBytes);

        totalTimer.Stop();
        var timingSnapshot = new
        {
            TotalElapsedMilliseconds = totalTimer.Elapsed.TotalMilliseconds,
            SpawnAndSelectionMilliseconds = preparationTimer.Elapsed.TotalMilliseconds,
            MaterializationMilliseconds = totalMaterializeMs,
            DigestMilliseconds = totalDigestMs,
            UnattributedMilliseconds = Math.Max(
                0d,
                totalTimer.Elapsed.TotalMilliseconds -
                preparationTimer.Elapsed.TotalMilliseconds -
                totalMaterializeMs -
                totalDigestMs),
            MaterializationAllocatedBytes = totalMaterializeBytes,
            DigestAllocatedBytes = totalDigestBytes,
        };
        if (invocation == 1)
            coldInitialAreaTimingDiagnostics = timingSnapshot;
        else
            warmInitialAreaTimingDiagnostics.Add(timingSnapshot);
        return digest;
    }));

object CaptureMaterializationStageTimings(BiomeWorldGenerator generator)
{
    var center = new ChunkCoord(
        initialAreaDiagnostics.CenterChunkX,
        initialAreaDiagnostics.CenterChunkY,
        initialAreaDiagnostics.CenterChunkZ);
    var desired = ChunkStreamingSelection
        .DesiredSurfaceChunks(center, horizontalRadius: 2, generator)
        .OrderBy(coord => coord.Z)
        .ThenBy(coord => coord.X)
        .ThenBy(coord => coord.Y)
        .ToArray();
    var stages = Enum.GetValues<ChunkMaterializationStage>();
    var elapsedMsByStage = new double[stages.Length];
    var allocatedBytesByStage = new long[stages.Length];
    var advanceCountByStage = new int[stages.Length];
    var paletteProfiles = new List<DirectionalFacePaletteMetrics>();
    var boundaryColumnBoundsMilliseconds = 0d;
    var caveSamplerBuildMilliseconds = 0d;
    var boundaryColumnEvaluationMilliseconds = 0d;
    var directionalMetadataChunkCount = 0;
    var directionalMetadataVoxelCount = 0L;
    var directionalMetadataPaletteEntryCount = 0L;
    var directionalMetadataStorageProfiles = new List<(
        ChunkCoord Coord,
        int MetadataVoxelCount,
        PaletteStorageCapacityMetrics Storage)>();
    var maximumDirectionalMetadataVoxelsPerChunk = 0;
    var totalNonEmptyVoxels = 0L;
    var storageDiagnosticsSeen = false;
    var storageDiagnosticsAvailable = true;
    long terrainLayerBlockPaletteGrowthBytes = 0;
    var terrainLayerBlockPaletteGrowthEvents = 0;
    long terrainLayerDirectionalPaletteGrowthBytes = 0;
    var terrainLayerDirectionalPaletteGrowthEvents = 0;
    long directionalFaceBlockPaletteGrowthBytes = 0;
    var directionalFaceBlockPaletteGrowthEvents = 0;
    long directionalFaceMetadataPaletteGrowthBytes = 0;
    var directionalFaceMetadataPaletteGrowthEvents = 0;
    var terrainLayerGeologyNoiseCacheCapacity = 0;
    long terrainLayerGeologyNoiseCacheHits = 0;
    long terrainLayerGeologyNoiseCacheMisses = 0;
    long terrainLayerGeologyNoiseCacheCollisions = 0;
    var totalTimer = Stopwatch.StartNew();

    foreach (var coord in desired)
    {
        using var job = generator.BeginMaterialization(coord);
        while (!job.IsComplete)
        {
            var stage = job.ActiveWorkStage;
            var stageIndex = (int)stage;
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();
            job.Advance();
            elapsedMsByStage[stageIndex] +=
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            allocatedBytesByStage[stageIndex] +=
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            advanceCountByStage[stageIndex]++;

            if (stage == ChunkMaterializationStage.DirectionalFacePalettes)
            {
                if (job.DirectionalFacePaletteMetrics is { } profile)
                    paletteProfiles.Add(profile);
                boundaryColumnBoundsMilliseconds +=
                    job.DirectionalFaceBoundaryColumnBoundsMilliseconds;
                caveSamplerBuildMilliseconds +=
                    job.DirectionalFaceCaveSamplerBuildMilliseconds;
                boundaryColumnEvaluationMilliseconds +=
                    job.DirectionalFaceBoundaryColumnEvaluationMilliseconds;
            }
        }

        if (job is IChunkMaterializationStorageDiagnostics storageDiagnostic)
        {
            var diagnostics = storageDiagnostic.StorageDiagnostics;
            storageDiagnosticsSeen = true;
            storageDiagnosticsAvailable &= diagnostics.Available;
            terrainLayerBlockPaletteGrowthBytes +=
                diagnostics.TerrainLayerBlockPalette.ArrayPayloadBytes;
            terrainLayerBlockPaletteGrowthEvents +=
                diagnostics.TerrainLayerBlockPalette.GrowthEvents;
            terrainLayerDirectionalPaletteGrowthBytes +=
                diagnostics.TerrainLayerDirectionalFacePalette.ArrayPayloadBytes;
            terrainLayerDirectionalPaletteGrowthEvents +=
                diagnostics.TerrainLayerDirectionalFacePalette.GrowthEvents;
            directionalFaceBlockPaletteGrowthBytes +=
                diagnostics.DirectionalFaceBlockPalette.ArrayPayloadBytes;
            directionalFaceBlockPaletteGrowthEvents +=
                diagnostics.DirectionalFaceBlockPalette.GrowthEvents;
            directionalFaceMetadataPaletteGrowthBytes +=
                diagnostics.DirectionalFaceMetadataPalette.ArrayPayloadBytes;
            directionalFaceMetadataPaletteGrowthEvents +=
                diagnostics.DirectionalFaceMetadataPalette.GrowthEvents;
            terrainLayerGeologyNoiseCacheCapacity = Math.Max(
                terrainLayerGeologyNoiseCacheCapacity,
                diagnostics.TerrainLayerGeologyNoiseCache.Capacity);
            terrainLayerGeologyNoiseCacheHits +=
                diagnostics.TerrainLayerGeologyNoiseCache.Hits;
            terrainLayerGeologyNoiseCacheMisses +=
                diagnostics.TerrainLayerGeologyNoiseCache.Misses;
            terrainLayerGeologyNoiseCacheCollisions +=
                diagnostics.TerrainLayerGeologyNoiseCache.Collisions;
        }
        else
        {
            storageDiagnosticsAvailable = false;
        }

        var materializedChunk = job.Result ??
            throw new InvalidOperationException(
                $"Chunk {coord} completed without a materialized result.");
        var metadataVoxels = materializedChunk.DirectionalFaceDataVoxelCount;
        if (metadataVoxels > 0)
        {
            directionalMetadataChunkCount++;
            directionalMetadataVoxelCount += metadataVoxels;
            directionalMetadataPaletteEntryCount +=
                materializedChunk.DirectionalFaceDataPaletteEntryCount;
            maximumDirectionalMetadataVoxelsPerChunk = Math.Max(
                maximumDirectionalMetadataVoxelsPerChunk, metadataVoxels);
            directionalMetadataStorageProfiles.Add((
                coord,
                metadataVoxels,
                materializedChunk.DirectionalFaceStorageCapacityMetrics ??
                    throw new InvalidOperationException(
                        $"Chunk {coord} has directional metadata without palette storage diagnostics.")));
        }
        totalNonEmptyVoxels += materializedChunk.NonEmptyVoxelCount;
    }

    totalTimer.Stop();
    var byStage = new SortedDictionary<string, object>(StringComparer.Ordinal);
    var totalAdvanceMs = 0d;
    for (var index = 0; index < stages.Length; index++)
    {
        if (advanceCountByStage[index] == 0)
            continue;

        var elapsedMs = elapsedMsByStage[index];
        totalAdvanceMs += elapsedMs;
        byStage.Add(stages[index].ToString(), new
        {
            AdvanceCount = advanceCountByStage[index],
            ElapsedMs = elapsedMs,
            AllocatedBytes = allocatedBytesByStage[index],
        });
    }

#if DEBUG
    if (paletteProfiles.Count != desired.Length ||
        paletteProfiles.Any(profile => !profile.HasTimingBreakdown))
    {
        throw new InvalidOperationException(
            "Debug directional face-palette profile is missing chunk timing data.");
    }
#endif

    var paletteProfile = new
    {
        ProfiledChunkCount = paletteProfiles.Count,
        TimedChunkCount = paletteProfiles.Count(profile => profile.HasTimingBreakdown),
        TimingSampleInterval = paletteProfiles
            .Select(profile => profile.TimingSampleInterval)
            .DefaultIfEmpty(0)
            .Max(),
        SurfaceColumns = paletteProfiles.Sum(profile => (long)profile.SurfaceColumns),
        SolidVoxels = paletteProfiles.Sum(profile => (long)profile.SolidVoxels),
        FaceProbes = paletteProfiles.Sum(profile => (long)profile.FaceProbes),
        NeighborColumnLookups = paletteProfiles.Sum(profile => (long)profile.NeighborColumnLookups),
        SolidPaletteLookups = paletteProfiles.Sum(profile => (long)profile.SolidPaletteLookups),
        EmptyRegionPaletteLookups = paletteProfiles.Sum(profile => (long)profile.EmptyRegionPaletteLookups),
        DensityProbes = paletteProfiles.Sum(profile => (long)profile.DensityProbes),
        BoundaryDensityProbes = paletteProfiles.Sum(profile => (long)profile.BoundaryDensityProbes),
        EmptyRegionOwnerResolutions = paletteProfiles.Sum(profile => (long)profile.EmptyRegionOwnerResolutions),
        MaskRejectedProbes = paletteProfiles.Sum(profile => (long)profile.MaskRejectedProbes),
        EmptyRegionEligibilityRejectedProbes = paletteProfiles.Sum(profile => (long)profile.EmptyRegionEligibilityRejectedProbes),
        PaletteRejectedProbes = paletteProfiles.Sum(profile => (long)profile.PaletteRejectedProbes),
        DepthRejectedProbes = paletteProfiles.Sum(profile => (long)profile.DepthRejectedProbes),
        PaletteWrites = paletteProfiles.Sum(profile => (long)profile.PaletteWrites),
        TotalMeasuredMilliseconds = paletteProfiles.Sum(profile => profile.TotalMilliseconds),
        TotalMeasuredMaximumMilliseconds = paletteProfiles
            .Select(profile => profile.TotalMilliseconds)
            .DefaultIfEmpty(0d)
            .Max(),
        EstimatedSurfaceColumnPreparationMilliseconds = paletteProfiles.Sum(
            profile => profile.SurfaceColumnPreparationEstimatedMilliseconds),
        EstimatedSolidOwnerResolutionMilliseconds = paletteProfiles.Sum(
            profile => profile.SolidOwnerResolutionEstimatedMilliseconds),
        EstimatedNeighborOwnerLookupMilliseconds = paletteProfiles.Sum(
            profile => profile.NeighborOwnerLookupEstimatedMilliseconds),
        EstimatedDensityProbeMilliseconds = paletteProfiles.Sum(
            profile => profile.DensityProbeEstimatedMilliseconds),
        EstimatedEmptyRegionOwnerResolutionMilliseconds = paletteProfiles.Sum(
            profile => profile.EmptyRegionOwnerResolutionEstimatedMilliseconds),
        EstimatedPaletteWriteMilliseconds = paletteProfiles.Sum(
            profile => profile.PaletteWriteEstimatedMilliseconds),
        EstimatedOtherMilliseconds = paletteProfiles.Sum(
            profile => profile.OtherEstimatedMilliseconds),
        DensitySlabReadMilliseconds = paletteProfiles.Sum(
            profile => profile.DensitySlabReadMilliseconds),
        HorizontalBoundaryReadMilliseconds = paletteProfiles.Sum(
            profile => profile.HorizontalBoundaryReadMilliseconds),
        OccupancyRowAssemblyMilliseconds = paletteProfiles.Sum(
            profile => profile.OccupancyRowAssemblyMilliseconds),
        CandidateMaskBuildMilliseconds = paletteProfiles.Sum(
            profile => profile.CandidateMaskBuildMilliseconds),
        BoundaryColumnBoundsMilliseconds = boundaryColumnBoundsMilliseconds,
        CaveSamplerBuildMilliseconds = caveSamplerBuildMilliseconds,
        BoundaryColumnEvaluationMilliseconds = boundaryColumnEvaluationMilliseconds,
        DensityTerrainColumnMilliseconds = paletteProfiles.Sum(
            profile => profile.DensityTerrainColumnMilliseconds),
        OccupancyCacheAccessMilliseconds = paletteProfiles.Sum(
            profile => profile.OccupancyCacheAccessMilliseconds),
        SurfaceBiomeSamplingMilliseconds = paletteProfiles.Sum(
            profile => profile.SurfaceBiomeSamplingMilliseconds),
        BaseHeightSamplingMilliseconds = paletteProfiles.Sum(
            profile => profile.BaseHeightSamplingMilliseconds),
        VolumeBiomeSamplingMilliseconds = paletteProfiles.Sum(
            profile => profile.VolumeBiomeSamplingMilliseconds),
        HorizontalBoundaryBatchCount = paletteProfiles.Sum(
            profile => (long)profile.HorizontalBoundaryBatchCount),
        HorizontalBoundaryColumnCount = paletteProfiles.Sum(
            profile => (long)profile.HorizontalBoundaryColumnCount),
        AverageMillisecondsPerChunk = paletteProfiles.Count == 0
            ? 0d
            : paletteProfiles.Average(profile => profile.TotalMilliseconds),
    };

    var storageProfiles = directionalMetadataStorageProfiles;
    var paletteElementSizeBytes = Unsafe.SizeOf<DirectionalFaceVoxelData>();
    var lookupEntrySizeBytes = Unsafe.SizeOf<DirectionalFacePaletteLookupEntryLayout>();
    var paletteListCapacity = storageProfiles.Sum(
        profile => (long)profile.Storage.PaletteCapacity);
    var paletteListEntries = storageProfiles.Sum(
        profile => (long)profile.Storage.PaletteEntryCount);
    var usageListCapacity = storageProfiles.Sum(
        profile => (long)profile.Storage.UsageCapacity);
    var usageListEntries = storageProfiles.Sum(
        profile => (long)profile.Storage.UsageEntryCount);
    var lookupCapacity = storageProfiles.Sum(
        profile => (long)profile.Storage.LookupCapacity);
    var lookupEntries = storageProfiles.Sum(
        profile => (long)profile.Storage.LookupEntryCount);
    var freeSlotCapacity = storageProfiles.Sum(
        profile => (long)profile.Storage.FreeSlotCapacity);
    var freeSlotCount = storageProfiles.Sum(
        profile => (long)profile.Storage.FreeSlotCount);
    var indexCount = storageProfiles.Sum(
        profile => (long)profile.Storage.IndexCount);
    var occupancyWordCount = storageProfiles.Sum(
        profile => (long)profile.Storage.OccupancyWordCount);
    var meshletOccupancyCount = storageProfiles.Sum(
        profile => (long)profile.Storage.MeshletOccupancyCount);
    var paletteListBytes = paletteListCapacity * paletteElementSizeBytes;
    var usageListBytes = usageListCapacity * sizeof(ushort);
    var lookupBucketBytes = lookupCapacity * sizeof(int);
    var lookupEntryBytesEstimate = lookupCapacity * lookupEntrySizeBytes;
    var freeSlotStackBytes = freeSlotCapacity * sizeof(ushort);
    var denseIndexBytes = indexCount * sizeof(ushort);
    var occupancyBitsetBytes = occupancyWordCount * sizeof(ulong);
    var meshletOccupancyBytes = meshletOccupancyCount * sizeof(int);
    var fixedBufferBytes =
        denseIndexBytes + occupancyBitsetBytes + meshletOccupancyBytes;
    var estimatedBackingArrayPayloadBytes =
        paletteListBytes + usageListBytes +
        lookupBucketBytes + lookupEntryBytesEstimate +
        freeSlotStackBytes + fixedBufferBytes;
    var directionalStorageByChunk = storageProfiles.Select(profile => new
    {
        X = profile.Coord.X,
        Y = profile.Coord.Y,
        Z = profile.Coord.Z,
        MetadataVoxels = profile.MetadataVoxelCount,
        OccupiedVoxels = profile.Storage.OccupiedVoxelCount,
        PaletteEntries = profile.Storage.PaletteEntryCount,
        PaletteCapacity = profile.Storage.PaletteCapacity,
        UsageEntries = profile.Storage.UsageEntryCount,
        UsageCapacity = profile.Storage.UsageCapacity,
        LookupEntries = profile.Storage.LookupEntryCount,
        LookupCapacity = profile.Storage.LookupCapacity,
        FreeSlotCount = profile.Storage.FreeSlotCount,
        FreeSlotCapacity = profile.Storage.FreeSlotCapacity,
        IndexCount = profile.Storage.IndexCount,
        OccupancyWordCount = profile.Storage.OccupancyWordCount,
        MeshletOccupancyCount = profile.Storage.MeshletOccupancyCount,
    }).ToArray();

    return new
    {
        ChunkCount = desired.Length,
        TotalElapsedMs = totalTimer.Elapsed.TotalMilliseconds,
        TotalAdvanceMs = totalAdvanceMs,
        ByStage = byStage,
        DirectionalFacePaletteProfile = paletteProfile,
        PaletteStorageGrowthByStage = new
        {
            DiagnosticsAvailable =
                storageDiagnosticsSeen && storageDiagnosticsAvailable,
            TerrainLayer = new
            {
                BlockPalette = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth =
                        terrainLayerBlockPaletteGrowthBytes,
                    GrowthEvents = terrainLayerBlockPaletteGrowthEvents,
                },
                DirectionalMetadataPalette = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth =
                        terrainLayerDirectionalPaletteGrowthBytes,
                    GrowthEvents = terrainLayerDirectionalPaletteGrowthEvents,
                },
            },
            DirectionalFacePalettes = new
            {
                BlockPalette = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth =
                        directionalFaceBlockPaletteGrowthBytes,
                    GrowthEvents = directionalFaceBlockPaletteGrowthEvents,
                },
                DirectionalMetadataPalette = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth =
                        directionalFaceMetadataPaletteGrowthBytes,
                    GrowthEvents = directionalFaceMetadataPaletteGrowthEvents,
                },
            },
        },
        DirectionalMetadataStorage = new
        {
            ChunksWithMetadata = directionalMetadataChunkCount,
            TotalChunks = desired.Length,
            TotalMetadataVoxels = directionalMetadataVoxelCount,
            TotalNonEmptyVoxels = totalNonEmptyVoxels,
            MetadataVoxelPercentOfNonEmpty = totalNonEmptyVoxels == 0
                ? 0d
                : directionalMetadataVoxelCount * 100d / totalNonEmptyVoxels,
            TotalPaletteEntries = directionalMetadataPaletteEntryCount,
            AverageMetadataVoxelsPerBearingChunk =
                directionalMetadataChunkCount == 0
                    ? 0d
                    : directionalMetadataVoxelCount / (double)directionalMetadataChunkCount,
            MaximumMetadataVoxelsPerChunk =
                maximumDirectionalMetadataVoxelsPerChunk,
            DenseIndexBytes = denseIndexBytes,
            OccupancyBitsetBytes = occupancyBitsetBytes,
            PaletteList = new
            {
                Entries = paletteListEntries,
                Capacity = paletteListCapacity,
                UnusedSlots = paletteListCapacity - paletteListEntries,
                ElementSizeBytes = paletteElementSizeBytes,
                BackingArrayPayloadBytes = paletteListBytes,
            },
            UsageList = new
            {
                Entries = usageListEntries,
                Capacity = usageListCapacity,
                UnusedSlots = usageListCapacity - usageListEntries,
                ElementSizeBytes = sizeof(ushort),
                BackingArrayPayloadBytes = usageListBytes,
            },
            LookupDictionary = new
            {
                Entries = lookupEntries,
                Capacity = lookupCapacity,
                UnusedSlots = lookupCapacity - lookupEntries,
                BucketArrayPayloadBytes = lookupBucketBytes,
                EntrySizeBytesEstimate = lookupEntrySizeBytes,
                EntryArrayPayloadBytesEstimate = lookupEntryBytesEstimate,
            },
            FreeSlotStack = new
            {
                Entries = freeSlotCount,
                Capacity = freeSlotCapacity,
                UnusedSlots = freeSlotCapacity - freeSlotCount,
                BackingArrayPayloadBytes = freeSlotStackBytes,
            },
            FixedBuffers = new
            {
                DenseIndexBytes = denseIndexBytes,
                OccupancyBitsetBytes = occupancyBitsetBytes,
                MeshletOccupancyBytes = meshletOccupancyBytes,
                TotalBytes = fixedBufferBytes,
            },
            CapacityGrowth = new
            {
                DiagnosticsAvailable = storageProfiles.All(
                    profile => profile.Storage.GrowthDiagnosticsAvailable),
                PaletteList = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth = storageProfiles.Sum(
                        profile => profile.Storage.PaletteGrowthArrayBytes),
                    GrowthEvents = storageProfiles.Sum(
                        profile => profile.Storage.PaletteGrowthEvents),
                },
                UsageList = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth = storageProfiles.Sum(
                        profile => profile.Storage.UsageGrowthArrayBytes),
                    GrowthEvents = storageProfiles.Sum(
                        profile => profile.Storage.UsageGrowthEvents),
                },
                LookupDictionary = new
                {
                    BucketArrayPayloadBytesAllocatedAcrossGrowth = storageProfiles.Sum(
                        profile => profile.Storage.LookupBucketGrowthArrayBytes),
                    EntryArrayPayloadBytesAllocatedAcrossGrowth = storageProfiles.Sum(
                        profile => profile.Storage.LookupEntryGrowthArrayBytes),
                    GrowthEvents = storageProfiles.Sum(
                        profile => profile.Storage.LookupGrowthEvents),
                },
                FreeSlotStack = new
                {
                    ArrayPayloadBytesAllocatedAcrossGrowth = storageProfiles.Sum(
                        profile => profile.Storage.FreeSlotGrowthArrayBytes),
                    GrowthEvents = storageProfiles.Sum(
                        profile => profile.Storage.FreeSlotGrowthEvents),
                },
                TotalArrayPayloadBytesAllocatedAcrossGrowth =
                    storageProfiles.Sum(profile =>
                        profile.Storage.PaletteGrowthArrayBytes +
                        profile.Storage.UsageGrowthArrayBytes +
                        profile.Storage.LookupBucketGrowthArrayBytes +
                        profile.Storage.LookupEntryGrowthArrayBytes +
                        profile.Storage.FreeSlotGrowthArrayBytes),
            },
            EstimatedBackingArrayPayloadBytes = estimatedBackingArrayPayloadBytes,
            StorageCapacityByChunk = directionalStorageByChunk,
        },
        TerrainLayerGeologyNoiseCache = new
        {
            Capacity = terrainLayerGeologyNoiseCacheCapacity,
            Hits = terrainLayerGeologyNoiseCacheHits,
            Misses = terrainLayerGeologyNoiseCacheMisses,
            Collisions = terrainLayerGeologyNoiseCacheCollisions,
            Lookups = terrainLayerGeologyNoiseCacheHits +
                terrainLayerGeologyNoiseCacheMisses,
            HitRate = terrainLayerGeologyNoiseCacheHits +
                terrainLayerGeologyNoiseCacheMisses == 0
                    ? 0d
                    : terrainLayerGeologyNoiseCacheHits /
                        (double)(terrainLayerGeologyNoiseCacheHits +
                            terrainLayerGeologyNoiseCacheMisses),
            CollisionRate = terrainLayerGeologyNoiseCacheMisses == 0
                ? 0d
                : terrainLayerGeologyNoiseCacheCollisions /
                    (double)terrainLayerGeologyNoiseCacheMisses,
        },
    };
}

object CaptureTerrainLayerGeologyModeDiagnostics()
{
    const int repetitions = 6;
    const string swampBiomeId = "asteria:overworld/swamp";
    if (!dimension.SurfaceBiomes.Contains(swampBiomeId, StringComparer.Ordinal))
    {
        return new
        {
            Skipped = true,
            Reason = $"Dimension {dimension.Id} does not include {swampBiomeId}.",
        };
    }

    var authoredSwamp = biomes.Get(swampBiomeId);
    var swampGeology = authoredSwamp.Geology;
    if (swampGeology is null)
    {
        return new
        {
            Skipped = true,
            Reason = $"Biome {swampBiomeId} has no authored geology.",
        };
    }

    var swampGeneration = new WorldGenerationOptions(
        spawnBiome: swampBiomeId,
        singleBiome: true);
    var swampPerVoxelRegistry = new BiomeRegistry(
        sourceBiomeDefinitions.Select(definition =>
            string.Equals(definition.Id, swampBiomeId, StringComparison.Ordinal)
                ? WithGeologySamplingMode(
                    definition, BiomeGeologySamplingMode.PerVoxel)
                : definition));
    var swampRegionalRegistry = new BiomeRegistry(
        sourceBiomeDefinitions.Select(definition =>
            string.Equals(definition.Id, swampBiomeId, StringComparison.Ordinal)
                ? WithGeologySamplingMode(
                    definition, BiomeGeologySamplingMode.Regional)
                : definition));
    var swampHybridRegistry = new BiomeRegistry(
        sourceBiomeDefinitions.Select(definition =>
            string.Equals(definition.Id, swampBiomeId, StringComparison.Ordinal)
                ? WithGeologySamplingMode(
                    definition, BiomeGeologySamplingMode.RegionalHybrid)
                : definition));

    var selectionGenerator = NewGenerator(
        generation: swampGeneration,
        biomeRegistry: swampPerVoxelRegistry);
    var centerX = initialAreaDiagnostics.CenterChunkX;
    var centerZ = initialAreaDiagnostics.CenterChunkZ;
    var swampSurface = selectionGenerator.GetSurfaceRange(centerX, centerZ);
    var center = new ChunkCoord(
        centerX,
        Math.Max(0, (int)Math.Floor(
            swampSurface.MaximumWorldY / (double)Chunk.Size)),
        centerZ);
    var desired = ChunkStreamingSelection
        .DesiredSurfaceChunks(center, horizontalRadius: 2, selectionGenerator)
        .OrderBy(coord => coord.Z)
        .ThenBy(coord => coord.X)
        .ThenBy(coord => coord.Y)
        .ToArray();
    var perVoxelRuns = new List<(
        double ElapsedMs, long AllocatedBytes, ulong Digest, long SolidVoxels)>();
    var regionalRuns = new List<(
        double ElapsedMs, long AllocatedBytes, ulong Digest, long SolidVoxels)>();
    var hybridRuns = new List<(
        double ElapsedMs, long AllocatedBytes, ulong Digest, long SolidVoxels)>();

    for (var repetition = 0; repetition < repetitions; repetition++)
    {
        var modes = (repetition % 3) switch
        {
            0 => new[]
            {
                BiomeGeologySamplingMode.PerVoxel,
                BiomeGeologySamplingMode.Regional,
                BiomeGeologySamplingMode.RegionalHybrid,
            },
            1 => new[]
            {
                BiomeGeologySamplingMode.Regional,
                BiomeGeologySamplingMode.RegionalHybrid,
                BiomeGeologySamplingMode.PerVoxel,
            },
            _ => new[]
            {
                BiomeGeologySamplingMode.RegionalHybrid,
                BiomeGeologySamplingMode.PerVoxel,
                BiomeGeologySamplingMode.Regional,
            },
        };

        foreach (var mode in modes)
        {
            var registry = mode switch
            {
                BiomeGeologySamplingMode.PerVoxel => swampPerVoxelRegistry,
                BiomeGeologySamplingMode.Regional => swampRegionalRegistry,
                BiomeGeologySamplingMode.RegionalHybrid => swampHybridRegistry,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            var generator = NewGenerator(
                generation: swampGeneration,
                biomeRegistry: registry);

            // Warm the same generator before timing each independent repetition.
            _ = CapturePass(generator, desired, capture: false);
            var measured = CapturePass(generator, desired, capture: true);
            switch (mode)
            {
                case BiomeGeologySamplingMode.PerVoxel:
                    perVoxelRuns.Add(measured);
                    break;
                case BiomeGeologySamplingMode.Regional:
                    regionalRuns.Add(measured);
                    break;
                case BiomeGeologySamplingMode.RegionalHybrid:
                    hybridRuns.Add(measured);
                    break;
            }
        }
    }

    var perVoxel = Summarize(perVoxelRuns);
    var regional = Summarize(regionalRuns);
    var regionalHybrid = Summarize(hybridRuns);
    var solidVoxelCountsMatch =
        perVoxel.SolidVoxelsPerRun.Length == 1 &&
        regional.SolidVoxelsPerRun.Length == 1 &&
        regionalHybrid.SolidVoxelsPerRun.Length == 1 &&
        perVoxel.SolidVoxelsPerRun[0] == regional.SolidVoxelsPerRun[0] &&
        perVoxel.SolidVoxelsPerRun[0] == regionalHybrid.SolidVoxelsPerRun[0];
    return new
    {
        Biome = swampBiomeId,
        AuthoredSamplingMode = swampGeology.SamplingMode.ToString(),
        ComparisonSamplingMode = "Regional and RegionalHybrid",
        ChunkCount = desired.Length,
        Repetitions = repetitions,
        WarmupPassesPerRepetition = 1,
        PerVoxel = perVoxel,
        Regional = regional,
        RegionalHybrid = regionalHybrid,
        SolidVoxelCountsMatch = solidVoxelCountsMatch,
        RegionalTimeReductionPercent = perVoxel.MedianElapsedMs <= 0d
            ? 0d
            : (perVoxel.MedianElapsedMs - regional.MedianElapsedMs) *
              100d / perVoxel.MedianElapsedMs,
        HybridTimeReductionPercent = perVoxel.MedianElapsedMs <= 0d
            ? 0d
            : (perVoxel.MedianElapsedMs - regionalHybrid.MedianElapsedMs) *
              100d / perVoxel.MedianElapsedMs,
        Note = "Single-biome swamp materialization. PerVoxel, Regional and RegionalHybrid change only swamp geology sampling mode; chunk coordinates, terrain settings and other biome definitions remain unchanged. Digests are checked for repeatability within each mode, not equality between different materials. RegionalHybrid combines organic regional-boundary displacement with sparse fine-scale mineral inclusions selected using authored material weights.",
    };

    static (
        double ElapsedMs, long AllocatedBytes, ulong Digest, long SolidVoxels)
        CapturePass(
            BiomeWorldGenerator generator,
            ChunkCoord[] desired,
            bool capture)
    {
        var terrainLayerMilliseconds = 0d;
        var terrainLayerAllocatedBytes = 0L;
        var digest = 14695981039346656037UL;
        var solidVoxels = 0L;

        foreach (var coord in desired)
        {
            using var job = generator.BeginMaterialization(coord);
            while (!job.IsComplete)
            {
                var stage = job.ActiveWorkStage;
                if (capture && stage == ChunkMaterializationStage.TerrainLayer)
                {
                    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                    var startedAt = Stopwatch.GetTimestamp();
                    job.Advance();
                    terrainLayerMilliseconds +=
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                    terrainLayerAllocatedBytes +=
                        GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                }
                else
                {
                    job.Advance();
                }
            }

            if (!capture)
                continue;

            var chunk = job.Result ??
                throw new InvalidOperationException(
                    $"Chunk {coord} completed without a materialized result.");
            digest = HashNumber(digest, unchecked((ulong)coord.X));
            digest = HashNumber(digest, unchecked((ulong)coord.Y));
            digest = HashNumber(digest, unchecked((ulong)coord.Z));
            chunk.VisitBlockCells((localX, localY, localZ, cell) =>
            {
                digest = HashNumber(digest, (ulong)localX);
                digest = HashNumber(digest, (ulong)localY);
                digest = HashNumber(digest, (ulong)localZ);
                digest = HashNumber(digest, cell.Block.Value);
            });
            solidVoxels += chunk.NonEmptyVoxelCount;
        }

        return (
            terrainLayerMilliseconds,
            terrainLayerAllocatedBytes,
            digest,
            solidVoxels);
    }

    static TerrainLayerGeologyModeSummary Summarize(
        List<(double ElapsedMs, long AllocatedBytes, ulong Digest, long SolidVoxels)> runs)
    {
        var elapsed = runs.Select(run => run.ElapsedMs).Order().ToArray();
        var allocated = runs.Select(run => run.AllocatedBytes).Order().ToArray();
        var digests = runs.Select(run => run.Digest).Distinct().ToArray();
        var solidVoxels = runs.Select(run => run.SolidVoxels).Distinct().ToArray();
        var elapsedMiddle = elapsed.Length / 2;
        var medianElapsed = (elapsed.Length & 1) == 0
            ? (elapsed[elapsedMiddle - 1] + elapsed[elapsedMiddle]) / 2d
            : elapsed[elapsedMiddle];
        var allocatedMiddle = allocated.Length / 2;
        var medianAllocated = (allocated.Length & 1) == 0
            ? (long)Math.Round(
                ((double)allocated[allocatedMiddle - 1] + allocated[allocatedMiddle]) / 2d,
                MidpointRounding.AwayFromZero)
            : allocated[allocatedMiddle];
        return new TerrainLayerGeologyModeSummary(
            MedianElapsedMs: medianElapsed,
            MedianAllocatedBytes: medianAllocated,
            MinimumElapsedMs: elapsed[0],
            MaximumElapsedMs: elapsed[^1],
            Digests: digests,
            DeterministicAcrossRepetitions: digests.Length == 1,
            SolidVoxelsPerRun: solidVoxels);
    }
}

var terrainLayerGeologyModeDiagnostics = options.ProfileGeologySampling
    ? CaptureTerrainLayerGeologyModeDiagnostics()
    : null;

const int StageWarmSampleCount = 3;
var stageTimingGenerator = NewGenerator();
var stageTimingSamples = new List<object>(StageWarmSampleCount + 1);
for (var sample = 0; sample <= StageWarmSampleCount; sample++)
{
    stageTimingSamples.Add(
        CaptureMaterializationStageTimings(stageTimingGenerator));
}
var coldStageTimingDiagnostics = stageTimingSamples[0];
// Preserve the original Warm field for existing CI consumers: it remains
// the first same-generator pass after the cold materialization.
var warmStageTimingDiagnostics = stageTimingSamples[1];
var warmStageTimingSamples = stageTimingSamples
    .Skip(1)
    .Select((timing, index) => new
    {
        Sample = index + 1,
        Timing = timing,
    })
    .ToArray();
var geologySamplingDiagnostics = options.ProfileGeologySampling
    ? CaptureGeologySamplingDiagnostics(
        biomes, blocks, dimensionSeed)
    : null;

if (options.EnforceCiBudgets)
{
    EnforceCiBudgets(
        options,
        metrics);
}

var result = new
{
    options.Seed,
    Dimension = options.Dimension,
    DimensionSeed = dimensionSeed,
    Center = new[] { options.CenterX, options.CenterZ },
    options.Samples,
    options.AreaSize,
    options.MapSize,
    options.MapStep,
    options.Chunks,
    options.SearchRadius,
    Metrics = metrics,
    InitialAreaDiagnostics = initialAreaDiagnostics,
    InitialAreaMaterializationDiagnostics = new
    {
        Cold = coldMaterializationDiagnostics,
        Warm = warmMaterializationDiagnostics,
    },
    InitialAreaSynthesisTimingBreakdown = new
    {
        Cold = coldInitialAreaTimingDiagnostics,
        WarmSampleCount = warmInitialAreaTimingDiagnostics.Count,
        WarmSamples = warmInitialAreaTimingDiagnostics,
    },
    InitialAreaStageTimingDiagnostics = new
    {
        Cold = coldStageTimingDiagnostics,
        Warm = warmStageTimingDiagnostics,
        WarmSampleCount = warmStageTimingSamples.Length,
        WarmSamples = warmStageTimingSamples,
    },
    TerrainLayerGeologyModeDiagnostics = terrainLayerGeologyModeDiagnostics,
    GeologySamplingDiagnostics = geologySamplingDiagnostics,
    Notes = new[]
    {
        "Cold and warm passes use identical coordinates and independent generator instances per metric.",
        "Elapsed times include immutable query/cache cost, not Godot meshing, physics or GPU publication.",
        "Allocation counters use the current benchmark thread and are comparative diagnostics, not retained-memory truth.",
        "Initial-area stage timings isolate chunk materialization work and are diagnostic only; they are not regression thresholds.",
        "Directional face-palette profiles use operation counters and sampled Debug timings; cold and warm passes use the same fixed-seed chunk selection.",
        options.ProfileGeologySampling
            ? "Per-voxel and regional geology profiles compare the same fixed world-space voxel sample with chunk-local noise-cache lifetimes."
            : "Pass --profile-geology-sampling true to compare per-voxel and regional geology selection on representative surface biomes.",
        "Every metric records its generated-result digest so determinism can be compared across runs.",
        "Search/destination metrics include near and far world coordinates without chunk materialization.",
        options.EnforceCiBudgets
            ? "CI regression budgets are enforced only for the canonical fixed-seed fixture and include deliberate headroom over the measured baseline."
            : "No time thresholds are enforced unless --enforce-ci-budgets true is supplied.",
    },
};

var json = JsonSerializer.Serialize(
    result,
    new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
Console.WriteLine(json);
if (options.Output is { } output)
{
    File.WriteAllText(output, json);
}

if (options.MaterialMapOutput is { } materialMapOutput)
{
    var materialMapBiomes = options.MaterialMapSamplingMode is { } samplingMode
        ? new BiomeRegistry(sourceBiomeDefinitions.Select(definition =>
            definition.Geology is null
                ? definition
                : WithGeologySamplingMode(definition, samplingMode)))
        : biomes;
    RenderGeologyMaterialMap(
        NewGenerator(
            new WorldGenerationOptions(
                spawnStructures: false,
                spawnCaves: false),
            materialMapBiomes),
        blocks,
        options,
        materialMapOutput);
}

Measurement Measure(
    int operations,
    Func<BiomeWorldGenerator, ulong> work)
{
    var generator = NewGenerator();
    var beforeCold =
        GC.GetAllocatedBytesForCurrentThread();
    var timer =
        Stopwatch.StartNew();
    var coldDigest =
        work(
            generator);
    timer.Stop();
    var coldMs =
        timer.Elapsed.TotalMilliseconds;
    var coldAllocated =
        checked(
            GC.GetAllocatedBytesForCurrentThread() -
            beforeCold);

    // A single warm sample is too sensitive to shared-runner scheduling
    // noise. Take three independent timings against the same warmed generator
    // and use the median; correctness and allocation budgets remain unchanged.
    const int WarmSampleCount = 3;
    var warmTimes = new double[WarmSampleCount];
    var warmAllocations = new long[WarmSampleCount];
    for (var sample = 0; sample < WarmSampleCount; sample++)
    {
        var beforeWarm =
            GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var warmDigest =
            work(generator);
        timer.Stop();
        warmTimes[sample] = timer.Elapsed.TotalMilliseconds;
        warmAllocations[sample] = checked(
            GC.GetAllocatedBytesForCurrentThread() - beforeWarm);

        if (coldDigest != warmDigest)
        {
            throw new InvalidOperationException(
                "Worldgen cold and warm queries produced different generated results.");
        }
    }

    Array.Sort(warmTimes);
    Array.Sort(warmAllocations);
    var warmMs = warmTimes[WarmSampleCount / 2];
    var warmAllocated = warmAllocations[WarmSampleCount / 2];

    return new Measurement(
        operations,
        coldMs,
        warmMs,
        coldAllocated,
        warmAllocated,
        coldDigest);
}

static BiomeDefinition WithGeologySamplingMode(
    BiomeDefinition source,
    BiomeGeologySamplingMode samplingMode)
{
    var original = source.Geology ??
        throw new InvalidOperationException(
            $"Biome {source.Id} has no authored geology.");
    var weights = original.Blocks
        .Select((block, index) => new
        {
            Block = block,
            Weight = original.BlockWeights[index],
        })
        .ToDictionary(
            item => item.Block,
            item => item.Weight,
            StringComparer.Ordinal);
    var geology = new BiomeGeologyDefinition(
        original.BaseBlock,
        original.Blocks,
        original.Scale,
        original.VerticalScale,
        original.DetailScale,
        original.SelectionScale,
        original.Coverage,
        original.Roughness,
        weights,
        original.SurfaceCoverDepth,
        original.SurfaceCoverage,
        original.SurfaceSubmergedOnly,
        original.GrainScale,
        original.GrainVerticalScale,
        original.GrainInfluence,
        original.ClusterScale,
        original.ClusterVerticalScale,
        original.ClusterInfluence,
        original.BoundaryGrainInfluence,
        samplingMode,
        original.RegionScale,
        original.RegionVerticalScale,
        original.RegionWarpStrength);

    return new BiomeDefinition(
        source.Id,
        source.SurfaceLayout,
        source.SurfaceTerrain,
        decorations: source.Decorations,
        tints: source.Tints,
        terrain3d: source.Terrain3d,
        volumeLayout: source.VolumeLayout,
        surfaceHabitats: source.SurfaceHabitats,
        caveSpikes: source.CaveSpikes,
        palette: source.Palette,
        volumeStructures: source.VolumeStructures,
        geology: geology);
}

static object CaptureGeologySamplingDiagnostics(
    BiomeRegistry biomes,
    BlockRegistry blocks,
    ulong seed)
{
    var representativeBiomes = new[]
    {
        "asteria:overworld/plains",
        "asteria:overworld/swamp",
        "asteria:overworld/ocean",
        "asteria:overworld/mountains",
    };
    var profiles = new List<object>();

    foreach (var biomeId in representativeBiomes)
    {
        if (!biomes.Contains(biomeId))
            continue;

        var authoredDefinition = biomes.Get(biomeId);
        if (authoredDefinition.Geology is null)
            continue;

        var perVoxelDefinition = WithGeologySamplingMode(
            authoredDefinition,
            BiomeGeologySamplingMode.PerVoxel);
        var regionalDefinition = WithGeologySamplingMode(
            authoredDefinition,
            BiomeGeologySamplingMode.Regional);
        var hybridDefinition = WithGeologySamplingMode(
            authoredDefinition,
            BiomeGeologySamplingMode.RegionalHybrid);
        var sample = new BiomeSample(
            biomeId,
            [new BiomeInfluence(biomeId, 1f)]);
        var perVoxelMaterials = new BiomeSurfaceMaterialField(
            seed, [perVoxelDefinition], blocks);
        var regionalMaterials = new BiomeSurfaceMaterialField(
            seed, [regionalDefinition], blocks);
        var hybridMaterials = new BiomeSurfaceMaterialField(
            seed, [hybridDefinition], blocks);
        var perVoxelRuns = new List<GeologySamplingRun>(6);
        var regionalRuns = new List<GeologySamplingRun>(6);
        var hybridRuns = new List<GeologySamplingRun>(6);

        for (var repetition = 0; repetition < 6; repetition++)
        {
            var modes = (repetition % 3) switch
            {
                0 => new[]
                {
                    BiomeGeologySamplingMode.PerVoxel,
                    BiomeGeologySamplingMode.Regional,
                    BiomeGeologySamplingMode.RegionalHybrid,
                },
                1 => new[]
                {
                    BiomeGeologySamplingMode.Regional,
                    BiomeGeologySamplingMode.RegionalHybrid,
                    BiomeGeologySamplingMode.PerVoxel,
                },
                _ => new[]
                {
                    BiomeGeologySamplingMode.RegionalHybrid,
                    BiomeGeologySamplingMode.PerVoxel,
                    BiomeGeologySamplingMode.Regional,
                },
            };
            foreach (var mode in modes)
            {
                var measured = mode switch
                {
                    BiomeGeologySamplingMode.PerVoxel =>
                        MeasureGeologySelection(perVoxelMaterials, sample),
                    BiomeGeologySamplingMode.Regional =>
                        MeasureGeologySelection(regionalMaterials, sample),
                    BiomeGeologySamplingMode.RegionalHybrid =>
                        MeasureGeologySelection(hybridMaterials, sample),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                };
                switch (mode)
                {
                    case BiomeGeologySamplingMode.PerVoxel:
                        perVoxelRuns.Add(measured);
                        break;
                    case BiomeGeologySamplingMode.Regional:
                        regionalRuns.Add(measured);
                        break;
                    case BiomeGeologySamplingMode.RegionalHybrid:
                        hybridRuns.Add(measured);
                        break;
                }
            }
        }

        profiles.Add(new
        {
            Biome = biomeId,
            RegionScale = regionalDefinition.Geology!.RegionScale,
            RegionVerticalScale = regionalDefinition.Geology.RegionVerticalScale,
            RegionWarpStrength = regionalDefinition.Geology.RegionWarpStrength,
            VoxelsPerRun = perVoxelRuns[0].VoxelCount,
            Repetitions = perVoxelRuns.Count,
            PerVoxel = Summarize(perVoxelRuns),
            Regional = Summarize(regionalRuns),
            RegionalHybrid = Summarize(hybridRuns),
        });
    }

    return new
    {
        Seed = seed,
        ChunkCount = 8,
        ChunkLocalNoiseCache = true,
        Profiles = profiles,
    };

    static GeologySamplingRun MeasureGeologySelection(
        BiomeSurfaceMaterialField materials,
        BiomeSample sample)
    {
        const int chunksPerAxis = 2;
        const int minimumChunkX = -1;
        const int minimumChunkY = 1;
        const int minimumChunkZ = -1;
        var digest = 14695981039346656037UL;
        var voxelCount = 0;
        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();

        for (var chunkY = minimumChunkY;
             chunkY < minimumChunkY + chunksPerAxis;
             chunkY++)
        for (var chunkZ = minimumChunkZ;
             chunkZ < minimumChunkZ + chunksPerAxis;
             chunkZ++)
        for (var chunkX = minimumChunkX;
             chunkX < minimumChunkX + chunksPerAxis;
             chunkX++)
        {
            var originX = checked(chunkX * Chunk.Size);
            var originY = checked(chunkY * Chunk.Size);
            var originZ = checked(chunkZ * Chunk.Size);
            using var latticeCache =
                new WorldGenerationEntropy.ValueNoise3DLatticeCache();

            for (var localY = 0; localY < Chunk.Size; localY++)
            for (var localZ = 0; localZ < Chunk.Size; localZ++)
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var worldX = checked(originX + localX);
                var worldY = checked(originY + localY);
                var worldZ = checked(originZ + localZ);
                if (!materials.TryGeologyBlockAtCached(
                        sample,
                        worldX,
                        worldY,
                        worldZ,
                        latticeCache,
                        out var block))
                {
                    throw new InvalidOperationException(
                        $"Biome {sample.Primary} lost its geology rule during profiling.");
                }

                digest = HashNumber(digest, block.Value);
                voxelCount++;
            }
        }

        timer.Stop();
        return new GeologySamplingRun(
            timer.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - beforeBytes,
            digest,
            voxelCount);
    }

    static object Summarize(IReadOnlyList<GeologySamplingRun> runs)
    {
        var orderedTimes = runs
            .Select(run => run.ElapsedMilliseconds)
            .Order()
            .ToArray();
        var orderedAllocations = runs
            .Select(run => (double)run.AllocatedBytes)
            .Order()
            .ToArray();
        var deterministic = runs.All(run =>
            run.Digest == runs[0].Digest &&
            run.VoxelCount == runs[0].VoxelCount);
        return new
        {
            MedianMilliseconds = Median(orderedTimes),
            MedianAllocatedBytes = Median(orderedAllocations),
            Digest = runs[0].Digest,
            Deterministic = deterministic,
        };
    }

    static double Median(double[] ordered)
    {
        var middle = ordered.Length / 2;
        return (ordered.Length & 1) == 0
            ? (ordered[middle - 1] + ordered[middle]) * 0.5d
            : ordered[middle];
    }
}

static void RenderGeologyMaterialMap(
    BiomeWorldGenerator generator,
    BlockRegistry blocks,
    Arguments options,
    string outputPath)
{
    const int cellSize = 4;
    const int margin = 16;
    const int headingHeight = 32;
    const int panelGap = 20;
    var size = options.MapSize;
    var panelPixels = checked(size * cellSize);
    var panelTop = margin + headingHeight;
    const int panelCount = 4;
    var panelLefts = Enumerable.Range(0, panelCount)
        .Select(index => margin + index * (panelPixels + panelGap))
        .ToArray();
    var imageWidth = margin * 2 + panelPixels * panelCount + panelGap * (panelCount - 1);
    var imageHeight = panelTop + panelPixels + 76;
    var centerX = options.CenterX;
    var centerZ = options.CenterZ;
    if (options.MaterialMapBiome is { } targetBiome)
    {
        var target = generator.FindNearestSurfaceBiome(
            targetBiome, centerX, centerZ, options.SearchRadius)
            ?? throw new InvalidOperationException(
                $"Could not locate surface biome {targetBiome} within {options.SearchRadius} blocks.");

        // Locate an elevated representative inside the requested biome instead
        // of accidentally centering on a low-relief border cell.
        centerX = target.X;
        centerZ = target.Z;
        var highestSurface = generator.SurfaceHeight(centerX, centerZ);
        const int peakSearchRadius = 256;
        const int peakSearchStep = 8;
        for (var dz = -peakSearchRadius; dz <= peakSearchRadius; dz += peakSearchStep)
        for (var dx = -peakSearchRadius; dx <= peakSearchRadius; dx += peakSearchStep)
        {
            var x = checked(target.X + dx);
            var z = checked(target.Z + dz);
            if (!string.Equals(
                    generator.Biomes.Sample(x, z).Primary,
                    targetBiome,
                    StringComparison.Ordinal))
                continue;

            var height = generator.SurfaceHeight(x, z);
            if (height > highestSurface)
            {
                highestSurface = height;
                centerX = x;
                centerZ = z;
            }
        }
    }

    var centerY = Math.Max(0, generator.SurfaceHeight(centerX, centerZ));
    var subsurfaceY = Math.Max(0, centerY - 32);
    var verticalRadius = Math.Min(64, Math.Max(16, centerY));
    var startX = checked(centerX - ((size - 1) / 2) * options.MapStep);
    var startZ = checked(centerZ - ((size - 1) / 2) * options.MapStep);
    var chunkCache = new Dictionary<ChunkCoord, Chunk>();

    BlockRuntimeId SampleBlock(int x, int y, int z)
    {
        var (coord, local) = VoxelCoordinates.FromWorld(x, Math.Max(0, y), z);
        if (!chunkCache.TryGetValue(coord, out var chunk))
        {
            chunk = generator.Materialize(coord);
            chunkCache.Add(coord, chunk);
        }

        return chunk.GetBlock(local.X, local.Y, local.Z);
    }

    var surface = new BlockRuntimeId[size, size];
    var horizontal = new BlockRuntimeId[size, size];
    var subsurface = new BlockRuntimeId[size, size];
    var vertical = new BlockRuntimeId[size, size];
    for (var row = 0; row < size; row++)
    {
        var z = checked(startZ + row * options.MapStep);
        var worldY = Math.Max(0,
            centerY + verticalRadius -
            (int)Math.Round(row * (2d * verticalRadius) / Math.Max(1, size - 1)));
        for (var column = 0; column < size; column++)
        {
            var x = checked(startX + column * options.MapStep);
            var topY = Math.Max(0, generator.SurfaceHeight(x, z));
            var verticalX = checked(centerX - verticalRadius +
                (int)Math.Round(column * (2d * verticalRadius) /
                    Math.Max(1, size - 1)));
            surface[row, column] = SampleBlock(x, topY, z);
            horizontal[row, column] = SampleBlock(x, centerY, z);
            subsurface[row, column] = SampleBlock(x, subsurfaceY, z);
            vertical[row, column] = SampleBlock(verticalX, worldY, centerZ);
        }
    }

    var legendBlocks = new[]
    {
        "asteria:stone", "asteria:sand", "asteria:gravel", "asteria:clay",
        "asteria:cobble_stone", "asteria:dirt", "asteria:sandstone",
        "asteria:basalt", "asteria:cobble_basalt", "asteria:ice",
        "asteria:snow", "asteria:mud", "asteria:block_grass",
    };
    var svg = new StringBuilder();
    svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{imageWidth}\" height=\"{imageHeight}\" viewBox=\"0 0 {imageWidth} {imageHeight}\">");
    svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#10141a\"/>");
    svg.AppendLine("<style>text{font-family:monospace;fill:#e8edf2}.label{font-size:12px}.title{font-size:15px;font-weight:bold}</style>");
    var targetLabel = options.MaterialMapBiome is { } requestedBiome
        ? $" · target {requestedBiome}"
        : string.Empty;
    svg.AppendLine($"<text x=\"{margin}\" y=\"19\" class=\"title\">Asteria geology materialization · seed {options.Seed} · center {centerX},{centerZ}{targetLabel} · surface Y {centerY}</text>");

    AppendPanel(svg, "Exposed surface", surface, panelLefts[0], panelTop);
    AppendPanel(svg, $"Horizontal slice Y={centerY}", horizontal, panelLefts[1], panelTop);
    AppendPanel(svg, $"Subsurface slice Y={subsurfaceY}", subsurface, panelLefts[2], panelTop);
    AppendPanel(svg, $"Local vertical slice X={centerX}±{verticalRadius}, Z={centerZ}", vertical, panelLefts[3], panelTop);

    var legendY = panelTop + panelPixels + 28;
    var legendX = margin;
    foreach (var id in legendBlocks)
    {
        var color = MaterialColor(id);
        svg.AppendLine($"<rect x=\"{legendX}\" y=\"{legendY - 10}\" width=\"10\" height=\"10\" fill=\"{color}\"/>");
        svg.AppendLine($"<text x=\"{legendX + 14}\" y=\"{legendY - 1}\" class=\"label\">{id.Replace("asteria:", "")}</text>");
        legendX += 92;
    }

    svg.AppendLine("</svg>");
    var fullPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, svg.ToString());
    Console.WriteLine($"Geology materialization map: {fullPath} ({chunkCache.Count} generated chunks)");

    void AppendPanel(
        StringBuilder builder, string label, BlockRuntimeId[,] values,
        int left, int top)
    {
        builder.AppendLine($"<text x=\"{left}\" y=\"{top - 7}\" class=\"label\">{label}</text>");
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            var block = values[row, column];
            var color = block.IsAir
                ? "#10141a"
                : MaterialColor(blocks.GetDefinition(block).Id);
            var x = left + column * cellSize;
            var y = top + row * cellSize;
            builder.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{cellSize}\" height=\"{cellSize}\" fill=\"{color}\"/>");
        }
    }
}

static string MaterialColor(string id) => id switch
{
    "asteria:stone" => "#858b92",
    "asteria:sand" => "#d6bd79",
    "asteria:gravel" => "#9a958a",
    "asteria:clay" => "#b38b72",
    "asteria:cobble_stone" => "#62666d",
    "asteria:dirt" => "#79543a",
    "asteria:sandstone" => "#bd8d58",
    "asteria:basalt" => "#3c4148",
    "asteria:cobble_basalt" => "#66605a",
    "asteria:ice" => "#8dd9e8",
    "asteria:snow" => "#eef2f5",
    "asteria:mud" => "#584939",
    "asteria:block_grass" => "#54884a",
    _ => "#d85bb8",
};

static (int X, int Z) Position(Arguments options, int index) =>
(
    checked(options.CenterX + (index % 8) * 11),
    checked(options.CenterZ - (index / 8) * 13)
);

static void EnforceCiBudgets(
    Arguments options,
    IReadOnlyDictionary<string, Measurement> metrics)
{
    const ulong ciSeed =
        181960897289965UL;

    if (options.Seed != ciSeed ||
        !string.Equals(
            options.Dimension,
            "asteria:overworld",
            StringComparison.Ordinal) ||
        options.CenterX != 0 ||
        options.CenterZ != 0 ||
        options.Samples != 2 ||
        options.AreaSize != 4 ||
        options.MapSize != 64 ||
        options.MapStep != 4 ||
        options.Chunks != 1 ||
        options.SearchRadius != 1024)
    {
        throw new InvalidOperationException(
            "--enforce-ci-budgets requires the canonical fixed-seed CI fixture.");
    }

    var budgets =
        new[]
        {
            new BenchmarkBudget(
                "biomeMap",
                MaximumColdMs: 250d,
                MaximumWarmMs: 150d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "biomeSearchNear",
                MaximumColdMs: 250d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 8L * 1024L * 1024L),
            new BenchmarkBudget(
                "biomeSearchFar",
                MaximumColdMs: 250d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 8L * 1024L * 1024L),
            new BenchmarkBudget(
                "destinationNear",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 512L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 16L * 1024L * 1024L),
            new BenchmarkBudget(
                "destinationFar",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 768L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 16L * 1024L * 1024L),
            new BenchmarkBudget(
                "structureSearchNear",
                MaximumColdMs: 2000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 1536L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "structureSearchFar",
                MaximumColdMs: 2000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 1792L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "chunkSynthesis",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 768L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
        };

    foreach (var budget in budgets)
    {
        if (!metrics.TryGetValue(
                budget.Metric,
                out var measurement))
        {
            throw new InvalidOperationException(
                $"Missing benchmark metric {budget.Metric}.");
        }

        var failures =
            new List<string>();

        if (measurement.ColdMs >
            budget.MaximumColdMs)
        {
            failures.Add(
                $"cold {measurement.ColdMs:F3}ms > {budget.MaximumColdMs:F3}ms");
        }

        if (measurement.WarmMs >
            budget.MaximumWarmMs)
        {
            failures.Add(
                $"warm {measurement.WarmMs:F3}ms > {budget.MaximumWarmMs:F3}ms");
        }

        if (measurement.ColdAllocatedBytes >
            budget.MaximumColdAllocatedBytes)
        {
            failures.Add(
                $"cold alloc {measurement.ColdAllocatedBytes} > {budget.MaximumColdAllocatedBytes}");
        }

        if (measurement.WarmAllocatedBytes >
            budget.MaximumWarmAllocatedBytes)
        {
            failures.Add(
                $"warm alloc {measurement.WarmAllocatedBytes} > {budget.MaximumWarmAllocatedBytes}");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Worldgen benchmark budget failed for {budget.Metric}: {string.Join(", ", failures)}.");
        }
    }
}

static ulong HashBiomeSearch(
    SurfaceBiomeSearchResult? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashNumber(
        digest,
        unchecked((ulong)result.X));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Z));
    return HashText(
        digest,
        result.Sample.Primary);
}

static ulong HashDestination(
    GeneratedSurfaceDestination? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.X));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.Y));
    return HashNumber(
        digest,
        unchecked((ulong)result.Value.Z));
}

static ulong HashStructureSearch(
    SurfaceStructureQueryResult? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashText(
        digest,
        result.Value.Reference);
    digest = HashText(
        digest,
        result.Value.StructureId);
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorX));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorY));
    return HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorZ));
}

static int OffsetWorldAxis(
    int value,
    int offset)
{
    var result =
        (long)value +
        offset;
    return result switch
    {
        <= int.MinValue =>
            int.MinValue,
        >= int.MaxValue =>
            int.MaxValue,
        _ =>
            (int)result,
    };
}

static ulong HashText(ulong current, string value)
{
    foreach (var character in value)
    {
        current = HashNumber(current, character);
    }

    return current;
}

static ulong HashNumber(ulong current, ulong value) =>
    unchecked((current ^ value) * 1099511628211UL);

internal sealed record Measurement(
    int Operations,
    double ColdMs,
    double WarmMs,
    long ColdAllocatedBytes,
    long WarmAllocatedBytes,
    ulong Digest);

internal sealed record GeologySamplingRun(
    double ElapsedMilliseconds,
    long AllocatedBytes,
    ulong Digest,
    int VoxelCount);

internal sealed record TerrainLayerGeologyModeSummary(
    double MedianElapsedMs,
    long MedianAllocatedBytes,
    double MinimumElapsedMs,
    double MaximumElapsedMs,
    ulong[] Digests,
    bool DeterministicAcrossRepetitions,
    long[] SolidVoxelsPerRun);

internal sealed record BenchmarkBudget(
    string Metric,
    double MaximumColdMs,
    double MaximumWarmMs,
    long MaximumColdAllocatedBytes,
    long MaximumWarmAllocatedBytes);

internal sealed record Arguments(
    string ProjectRoot,
    string Pack,
    string Dimension,
    ulong Seed,
    int CenterX,
    int CenterZ,
    int Samples,
    int AreaSize,
    int MapSize,
    int MapStep,
    int Chunks,
    int SearchRadius,
    bool EnforceCiBudgets,
    bool ProfileGeologySampling,
    BiomeGeologySamplingMode? MaterialMapSamplingMode,
    string? Output,
    string? MaterialMapOutput,
    string? MaterialMapBiome)
{
    public static Arguments Parse(string[] input)
    {
        var root = Directory.GetCurrentDirectory();
        var pack = "default";
        var dimension = "asteria:overworld";
        var seed = 181960897289965UL;
        var x = 0;
        var z = 0;
        var samples = 16;
        var areaSize = 16;
        var mapSize = 64;
        var mapStep = 4;
        var chunks = 2;
        var searchRadius = 1024;
        var enforceCiBudgets = false;
        var profileGeologySampling = false;
        BiomeGeologySamplingMode? materialMapSamplingMode = null;
        string? output = null;
        string? materialMapOutput = null;
        string? materialMapBiome = null;

        if (input.Length % 2 != 0)
        {
            throw new ArgumentException(
                "CLI flags require values.");
        }

        for (var i = 0; i < input.Length; i += 2)
        {
            var value = input[i + 1];
            switch (input[i])
            {
                case "--project-root":
                    root = value;
                    break;
                case "--pack":
                    pack = value;
                    break;
                case "--dimension":
                    dimension = value;
                    break;
                case "--seed":
                    if (!WorldCreationSeed.TryParse(value, out seed))
                    {
                        throw new ArgumentException("Invalid unsigned 64-bit seed.");
                    }

                    break;
                case "--center-x":
                    x = int.Parse(value);
                    break;
                case "--center-z":
                    z = int.Parse(value);
                    break;
                case "--samples":
                    samples = int.Parse(value);
                    break;
                case "--area-size":
                    areaSize = int.Parse(value);
                    break;
                case "--map-size":
                    mapSize = int.Parse(value);
                    break;
                case "--map-step":
                    mapStep = int.Parse(value);
                    break;
                case "--chunks":
                    chunks = int.Parse(value);
                    break;
                case "--search-radius":
                    searchRadius = int.Parse(value);
                    break;
                case "--enforce-ci-budgets":
                    enforceCiBudgets =
                        bool.Parse(
                            value);
                    break;
                case "--profile-geology-sampling":
                    profileGeologySampling = bool.Parse(value);
                    break;
                case "--material-map-sampling-mode":
                    materialMapSamplingMode = value switch
                    {
                        "per-voxel" => BiomeGeologySamplingMode.PerVoxel,
                        "regional" => BiomeGeologySamplingMode.Regional,
                        "regional-hybrid" => BiomeGeologySamplingMode.RegionalHybrid,
                        var mode => throw new ArgumentException(
                            $"Unknown material-map-sampling-mode: {mode}."),
                    };
                    break;
                case "--output":
                    output = value;
                    break;
                case "--material-map-output":
                    materialMapOutput = value;
                    break;
                case "--material-map-biome":
                    materialMapBiome = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {input[i]}.");
            }
        }

        if (samples is < 1 or > 4096 ||
            areaSize is < 1 or > 128 ||
            mapSize is < 1 or > 512 ||
            mapStep is < 1 or > 4096 ||
            chunks is < 1 or > 16 ||
            searchRadius is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Samples must be 1–4096, area-size 1–128, map-size 1–512, map-step 1–4096, chunks 1–16 and search-radius 1–8192.");
        }

        return new Arguments(
            root,
            pack,
            dimension,
            seed,
            x,
            z,
            samples,
            areaSize,
            mapSize,
            mapStep,
            chunks,
            searchRadius,
            enforceCiBudgets,
            profileGeologySampling,
            materialMapSamplingMode,
            output,
            materialMapOutput,
            materialMapBiome);
    }
}


internal struct DirectionalFacePaletteLookupEntryLayout
{
    public int HashCode;
    public int Next;
    public DirectionalFaceVoxelData Key;
    public ushort Value;
}
