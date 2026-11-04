using System.Buffers;

#if DEBUG
using System.Diagnostics;
#endif

namespace Asteria.Core.World;

/// <summary>
/// Only the materialization stage writes chunk voxels. It consumes immutable
/// terrain/biome snapshots and material/decorator capabilities.
/// </summary>
public sealed class SurfaceChunkMaterializer
{
    private readonly record struct DirectionalFaceOccupancyPreparationProfile(
        double DensitySlabReadMilliseconds,
        double HorizontalBoundaryReadMilliseconds,
        double OccupancyRowAssemblyMilliseconds,
        double CandidateMaskBuildMilliseconds,
        int HorizontalBoundaryBatchCount,
        int HorizontalBoundaryColumnCount);

    private readonly record struct BoundaryMaskReadMetrics(
        int DensityProbes,
        int BatchCount,
        int ColumnCount);

    // A 16x16 chunk plus its one-column cardinal slope-probe halo is 18x18.
    private const int SlopeProbeSampleCacheCapacity =
        (Chunk.Size + 2) * (Chunk.Size + 2);

    private struct ColumnMaterializationCache
    {
        public int SurfaceY;
        public int BaseY;
        public int SurfaceFluidCutDepth;
        public int WorldX;
        public int WorldZ;
        public BiomeSample? Sample;
        public (BlockRuntimeId Block, uint StartDepth) SurfaceCore;
        public uint? SurfaceCoverDepth;
        public bool HasSurfaceMaterialMetadata;
        public BiomeSurfaceMaterialColumn? SurfaceMaterials;
        public BiomeSample? VolumeSample;
        public BiomeSurfaceMaterialColumn? VolumeMaterials;
        public uint? VolumeCoverDepth;
        public GeneratedFluidColumnPlan GeneratedFluidPlan;
    }

    private readonly record struct DirectionalPaletteColumnData(
        BiomeSample SurfaceBiome,
        BiomeSample? EmptyRegionBiome,
        BiomeSample? VolumeBiome,
        int BaseY,
        int SurfaceFluidCutDepth,
        BiomeDirectionalPalette EmptyRegionPalette);

    private sealed class DirectionalFaceOccupancyMasks
    {
        private readonly int _maximumDepth;
        private readonly int _wordCount;
        private readonly ulong[] _xRows;
        private readonly ulong[] _zRows;
        private readonly ulong[] _yRows;
        private readonly ushort[] _candidateColumns;

        public DirectionalFaceOccupancyMasks(
            int maximumDepth,
            int wordCount,
            ulong[] xRows,
            ulong[] zRows,
            ulong[] yRows,
            ushort[] candidateColumns)
        {
            _maximumDepth = maximumDepth;
            _wordCount = wordCount;
            _xRows = xRows;
            _zRows = zRows;
            _yRows = yRows;
            _candidateColumns = candidateColumns;
        }

        public ushort[] CandidateColumns => _candidateColumns;

        public DirectionalFaceVoxelData AtCandidate(int x, int y, int z)
        {
            if ((uint)x >= Chunk.Size ||
                (uint)y >= Chunk.Size ||
                (uint)z >= Chunk.Size)
                throw new ArgumentOutOfRangeException(nameof(x));
            if (_maximumDepth == 0)
                return default;
            var columnIndex = z * Chunk.Size + x;
            var ySourceIndex = _maximumDepth + y;
            var yRowOffset = columnIndex * _wordCount;

            var xRowOffset = (y * Chunk.Size + z) * _wordCount;
            var zRowOffset = (y * Chunk.Size + x) * _wordCount;
            var xSourceIndex = _maximumDepth + x;
            var zSourceIndex = _maximumDepth + z;

            var up = EmptyOffsets(
                _yRows, yRowOffset, _wordCount,
                ySourceIndex, positive: true, _maximumDepth);
            var negativeX = EmptyOffsets(
                _xRows, xRowOffset, _wordCount,
                xSourceIndex, positive: false, _maximumDepth);
            var positiveX = EmptyOffsets(
                _xRows, xRowOffset, _wordCount,
                xSourceIndex, positive: true, _maximumDepth);
            var negativeZ = EmptyOffsets(
                _zRows, zRowOffset, _wordCount,
                zSourceIndex, positive: false, _maximumDepth);
            var positiveZ = EmptyOffsets(
                _zRows, zRowOffset, _wordCount,
                zSourceIndex, positive: true, _maximumDepth);
            var down = EmptyOffsets(
                _yRows, yRowOffset, _wordCount,
                ySourceIndex, positive: false, _maximumDepth);
            if ((up | negativeX | positiveX | negativeZ | positiveZ | down) == 0)
                return default;

            return new DirectionalFaceVoxelData(
                true, up, negativeX, positiveX, negativeZ, positiveZ, down,
                BlockRuntimeId.Air);
        }

        private static ulong ReverseBits(ulong bits)
        {
            bits = ((bits & 0x5555555555555555UL) << 1) |
                ((bits >> 1) & 0x5555555555555555UL);
            bits = ((bits & 0x3333333333333333UL) << 2) |
                ((bits >> 2) & 0x3333333333333333UL);
            bits = ((bits & 0x0F0F0F0F0F0F0F0FUL) << 4) |
                ((bits >> 4) & 0x0F0F0F0F0F0F0F0FUL);
            bits = ((bits & 0x00FF00FF00FF00FFUL) << 8) |
                ((bits >> 8) & 0x00FF00FF00FF00FFUL);
            bits = ((bits & 0x0000FFFF0000FFFFUL) << 16) |
                ((bits >> 16) & 0x0000FFFF0000FFFFUL);
            return (bits << 32) | (bits >> 32);
        }

        private static ulong ReadBits(
            ulong[] rows, int rowOffset, int wordCount, int start, int length)
        {
            var word = start >> 6;
            var shift = start & 63;
            var bits = rows[rowOffset + word] >> shift;
            if (shift != 0 && word + 1 < wordCount)
                bits |= rows[rowOffset + word + 1] << (64 - shift);
            return bits;
        }

        private static ulong EmptyOffsets(
            ulong[] rows,
            int rowOffset,
            int wordCount,
            int sourceIndex,
            bool positive,
            int depth)
        {
            var mask = depth == 64 ? ulong.MaxValue : (1UL << depth) - 1UL;
            if (positive)
                return ~ReadBits(
                    rows, rowOffset, wordCount, sourceIndex + 1, depth) & mask;

            var ascending = ReadBits(
                rows, rowOffset, wordCount, sourceIndex - depth, depth);
            var reversedSolids = ReverseBits(ascending) >> (64 - depth);
            return ~reversedSolids & mask;
        }
    }

    private readonly record struct PendingGeneratedObject(
        int X, int Y, int Z, string Id, BlockFace Face,
        bool Submerged = false, FluidRuntimeId SupportFluid = default)
    {
        public (int X, int Y, int Z) Target => Face switch
        {
            BlockFace.Top => (X, Y + 1, Z),
            BlockFace.Bottom => (X, Y - 1, Z),
            BlockFace.Right => (X + 1, Y, Z),
            BlockFace.Left => (X - 1, Y, Z),
            BlockFace.Front => (X, Y, Z + 1),
            BlockFace.Back => (X, Y, Z - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(Face))
        };
    }

    private readonly SurfaceTerrainColumnCache _columns;
    private readonly SurfaceTerrainField _terrain;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly SurfaceDecorationField _decorations;
    private readonly GeneratedFluidField _generatedFluids;
    private readonly BlockRegistry _blocks;
    private readonly HashSet<BlockRuntimeId> _gravityBlocks;
    private readonly SurfaceStructureField _structures;
    private readonly Func<FluidRuntimeId, int, int, int, int, bool>
        _nearbyFluidQuery;
    private readonly VolumeBiomeField? _volumeBiomes;
    private readonly VoidSpawnPlatform? _voidSpawnPlatform;
    private readonly CaveSpikeField? _caveSpikes;
    private readonly VolumeStructureField? _volumeStructures;
    private readonly LayerVeinField? _veins;
    private readonly BlockRuntimeId _shellBlock;
    private readonly int? _floorY;
    private readonly int? _roofY;

    public SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        GeneratedFluidField generatedFluids,
        SurfaceStructureField structures,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        VolumeBiomeField? volumeBiomes = null)
        : this(columns, terrain, materials, decorations, generatedFluids,
            structures, dimension, blocks, volumeBiomes, null)
    {
    }

    internal SurfaceChunkMaterializer(
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainField terrain,
        BiomeSurfaceMaterialField materials,
        SurfaceDecorationField decorations,
        GeneratedFluidField generatedFluids,
        SurfaceStructureField structures,
        DimensionDefinition dimension,
        BlockRegistry blocks,
        VolumeBiomeField? volumeBiomes,
        VoidSpawnPlatform? voidSpawnPlatform,
        CaveSpikeField? caveSpikes = null,
        VolumeStructureField? volumeStructures = null,
        LayerVeinField? veins = null)
    {
        _columns = columns ??
            throw new ArgumentNullException(nameof(columns));
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _materials = materials ??
            throw new ArgumentNullException(nameof(materials));
        _decorations = decorations ??
            throw new ArgumentNullException(nameof(decorations));
        _generatedFluids = generatedFluids ??
            throw new ArgumentNullException(nameof(generatedFluids));
        _structures = structures ??
            throw new ArgumentNullException(nameof(structures));
        _volumeBiomes = volumeBiomes;
        _voidSpawnPlatform = voidSpawnPlatform;
        _caveSpikes = caveSpikes;
        _volumeStructures = volumeStructures;
        _veins = veins;
        _nearbyFluidQuery = HasNearbyFluid;
        ArgumentNullException.ThrowIfNull(dimension);
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _gravityBlocks = blocks.AuthoredDefinitions()
            .Where(entry => entry.Definition.HasTag(BlockPhysicsCapabilities.Gravity))
            .Select(entry => entry.RuntimeId)
            .ToHashSet();

        // Flat and Normal respect authored Sphere Shells; Void does not.
        _shellBlock = voidSpawnPlatform is null && dimension.Shell is { } shell
            ? blocks.GetId(shell.Block)
            : BlockRuntimeId.Air;
        _floorY = voidSpawnPlatform is null ? dimension.Shell?.FloorY : null;
        _roofY = voidSpawnPlatform is null ? dimension.Shell?.RoofY : null;
    }

    public Chunk Materialize(ChunkCoord coord)
    {
        using var job = BeginMaterialization(coord);
        while (!job.IsComplete)
            job.Advance();

        return job.Result ??
            throw new InvalidOperationException(
                "Completed materialization job has no chunk result.");
    }

    public IChunkMaterializationJob BeginMaterialization(ChunkCoord coord) =>
        new MaterializationJob(this, coord);

    private void Advance(MaterializationJob job)
    {
        if (job.IsComplete)
            throw new InvalidOperationException(
                "A completed materialization job cannot advance.");

        switch (job.Stage)
        {
            case ChunkMaterializationStage.TerrainSetup:
                job.InitializeChunk();
                if (_voidSpawnPlatform is not null)
                {
                    _voidSpawnPlatform.Apply(job.Chunk, job.Coord);
                    job.Chunk.MarkDirectionalFaceDataInitialized(updateRevision: false);
                    job.Result = job.Chunk;
                    job.Stage = ChunkMaterializationStage.Complete;
                    break;
                }

                PrepareTerrain(job);
                if (job.FirstTerrainWorldY <= job.LastTerrainWorldY)
                {
                    job.Stage = ChunkMaterializationStage.TerrainLayer;
                }
                else
                {
                    CompleteInitialBlockWrites(job);
                    job.Stage = ChunkMaterializationStage.SurfaceDecorations;
                }
                break;

            case ChunkMaterializationStage.TerrainLayer:
                MaterializeTerrainLayer(job);
                if (job.NextTerrainLocalY > job.LastTerrainWorldY - job.OriginY)
                {
                    CompleteInitialBlockWrites(job);
                    if (job.SupportFaceFastPath)
                    {
                        job.Result = job.Chunk;
                        job.Stage = ChunkMaterializationStage.Complete;
                    }
                    else
                    {
                        job.Stage = ChunkMaterializationStage.SurfaceDecorations;
                    }
                }
                break;

            case ChunkMaterializationStage.SurfaceDecorations:
                MaterializeSurfaceDecorations(job);
                // A support-only job returns only the upper face's occupancy.
                // Cave/exterior palettes normally recolor already-solid cells;
                // when every loaded palette is occupancy-preserving, those
                // full-chunk scans cannot change the support-face answer.
                job.Stage = job.SupportOnly &&
                    _materials.FacePalettesPreserveOccupancy
                        ? ChunkMaterializationStage.VolumeStructures
                        : _materials.HasDirectionalFacePalettes
                            ? ChunkMaterializationStage.DirectionalFacePalettes
                            : ChunkMaterializationStage.VolumeStructures;
                break;

            case ChunkMaterializationStage.DirectionalFacePalettes:
                MaterializeDirectionalFacePalettes(job);
                job.Stage = ChunkMaterializationStage.VolumeStructures;
                break;

            case ChunkMaterializationStage.VolumeStructures:
                MaterializeVolumeStructures(job.Chunk, job.Coord);
                job.Stage = ChunkMaterializationStage.GeneratedFluids;
                break;

            case ChunkMaterializationStage.GeneratedFluids:
                MaterializeGeneratedFluids(
                    job.Chunk, RequiredDensity(job),
                    job.ColumnMaterializationCaches ??
                        throw new InvalidOperationException(
                            "Materialization column cache is missing."),
                    job.OriginY, job.TopExclusive);
                job.Stage = ChunkMaterializationStage.SurfaceStructures;
                break;

            case ChunkMaterializationStage.SurfaceStructures:
                MaterializeStructures(
                    job.Chunk, job.Coord, job.OriginY, job.TopExclusive);
                job.Stage = ChunkMaterializationStage.CaveSpikes;
                break;

            case ChunkMaterializationStage.CaveSpikes:
                MaterializeCaveSpikes(
                    job.Chunk, RequiredColumn(job), RequiredDensity(job),
                    job.OriginX, job.OriginY, job.OriginZ);
                job.Stage = ChunkMaterializationStage.VerticalDecorations;
                break;

            case ChunkMaterializationStage.VerticalDecorations:
            {
                var pendingObjects = job.PendingObjects;
                MaterializeVerticalDecorations(
                    job.Chunk, RequiredColumn(job), RequiredDensity(job),
                    job.OriginX, job.OriginY, job.OriginZ,
                    ref pendingObjects,
                    includeObjects: !job.SupportOnly);
                job.PendingObjects = pendingObjects;
                job.Stage = ChunkMaterializationStage.Veins;
                break;
            }

            case ChunkMaterializationStage.Veins:
                // Veins only attach face-layer state; they cannot change voxel
                // occupancy and are irrelevant to a support-face query.
                if (!job.SupportOnly)
                    _veins?.Apply(job.Chunk, job.Coord);
                job.Stage = ChunkMaterializationStage.WallLayers;
                break;

            case ChunkMaterializationStage.WallLayers:
                // Wall coatings and wall Objects are presentation attachments.
                // Physical wall blocks are still generated by WallObjects.
                if (!job.SupportOnly)
                {
                    var pendingObjects = job.PendingObjects;
                    MaterializeWallLayers(
                        job.Chunk, RequiredColumn(job), RequiredDensity(job),
                        job.OriginX, job.OriginY, job.OriginZ,
                        ref pendingObjects);
                    job.PendingObjects = pendingObjects;
                }
                job.Stage = ChunkMaterializationStage.WallObjects;
                break;

            case ChunkMaterializationStage.WallObjects:
                MaterializeWallObjects(
                    job.Chunk, RequiredColumn(job), RequiredDensity(job),
                    job.OriginX, job.OriginY, job.OriginZ);
                job.Stage = ChunkMaterializationStage.ShellFinalization;
                break;

            case ChunkMaterializationStage.ShellFinalization:
                ConcealSphereShell(
                    job.Chunk, RequiredColumn(job), job.OriginY, job.TopExclusive);
                job.Stage = ChunkMaterializationStage.GravitySupport;
                break;

            case ChunkMaterializationStage.GravitySupport:
                AdvanceGravitySupport(job);
                break;

            case ChunkMaterializationStage.StructureObjects:
                MaterializeStructureObjects(job.Chunk, job.Coord);
                job.Stage = ChunkMaterializationStage.PendingObjects;
                break;

            case ChunkMaterializationStage.PendingObjects:
                MaterializePendingObjects(job);
                job.Result = job.Chunk;
                job.Stage = ChunkMaterializationStage.Complete;
                break;

            case ChunkMaterializationStage.ProviderFallback:
            case ChunkMaterializationStage.Complete:
            default:
                throw new InvalidOperationException(
                    $"Unsupported materialization stage: {job.Stage}.");
        }
    }

    private static SurfaceTerrainColumn RequiredColumn(MaterializationJob job) =>
        job.Column ??
        throw new InvalidOperationException("Materialization column is missing.");

    private static TerrainDensityVolume RequiredDensity(MaterializationJob job) =>
        job.DensityVolume ??
        throw new InvalidOperationException("Terrain density snapshot is missing.");

    private static void CompleteInitialBlockWrites(MaterializationJob job)
    {
        job.InitialBlocks?.Complete();
        job.InitialBlocks = null;
        if (job.DirectionalFaceOccupancyMasks is not null)
        {
            job.Chunk.MarkDirectionalFaceDataInitialized(updateRevision: false);
            job.DirectionalFaceOccupancyMasks = null;
        }
    }

    private void PrepareTerrain(MaterializationJob job)
    {
        var coord = job.Coord;
        var originX = job.OriginX;
        var originY = job.OriginY;
        var originZ = job.OriginZ;
        var initialBlocks = job.InitialBlocks ??
            throw new InvalidOperationException("Terrain writes are already complete.");

        var column = job.Column = _columns.Get(coord.X, coord.Z);
        var densityVolume = job.DensityVolume = job.SupportFaceFastPath
            ? _terrain.CreateLazyDensityVolume(column, originX, originY, originZ)
            : _columns.SampleDensityVolume(
                column,
                originX,
                originY,
                originZ);

        if (!job.SupportFaceFastPath)
        {
            // Transpose the compact X/Z column masks into ordered candidate
            // columns per Y. TerrainLayer can then visit only solid cells,
            // while retaining the exact original Y/Z/X write order.
            var candidates =
                ArrayPool<ushort>.Shared.Rent(Chunk.Area * Chunk.Size);
            var counts =
                ArrayPool<ushort>.Shared.Rent(Chunk.Size);
            Array.Clear(counts, 0, Chunk.Size);
            for (var localZ = 0; localZ < Chunk.Size; localZ++)
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var columnIndex = localX + Chunk.Size * localZ;
                var solidMask =
                    densityVolume.SolidColumnMaskAtUnchecked(localX, localZ);
                while (solidMask != 0)
                {
                    var localY = System.Numerics.BitOperations.TrailingZeroCount(
                        (uint)solidMask);
                    var rowOffset = localY * Chunk.Area;
                    candidates[rowOffset + counts[localY]++] =
                        checked((ushort)columnIndex);
                    solidMask = (ushort)(solidMask & (solidMask - 1));
                }
            }

            job.TerrainLayerCandidatesByY = candidates;
            job.TerrainLayerCandidateCountsByY = counts;
        }
#if DEBUG
        var directionalPreparationStarted = Stopwatch.GetTimestamp();
#endif
        if (_materials.HasDirectionalFacePalettes && !job.SupportFaceFastPath)
        {
            job.DirectionalFaceOccupancyMasks = BuildDirectionalFaceOccupancyMasks(
                densityVolume, column, originX, originY, originZ,
                out var densityProbes,
                out var boundaryDensityProbes,
                out var preparationProfile);
            job.DirectionalFacePalettePreparationProfile = preparationProfile;
            job.DirectionalFacePaletteDensityProbes = densityProbes;
            job.DirectionalFacePaletteBoundaryDensityProbes = boundaryDensityProbes;
        }
#if DEBUG
        job.DirectionalFacePalettePreparationMilliseconds =
            Stopwatch.GetElapsedTime(directionalPreparationStarted).TotalMilliseconds;
#endif
        var topExclusive = job.TopExclusive = checked(originY + Chunk.Size);
        job.PendingObjects = null;
        var columnMaterializationCaches =
            job.ColumnMaterializationCaches =
                ArrayPool<ColumnMaterializationCache>.Shared.Rent(Chunk.Area);
        Array.Clear(columnMaterializationCaches, 0, Chunk.Area);

        // Resolve column-only metadata once. Terrain voxels are then visited
        // in the same Y/Z/X order as Chunk's linear storage.
        var firstTerrainWorldY = job.FirstTerrainWorldY = Math.Max(originY, _floorY ?? 0);
        var lastTerrainWorldY = job.LastTerrainWorldY = Math.Min(
            topExclusive - 1, _roofY ?? int.MaxValue);
        var maximumSurfaceY = int.MinValue;
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var index = localX + Chunk.Size * localZ;
            if (job.SupportFaceFastPath &&
                !(job.SupportFaceColumns ?? throw new InvalidOperationException(
                    "Support-face query has no requested-column mask."))[index])
                continue;

            var surfaceY = column.HeightAt(localX, localZ);
            maximumSurfaceY = Math.Max(maximumSurfaceY, surfaceY);
            columnMaterializationCaches[index] = new ColumnMaterializationCache
            {
                SurfaceY = surfaceY,
                BaseY = column.BaseHeightAt(localX, localZ),
                SurfaceFluidCutDepth =
                    column.SurfaceFluidCutDepthAt(localX, localZ),
                WorldX = checked(originX + localX),
                WorldZ = checked(originZ + localZ),
                Sample = column.BiomeAt(localX, localZ),
            };

            if (!_shellBlock.IsAir)
            {
                SetShellIfInChunk(
                    initialBlocks, localX, localZ, originY, topExclusive, _floorY);
                SetShellIfInChunk(
                    initialBlocks, localX, localZ, originY, topExclusive, _roofY);
            }
        }

        lastTerrainWorldY = Math.Min(lastTerrainWorldY, maximumSurfaceY);
        job.LastTerrainWorldY = lastTerrainWorldY;

        if (job.SupportFaceFastPath)
        {
            // Only the requested top-face cells and their immediate supports
            // are needed to establish ordinary terrain occupancy.
            job.FirstTerrainWorldY = checked(originY + Chunk.Size - 2);
            job.LastTerrainWorldY = checked(originY + Chunk.Size - 1);
            job.NextTerrainLocalY = Chunk.Size - 2;
            return;
        }

        job.NextTerrainLocalY = firstTerrainWorldY - originY;

    }

    private void MaterializeTerrainLayer(MaterializationJob job)
    {
        var originX = job.OriginX;
        var originY = job.OriginY;
        var originZ = job.OriginZ;
        var column = job.Column ??
            throw new InvalidOperationException("Materialization column is missing.");
        var densityVolume = job.DensityVolume ??
            throw new InvalidOperationException("Terrain density snapshot is missing.");
        var topExclusive = job.TopExclusive;
        var columnMaterializationCaches = job.ColumnMaterializationCaches ??
            throw new InvalidOperationException("Materialization column cache is missing.");
        var initialBlocks = job.InitialBlocks ??
            throw new InvalidOperationException("Terrain writes are already complete.");
        var directionalMasks = job.DirectionalFaceOccupancyMasks;
        var directionalCandidateColumns = directionalMasks?.CandidateColumns;
        var terrainCandidates = job.TerrainLayerCandidatesByY;
        var terrainCandidateCounts = job.TerrainLayerCandidateCountsByY;
        var usesSolidCandidates =
            terrainCandidates is not null && terrainCandidateCounts is not null;
        var localY = job.NextTerrainLocalY;

        var worldY = checked(originY + localY);
        var columnCount = usesSolidCandidates
            ? terrainCandidateCounts![localY]
            : Chunk.Area;
        for (var ordinal = 0; ordinal < columnCount; ordinal++)
        {
            var columnIndex = usesSolidCandidates
                ? terrainCandidates![localY * Chunk.Area + ordinal]
                : ordinal;
            var localZ = columnIndex / Chunk.Size;
            var localX = columnIndex - localZ * Chunk.Size;
            if (job.SupportFaceFastPath &&
                !(job.SupportFaceColumns ?? throw new InvalidOperationException(
                    "Support-face query has no requested-column mask."))[columnIndex])
                continue;

                    ref var state = ref columnMaterializationCaches[columnIndex];
                    var sample = state.Sample ??
                        throw new InvalidOperationException(
                            "Materialization column has no biome sample.");
                    var surfaceY = state.SurfaceY;
                    var baseY = state.BaseY;
                    var surfaceFluidCutDepth = state.SurfaceFluidCutDepth;
                    var worldX = state.WorldX;
                    var worldZ = state.WorldZ;

                    // Shell planes were installed before terrain writes.
                    // Skip their voxels so geology cannot overwrite them.
                    if (!_shellBlock.IsAir &&
                        (worldY == _floorY || worldY == _roofY))
                        continue;

                    if (worldY < (_floorY ?? 0) ||
                        worldY > surfaceY ||
                        worldY > (_roofY ?? int.MaxValue) ||
                        (!usesSolidCandidates &&
                         !densityVolume.IsSolidAtUnchecked(
                             localX, localY, localZ)))
                    {
                        continue;
                    }

                    if (!state.HasSurfaceMaterialMetadata)
                    {
                        state.SurfaceCore = _materials.CoreLayer(sample);
                        state.SurfaceCoverDepth =
                            _materials.GeologySurfaceCoverDepth(sample);
                        state.HasSurfaceMaterialMetadata = true;
                    }

                    var isExposedTerrainTop = worldY == surfaceY;
                    if (!isExposedTerrainTop &&
                        worldY < surfaceY &&
                        (long)surfaceY - worldY <= 2L)
                    {
                        isExposedTerrainTop = true;
                        for (var aboveY = worldY + 1;
                             aboveY <= surfaceY;
                             aboveY++)
                        {
                            var aboveLocalY = aboveY - originY;
                            if (aboveLocalY < 0 ||
                                aboveLocalY >= Chunk.Size ||
                                densityVolume.IsSolidAtUnchecked(
                                    localX, aboveLocalY, localZ))
                            {
                                isExposedTerrainTop = false;
                                break;
                            }
                        }
                    }

                    BlockRuntimeId exposedBlock = BlockRuntimeId.Air;
                    var hasExposedGeology = false;
                    if (isExposedTerrainTop)
                    {
                        var surfaceOwner = worldY > baseY
                            ? densityVolume.VolumeBiomeAt(
                                localX, worldY, localZ) ??
                              throw new InvalidOperationException(
                                  "Additive surface has no volume biome owner.")
                            : sample;
                        if (!_materials.SurfaceGeologyRequiresSubmerged(surfaceOwner) ||
                            HasGeneratedFluidAboveSurface(
                                surfaceOwner, sample, baseY,
                                surfaceFluidCutDepth, worldX, worldY, worldZ,
                                densityVolume.VolumePlacementAt(localX, localZ)))
                        {
                            hasExposedGeology = _materials.TrySurfaceGeologyBlockAtCached(
                                surfaceOwner, worldX, worldY, worldZ,
                                job.GetGeologyNoiseCache(),
                                out exposedBlock);
                        }
                    }

                    BlockRuntimeId block;
                    if (hasExposedGeology)
                    {
                        block = exposedBlock;
                    }
                    else if (worldY <= baseY)
                    {
                        var depth = checked((uint)(baseY - worldY));
                        var surfaceMaterials = state.SurfaceMaterials ??=
                            _materials.SampleColumn(sample, worldX, worldZ);
                        var useSurfaceCover =
                            state.SurfaceCoverDepth is { } authoredCoverDepth
                                ? depth < authoredCoverDepth
                                : depth == 0 || depth < surfaceMaterials.FiniteDepth;
                        if (useSurfaceCover)
                        {
                            block = surfaceMaterials.BlockAt(depth);
                        }
                        else if (state.SurfaceCoverDepth is not null &&
                                 _materials.TryGeologyBlockAtCached(
                                     sample, worldX, worldY, worldZ,
                                     job.GetGeologyNoiseCache(),
                                     out var geologyBlock))
                        {
                            block = geologyBlock;
                        }
                        else
                        {
                            // A null cover depth also means this biome has no
                            // 3D geology rule; avoid a dictionary lookup per voxel.
                            block = depth >= state.SurfaceCore.StartDepth
                                ? state.SurfaceCore.Block
                                : surfaceMaterials.BlockAt(depth);
                        }
                    }
                    else
                    {
                        var volumeSample = state.VolumeSample;
                        if (volumeSample is null)
                        {
                            volumeSample = densityVolume.VolumeBiomeAt(
                                localX, worldY, localZ) ??
                                throw new InvalidOperationException(
                                    "Additive solid voxel has no volume biome owner.");
                            state.VolumeSample = volumeSample;
                            state.VolumeCoverDepth =
                                _materials.GeologySurfaceCoverDepth(volumeSample);
                        }

                        var volumeMaterials = state.VolumeMaterials ??=
                            _materials.SampleColumn(volumeSample, worldX, worldZ);
                        var depth = AdditiveDepthAt(
                            densityVolume, sample, baseY, localX, localY, localZ,
                            worldX, worldY, worldZ, volumeMaterials.FiniteDepth);
                        var useSurfaceCover =
                            state.VolumeCoverDepth is { } authoredCoverDepth
                                ? depth < authoredCoverDepth
                                : depth == 0 || depth < volumeMaterials.FiniteDepth;
                        if (useSurfaceCover)
                        {
                            block = volumeMaterials.BlockAt(depth);
                        }
                        else if (state.VolumeCoverDepth is not null &&
                                 _materials.TryGeologyBlockAtCached(
                                     volumeSample, worldX, worldY, worldZ,
                                     job.GetGeologyNoiseCache(),
                                     out var volumeGeologyBlock))
                        {
                            block = volumeGeologyBlock;
                        }
                        else
                        {
                            // The cached nullable cover depth also identifies
                            // whether this volume biome has volumetric geology.
                            block = volumeMaterials.BlockAt(depth);
                        }
                    }

                    var voxelIndex = columnIndex + Chunk.Area * localY;
                    if (initialBlocks.SetBlockAtLinearIndex(voxelIndex, block) &&
                        !block.IsAir &&
                        directionalMasks is not null &&
                        directionalCandidateColumns is { } candidateColumns &&
                        (candidateColumns[columnIndex] & (1u << localY)) != 0)
                    {
                        var faceData = directionalMasks.AtCandidate(
                            localX, localY, localZ);
                        if (faceData.HasCandidates)
                        {
                            job.Chunk.SetInitialDirectionalFaceDataAtLinearIndex(
                                voxelIndex, faceData, updateRevision: false);
                        }
                    }
        }

        job.NextTerrainLocalY++;
    }

    private void MaterializeSurfaceDecorations(MaterializationJob job)
    {
        var chunk = job.Chunk;
        var originX = job.OriginX;
        var originY = job.OriginY;
        var originZ = job.OriginZ;
        var column = job.Column ??
            throw new InvalidOperationException("Materialization column is missing.");
        var densityVolume = job.DensityVolume ??
            throw new InvalidOperationException("Terrain density snapshot is missing.");
        // Slope conditions inspect the current and four adjacent columns.
        // Seed the query-local samplers with the immutable biome samples that
        // TerrainSetup already resolved for this chunk; only the outer halo
        // needs additional sampling.
        SurfaceTerrainField.TerrainSampleContext? terrainSampleContext =
            _decorations.HasSlopeConditions
                ? _terrain.CreateTerrainSampleContext(
                    column, originX, originZ,
                    SlopeProbeSampleCacheCapacity)
                : null;
        var topExclusive = job.TopExclusive;
        var pendingObjects = job.PendingObjects;
        var columnMaterializationCaches = job.ColumnMaterializationCaches ??
            throw new InvalidOperationException("Materialization column cache is missing.");
        // Decors are evaluated only after all terrain exists, in stable
        // column order so existing placement precedence remains unchanged.
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                ref var state = ref columnMaterializationCaches[
                    localX + Chunk.Size * localZ];
                var surfaceY = state.SurfaceY;
                var baseY = state.BaseY;
                var surfaceFluidCutDepth = state.SurfaceFluidCutDepth;
                var worldX = state.WorldX;
                var worldZ = state.WorldZ;
                var sample = state.Sample ??
                    throw new InvalidOperationException(
                        "Materialization column has no biome sample.");
                var surfaceMaterials = state.SurfaceMaterials;
                var volumeSample = state.VolumeSample;
                var volumeMaterials = state.VolumeMaterials;
                var decorationY = (long)surfaceY + 1;
                var normalInChunk = CanPlaceDecorationAt(
                    decorationY, originY, topExclusive);
                var fluidTopInChunk = CanPlaceDecorationAt(
                    decorationY + 1, originY, topExclusive);
                var fluidObjectSupportInChunk = CanPlaceDecorationAt(
                    decorationY, originY, topExclusive);
                var objectSupportInChunk = CanPlaceDecorationAt(
                    surfaceY, originY, topExclusive);
                if (!normalInChunk && !fluidTopInChunk && !objectSupportInChunk &&
                    !fluidObjectSupportInChunk)
                    continue;

                var topSample =
                    surfaceY > baseY
                        ? volumeSample ??
                          densityVolume.VolumeBiomeAt(
                              localX,
                              surfaceY,
                              localZ) ??
                          throw new InvalidOperationException(
                              "Additive surface has no volume biome owner.")
                        : sample;
                // Disconnected additive surfaces are owned by the vertical
                // decoration pass, including when they cross chunk boundaries.
                if ((surfaceY > baseY &&
                     _decorations.HasVerticalDecorationsFor(topSample.Primary)) ||
                    !_decorations.HasDecorations(topSample))
                    continue;

                var topMaterials = surfaceY > baseY
                    ? volumeMaterials ??= _materials.SampleColumn(
                        topSample, worldX, worldZ)
                    : surfaceMaterials ??= _materials.SampleColumn(
                        sample, worldX, worldZ);
                var topBlock = topMaterials.BlockAt(0);

                var considerObjects = objectSupportInChunk &&
                    _decorations.HasObjectDecorations(topSample);
                if (normalInChunk || considerObjects)
                {
                    // Generated-fluid occupancy, not only the column range,
                    // decides whether this is a submerged decorator cell.
                    var decorationTerrainSolid = normalInChunk
                        ? densityVolume.IsSolidAtUnchecked(
                            localX, (int)decorationY - originY, localZ)
                        : _terrain.DensityAt(
                            sample, baseY, worldX, (int)decorationY, worldZ,
                            densityVolume.VolumePlacementAt(localX, localZ)) >= 0d;
                    var submerged =
                        _generatedFluids.TryGetColumnBounds(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, worldZ, out var minimumY,
                            out var maximumY) &&
                        decorationY >= minimumY && decorationY <= maximumY &&
                        !_generatedFluids.FluidAt(
                            sample, baseY, surfaceFluidCutDepth,
                            worldX, (int)decorationY, worldZ,
                            decorationTerrainSolid).IsEmpty;

                    if (normalInChunk)
                    {
                        var decoration = _decorations.BlockAtWithTerrainSampleContext(
                            topSample, topBlock, worldX, worldZ,
                            suppliedPlacement: null,
                            verticalY: null,
                            submerged: submerged,
                            supportY: surfaceY,
                            onFluidSurface: false,
                            fluidBelow: default,
                            nearbyFluid: _nearbyFluidQuery,
                            supportSurface: DecorationSupportSurface.Floor,
                            terrainSampleContext: terrainSampleContext);
                        if (!decoration.IsAir)
                            chunk.SetBlock(localX, (int)decorationY - originY,
                                localZ, decoration);
                    }
                    if (!job.SupportOnly && considerObjects)
                    {
                        var objectId = _decorations.ObjectAtWithTerrainSampleContext(
                            topSample, topBlock, worldX, worldZ,
                            placement: null,
                            verticalY: null,
                            submerged: submerged,
                            supportY: surfaceY,
                            nearbyFluid: _nearbyFluidQuery,
                            supportSurface: DecorationSupportSurface.Floor,
                            onFluidSurface: false,
                            fluidBelow: default,
                            terrainSampleContext: terrainSampleContext);
                        if (objectId is not null)
                        {
                            pendingObjects ??= [];
                            pendingObjects.Add(new PendingGeneratedObject(
                                localX, surfaceY - originY, localZ,
                                objectId, BlockFace.Top, submerged));
                        }
                    }
                }

                // Water-top decorations occupy the *air above* a source,
                // not its water voxel. This also spans vertical chunk edges.
                if ((fluidTopInChunk || fluidObjectSupportInChunk) &&
                    _decorations.HasFluidSurfaceDecorations(topSample))
                {
                    var supportFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY, worldZ);
                    var aboveFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY + 1, worldZ);
                    if (!supportFluid.IsEmpty && aboveFluid.IsEmpty)
                    {
                        var decoration = _decorations.BlockAtWithTerrainSampleContext(
                            topSample, topBlock, worldX, worldZ,
                            suppliedPlacement: null,
                            verticalY: null,
                            submerged: false,
                            supportY: (int)decorationY,
                            onFluidSurface: true,
                            fluidBelow: supportFluid.Fluid,
                            nearbyFluid: _nearbyFluidQuery,
                            supportSurface: DecorationSupportSurface.Floor,
                            terrainSampleContext: terrainSampleContext);
                        if (fluidTopInChunk && !decoration.IsAir)
                            chunk.SetBlock(
                                localX, (int)decorationY + 1 - originY,
                                localZ, decoration);
                        if (!job.SupportOnly &&
                            fluidObjectSupportInChunk &&
                            _decorations.HasObjectDecorations(topSample))
                        {
                            var objectId = _decorations.ObjectAtWithTerrainSampleContext(
                                topSample, topBlock, worldX, worldZ,
                                placement: null,
                                verticalY: null,
                                submerged: false,
                                supportY: (int)decorationY,
                                nearbyFluid: _nearbyFluidQuery,
                                supportSurface: DecorationSupportSurface.Floor,
                                onFluidSurface: true,
                                fluidBelow: supportFluid.Fluid,
                                terrainSampleContext: terrainSampleContext);
                            if (objectId is not null)
                            {
                                pendingObjects ??= [];
                                pendingObjects.Add(new PendingGeneratedObject(
                                    localX, (int)decorationY - originY, localZ,
                                    objectId, BlockFace.Top,
                                    SupportFluid: supportFluid.Fluid));
                            }
                        }
                    }
                }
            }
        }


        job.PendingObjects = pendingObjects;
    }

    /// <summary>
    /// Enforces the generated-world gravity invariant after every voxel-writing
    /// stage and before Objects are attached. Unsupported deposits are replaced
    /// with their biome's stable core material; physics remains responsible for
    /// gravity blocks placed or uncovered during gameplay.
    /// </summary>
    private void AdvanceGravitySupport(MaterializationJob job)
    {
        if (_gravityBlocks.Count == 0)
        {
            CompleteGravitySupportStage(job);
            return;
        }

        var supportJobCreated = false;
        var positions = job.GravityPositions;
        if (positions is null)
        {
            positions = new List<(int X, int Y, int Z)>();
            if (job.SupportOnly)
            {
                // The parent reads only this chunk's upper face. Inspecting
                // every voxel and sorting a full-chunk candidate list repeats
                // work that cannot affect the answer.
                var supportY = Chunk.Size - 1;
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var cell = job.Chunk.GetCell(x, supportY, z);
                    if (_gravityBlocks.Contains(cell.Block))
                        positions.Add((x, supportY, z));
                }
            }
            else
            {
                job.Chunk.VisitBlockCells((x, y, z, cell) =>
                {
                    if (_gravityBlocks.Contains(cell.Block))
                        positions.Add((x, y, z));
                });

                positions.Sort(static (left, right) =>
                {
                    var result = left.Y.CompareTo(right.Y);
                    if (result != 0) return result;
                    result = left.Z.CompareTo(right.Z);
                    return result != 0 ? result : left.X.CompareTo(right.X);
                });
            }
            job.GravityPositions = positions;

            // The child dependency must survive across multiple worker slices.
            if (!job.SupportOnly && job.OriginY > 0 &&
                positions.Any(position => position.Y == 0))
            {
                var supportColumns = new bool[Chunk.Area];
                foreach (var position in positions)
                {
                    if (position.Y == 0)
                        supportColumns[position.X + Chunk.Size * position.Z] = true;
                }
                job.SupportChunkJob = new MaterializationJob(
                    this,
                    new ChunkCoord(job.Coord.X, job.Coord.Y - 1, job.Coord.Z),
                    supportOnly: true,
                    supportFaceFastPath: true,
                    supportFaceColumns: supportColumns);
                supportJobCreated = true;
            }
        }

        // Keep creation separate from advancement so the worker can attribute
        // the child job's first stage to its own coordinate and stage.
        if (supportJobCreated)
            return;

        if (job.SupportChunkJob is { IsComplete: false } pendingSupport)
        {
            // Advance the support query by one normal materialization slice.
            // The lower chunk is never published to the world by this job.
            pendingSupport.Advance();
            return;
        }

        if (job.SupportChunkJob is { IsComplete: true } completedSupport)
        {
            var supportChunk = completedSupport.Result ??
                throw new InvalidOperationException(
                    "Completed gravity-support chunk has no result.");

            if (completedSupport.SupportFaceFastPath)
            {
                var supportFace = new bool[Chunk.Area];
                var fallbackColumns = new bool[Chunk.Area];
                foreach (var (x, y, z) in positions)
                {
                    if (y != 0)
                        continue;

                    var index = x + Chunk.Size * z;
                    supportFace[index] = CanResolveSupportFaceFromTerrain(
                        completedSupport, x, z);
                    fallbackColumns[index] = !supportFace[index];
                }

                completedSupport.Dispose();
                if (fallbackColumns.Any(value => value))
                {
                    job.SupportChunkTopOccupancy = supportFace;
                    job.SupportChunkFallbackColumns = fallbackColumns;
                    job.SupportChunkJob = new MaterializationJob(
                        this,
                        new ChunkCoord(job.Coord.X, job.Coord.Y - 1, job.Coord.Z),
                        supportOnly: true);
                    return;
                }

                job.SupportChunkTopOccupancy = supportFace;
                job.SupportChunkJob = null;
            }
            else
            {
                var supportFace = job.SupportChunkTopOccupancy ??
                    throw new InvalidOperationException(
                        "Fallback gravity-support query has no fast-path result.");
                var fallbackColumns = job.SupportChunkFallbackColumns ??
                    throw new InvalidOperationException(
                        "Fallback gravity-support query has no unresolved-column mask.");
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var index = x + Chunk.Size * z;
                    if (fallbackColumns[index])
                        supportFace[index] =
                            !supportChunk.GetCell(x, Chunk.Size - 1, z).IsEmpty;
                }

                job.SupportChunkTopOccupancy = supportFace;
                job.SupportChunkFallbackColumns = null;
                completedSupport.Dispose();
                job.SupportChunkJob = null;
            }
        }

        foreach (var (x, y, z) in positions)
        {
            var worldX = job.OriginX + x;
            var worldY = job.OriginY + y;
            var worldZ = job.OriginZ + z;
            bool supported;

            if (y > 0)
            {
                var below = job.Chunk.GetCell(x, y - 1, z);
                supported = !below.IsEmpty;
            }
            else if (worldY == 0)
            {
                // The world floor is the same terminal support understood by
                // BlockMaterializationRules.BelowWorld.
                supported = true;
            }
            else
            {
                var supportFace = job.SupportChunkTopOccupancy ??
                    throw new InvalidOperationException(
                        "A bottom-seam gravity block has no generated support face.");
                supported = supportFace[x + Chunk.Size * z];
            }

            if (supported)
                continue;

            var column = RequiredColumn(job);
            var surfaceBiome = column.BiomeAt(x, z);
            var owner = ResolveGravityMaterialOwner(
                column, RequiredDensity(job), surfaceBiome,
                x, y, z, worldX, worldY, worldZ);
            var stableBlock = _materials.CoreLayer(owner).Block;
            if (stableBlock.IsAir || _gravityBlocks.Contains(stableBlock))
            {
                throw new InvalidOperationException(
                    $"Biome {owner.Primary} has no stable core material for unsupported " +
                    $"gravity block at {worldX},{worldY},{worldZ}.");
            }

            // SetBlock replaces the full VoxelCell (including sculpt mask,
            // facing and state) and clears any attached face-layer state.
            job.Chunk.SetBlock(x, y, z, stableBlock);
        }

        CompleteGravitySupportStage(job);
    }

    private bool CanResolveSupportFaceFromTerrain(
        MaterializationJob supportJob,
        int localX,
        int localZ)
    {
        // Void dimensions use a dedicated spawn-platform writer. Keep those
        // queries on the authoritative fallback path.
        if (supportJob.Column is null || supportJob.DensityVolume is null)
            return false;

        var topLocalY = Chunk.Size - 1;
        var belowLocalY = topLocalY - 1;
        var top = supportJob.Chunk.GetCell(localX, topLocalY, localZ);
        if (top.IsEmpty || !_materials.FacePalettesPreserveOccupancy)
            return false;

        var topWorldY = supportJob.OriginY + topLocalY;
        var belowWorldY = topWorldY - 1;
        if (IsSphereShellBoundaryOrLining(topWorldY) ||
            IsSphereShellBoundaryOrLining(belowWorldY))
            return false;

        var worldX = supportJob.OriginX + localX;
        var worldZ = supportJob.OriginZ + localZ;
        if (HasSurfaceStructurePayloadAt(
                supportJob.Coord, worldX, topWorldY, worldZ) ||
            HasSurfaceStructurePayloadAt(
                supportJob.Coord, worldX, belowWorldY, worldZ))
            return false;

        // A gravity voxel on the requested face must itself be stable before
        // it can be trusted as support. If its lower cell is empty, retain the
        // complete materialization path so decorations/structures and the
        // generated-gravity invariant are evaluated in their normal order.
        if (_gravityBlocks.Contains(top.Block) &&
            supportJob.Chunk.GetCell(localX, belowLocalY, localZ).IsEmpty)
            return false;

        return true;
    }

    private bool IsSphereShellBoundaryOrLining(int worldY) =>
        (_floorY is { } floorY &&
         (worldY == floorY || (long)worldY == (long)floorY + 1L)) ||
        (_roofY is { } roofY &&
         (worldY == roofY || (long)worldY == (long)roofY - 1L));

    private bool IsLocalDensityCaveVoid(
        SurfaceTerrainColumn column,
        int localX,
        int localZ,
        int worldY) =>
        IsLocalDensityCaveVoid(
            column.CaveBiomeAt(localX, localZ) is not null,
            column.BaseHeightAt(localX, localZ),
            worldY,
            IsSphereShellBoundaryOrLining(worldY));

    internal static bool IsLocalDensityCaveVoid(
        bool hasCaveBiome,
        int baseY,
        int worldY,
        bool isSphereShellBoundaryOrLining)
    {
        // Density establishes that this local voxel is empty; the assigned
        // cave biome establishes that the empty region is owned by a cave.
        // The caller has already established that the local density bit is empty.
        return hasCaveBiome &&
            worldY > 0 &&
            worldY <= baseY &&
            !isSphereShellBoundaryOrLining;
    }

    private bool HasSurfaceStructurePayloadAt(
        ChunkCoord coord,
        int worldX,
        int worldY,
        int worldZ)
    {
        if (!_structures.HasRules)
            return false;

        foreach (var placement in _structures.PlacementsForChunk(coord.X, coord.Z))
        {
            if (worldY < placement.MinimumY ||
                worldY > placement.MaximumY)
                continue;

            if (placement.ClearVoxels.Any(voxel =>
                    voxel.X == worldX && voxel.Y == worldY && voxel.Z == worldZ) ||
                placement.Voxels.Any(voxel =>
                    voxel.X == worldX && voxel.Y == worldY && voxel.Z == worldZ) ||
                placement.FluidVoxels.Any(voxel =>
                    voxel.X == worldX && voxel.Y == worldY && voxel.Z == worldZ))
                return true;
        }

        return false;
    }

    private static void CompleteGravitySupportStage(MaterializationJob job)
    {
        if (job.SupportOnly)
        {
            job.Result = job.Chunk;
            job.Stage = ChunkMaterializationStage.Complete;
        }
        else
        {
            job.Stage = ChunkMaterializationStage.StructureObjects;
        }
    }

    private BiomeSample ResolveGravityMaterialOwner(
        SurfaceTerrainColumn column,
        TerrainDensityVolume density,
        BiomeSample surfaceBiome,
        int localX,
        int localY,
        int localZ,
        int worldX,
        int worldY,
        int worldZ)
    {
        var baseY = column.BaseHeightAt(localX, localZ);
        if (worldY > baseY)
        {
            return density.VolumeBiomeAt(localX, localY, localZ) ??
                surfaceBiome;
        }

        var below = new WorldVoxelCoord(worldX, worldY - 1, worldZ);
        if (worldY > 0 &&
            _terrain.IsCaveVoidAt(baseY, below) &&
            column.CaveBiomeAt(localX, localZ) is { } caveBiome)
        {
            return caveBiome;
        }

        return surfaceBiome;
    }

    private void MaterializePendingObjects(MaterializationJob job)
    {
        var chunk = job.Chunk;
        var coord = job.Coord;
        var originY = job.OriginY;
        var pendingObjects = job.PendingObjects;
        // The supporting block owns an Object, never its adjacent fluid cell.
        // Publish only after structures, caves, and the shell are final.
        if (pendingObjects is not null)
        {
            foreach (var obj in pendingObjects)
            {
                if ((_floorY is { } floor && originY + obj.Y <= floor) ||
                    (_roofY is { } roof && originY + obj.Y >= roof))
                    continue;
                var support = chunk.GetCell(obj.X, obj.Y, obj.Z);
                if (!obj.SupportFluid.IsNone)
                {
                    var fluid = chunk.GetFluid(obj.X, obj.Y, obj.Z);
                    if (!support.IsEmpty || fluid.Fluid != obj.SupportFluid ||
                        fluid.Level != FluidCell.MaxLevel || !fluid.IsSource)
                        continue;
                }
                else if (support.IsEmpty ||
                         !_blocks.GetDefinition(support.Block).IsCollidable)
                    continue;
                var target = obj.Target;
                if (Chunk.Contains(target.X, target.Y, target.Z))
                {
                    if (!chunk.GetCell(target.X, target.Y, target.Z).IsEmpty ||
                        (obj.Submerged &&
                         chunk.GetFluid(target.X, target.Y, target.Z).IsEmpty))
                        continue;
                }
                chunk.AddObject(obj.X, obj.Y, obj.Z,
                    new ObjectCell(obj.Id, obj.Face));
            }
        }


    }

    private sealed class MaterializationJob : IChunkMaterializationJob
    {
        private readonly SurfaceChunkMaterializer _owner;
        private WorldGenerationEntropy.ValueNoise3DLatticeCache? _geologyNoiseCache;
        private Chunk? _chunk;

        public MaterializationJob(
            SurfaceChunkMaterializer owner,
            ChunkCoord coord,
            bool supportOnly = false,
            bool supportFaceFastPath = false,
            bool[]? supportFaceColumns = null)
        {
            _owner = owner;
            Coord = coord;
            SupportOnly = supportOnly;
            if (supportFaceFastPath &&
                (!supportOnly || supportFaceColumns is null ||
                 supportFaceColumns.Length != Chunk.Area ||
                 !supportFaceColumns.Any(value => value)))
                throw new ArgumentException(
                    "A support-face fast path requires a non-empty chunk-area column mask.",
                    nameof(supportFaceColumns));
            SupportFaceFastPath = supportFaceFastPath;
            SupportFaceColumns = supportFaceColumns;
            if (coord.Y < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(coord), "Sphere chunk Y cannot be negative.");

            Stage = ChunkMaterializationStage.TerrainSetup;
        }

        public ChunkCoord Coord { get; }

        public ChunkCoord ActiveWorkCoord =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveWorkCoord
                : Coord;

        public ChunkMaterializationStage ActiveWorkStage =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveWorkStage
                : Stage;

        public bool SupportOnly { get; }

        public bool SupportFaceFastPath { get; }

        public bool[]? SupportFaceColumns { get; }

        public bool[]? SupportChunkFallbackColumns { get; set; }

        public List<(int X, int Y, int Z)>? GravityPositions { get; set; }

        public MaterializationJob? SupportChunkJob { get; set; }

        public bool[]? SupportChunkTopOccupancy { get; set; }

        public Chunk Chunk => _chunk ??
            throw new InvalidOperationException(
                "Chunk materialization has not started.");

        public int OriginX { get; private set; }

        public int OriginY { get; private set; }

        public int OriginZ { get; private set; }

        public void InitializeChunk()
        {
            if (_chunk is not null)
                return;

            _chunk = new Chunk();
            (OriginX, OriginY, OriginZ) = VoxelCoordinates.ChunkOrigin(Coord);
            // Allocation and palette setup occur on the materialization worker.
            if (_owner._voidSpawnPlatform is null)
                InitialBlocks = _chunk.BeginInitialBlockWrites();
        }

        public SurfaceTerrainColumn? Column { get; set; }

        public TerrainDensityVolume? DensityVolume { get; set; }

        public int TopExclusive { get; set; }

        public int FirstTerrainWorldY { get; set; }

        public int LastTerrainWorldY { get; set; }

        public int NextTerrainLocalY { get; set; }

        public DirectionalFaceOccupancyMasks? DirectionalFaceOccupancyMasks { get; set; }

        public double DirectionalFacePalettePreparationMilliseconds { get; set; }

        public DirectionalFaceOccupancyPreparationProfile
            DirectionalFacePalettePreparationProfile { get; set; }

        public int DirectionalFacePaletteDensityProbes { get; set; }

        public int DirectionalFacePaletteBoundaryDensityProbes { get; set; }

        public ColumnMaterializationCache[]? ColumnMaterializationCaches { get; set; }

        public ushort[]? TerrainLayerCandidatesByY { get; set; }

        public ushort[]? TerrainLayerCandidateCountsByY { get; set; }

        public List<PendingGeneratedObject>? PendingObjects { get; set; }

        public Chunk.InitialBlockWriter? InitialBlocks { get; set; }

        public ChunkMaterializationStage Stage { get; internal set; }

        public bool IsComplete => Stage == ChunkMaterializationStage.Complete;

        public Chunk? Result { get; internal set; }

        private DirectionalFacePaletteMetrics? _directionalFacePaletteMetrics;

        public DirectionalFacePaletteMetrics? DirectionalFacePaletteMetrics
        {
            get => SupportChunkJob?.DirectionalFacePaletteMetrics ??
                _directionalFacePaletteMetrics;
            set => _directionalFacePaletteMetrics = value;
        }

        public void Advance() => _owner.Advance(this);

        public void Dispose()
        {
            SupportChunkJob?.Dispose();
            SupportChunkJob = null;
            SupportChunkTopOccupancy = null;
            InitialBlocks?.Dispose();
            InitialBlocks = null;
            DirectionalFaceOccupancyMasks = null;
            var columnCaches = ColumnMaterializationCaches;
            ColumnMaterializationCaches = null;
            if (columnCaches is not null)
            {
                Array.Clear(columnCaches, 0, Chunk.Area);
                ArrayPool<ColumnMaterializationCache>.Shared.Return(
                    columnCaches, clearArray: false);
            }
            var terrainCandidates = TerrainLayerCandidatesByY;
            TerrainLayerCandidatesByY = null;
            if (terrainCandidates is not null)
                ArrayPool<ushort>.Shared.Return(
                    terrainCandidates, clearArray: false);
            var terrainCandidateCounts = TerrainLayerCandidateCountsByY;
            TerrainLayerCandidateCountsByY = null;
            if (terrainCandidateCounts is not null)
            {
                Array.Clear(terrainCandidateCounts, 0, Chunk.Size);
                ArrayPool<ushort>.Shared.Return(
                    terrainCandidateCounts, clearArray: false);
            }
            _geologyNoiseCache?.Dispose();
            _geologyNoiseCache = null;
        }

        internal WorldGenerationEntropy.ValueNoise3DLatticeCache
            GetGeologyNoiseCache() =>
            _geologyNoiseCache ??= new WorldGenerationEntropy.ValueNoise3DLatticeCache();
    }

    /// <summary>
    /// Finalize authored Structure Objects on their actual supporting voxels.
    /// This pass observes final structures/caves/shell, and never replaces blocks
    /// or fluid occupying the adjacent Object cell.
    /// </summary>
    private void MaterializeStructureObjects(Chunk chunk, ChunkCoord coord)
    {
        if (!_structures.HasRules) return;
        var origin = VoxelCoordinates.ChunkOrigin(coord);
        foreach (var placement in _structures.PlacementsForChunk(coord.X, coord.Z))
        foreach (var obj in placement.Objects)
        {
            var support = obj.SupportPosition;
            var x = (long)support.X - origin.X;
            var y = (long)support.Y - origin.Y;
            var z = (long)support.Z - origin.Z;
            if (x < 0 || x >= Chunk.Size ||
                y < 0 || y >= Chunk.Size ||
                z < 0 || z >= Chunk.Size)
                continue;

            var sx = (int)x;
            var sy = (int)y;
            var sz = (int)z;
            var cell = chunk.GetCell(sx, sy, sz);
            if (cell.IsEmpty || !_blocks.GetDefinition(cell.Block).IsCollidable)
                continue;

            var targetX = (long)obj.X - origin.X;
            var targetY = (long)obj.Y - origin.Y;
            var targetZ = (long)obj.Z - origin.Z;
            if (targetX >= 0 && targetX < Chunk.Size &&
                targetY >= 0 && targetY < Chunk.Size &&
                targetZ >= 0 && targetZ < Chunk.Size &&
                !chunk.GetCell((int)targetX, (int)targetY, (int)targetZ).IsEmpty)
                continue;

            chunk.AddObject(sx, sy, sz, obj.Cell);
        }
    }

    private bool CanPlaceDecorationAt(long y, int originY, int topExclusive) =>
        y >= originY && y < topExclusive &&
        (_floorY is not { } floor || y > floor) &&
        (_roofY is not { } roof || y < roof);

    private bool HasNearbyFluid(
        FluidRuntimeId fluid, int radius, int worldX, int supportY, int worldZ)
    {
        var distanceSquared = radius * radius;
        for (var dz = -radius; dz <= radius; dz++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var square = dx * dx + dz * dz;
            if (square == 0 || square > distanceSquared)
                continue;
            var px = (long)worldX + dx;
            var pz = (long)worldZ + dz;
            if (px < int.MinValue || px > int.MaxValue ||
                pz < int.MinValue || pz > int.MaxValue)
                continue;
            var (coord, local) = VoxelCoordinates.FromWorld(
                (int)px, 0, (int)pz);
            var neighbor = _columns.Get(coord.X, coord.Z);
            var biome = neighbor.BiomeAt(local.X, local.Z);
            var baseY = neighbor.BaseHeightAt(local.X, local.Z);
            if (supportY <= baseY)
                continue;
            var candidate = _generatedFluids.FluidAtEmptyVoxel(
                biome, baseY,
                neighbor.SurfaceFluidCutDepthAt(local.X, local.Z),
                (int)px, supportY, (int)pz);
            if (candidate.Fluid == fluid &&
                _terrain.DensityAt(
                    biome,
                    baseY,
                    (int)px,
                    supportY,
                    (int)pz,
                    neighbor.VolumeBiomes[local.X, local.Z]) < 0d)
                return true;
        }
        return false;
    }

    private double DensityAtWorld(
        int worldX,
        int worldY,
        int worldZ)
    {
        var (coord, local) =
            VoxelCoordinates.FromWorld(worldX, 0, worldZ);
        var column = _columns.Get(coord.X, coord.Z);
        var biome = column.BiomeAt(local.X, local.Z);
        return _terrain.DensityAt(
            biome,
            column.BaseHeightAt(local.X, local.Z),
            worldX,
            worldY,
            worldZ,
            column.VolumeBiomes[local.X, local.Z]);
    }

    private uint AdditiveDepthAt(
        TerrainDensityVolume densityVolume,
        BiomeSample surfaceBiome,
        int baseY,
        int localX,
        int localY,
        int localZ,
        int worldX,
        int worldY,
        int worldZ,
        uint finiteDepth)
    {
        if (finiteDepth == 0)
            return 0;

        var depth = densityVolume.ContiguousSolidDepthAboveInVolume(
            localX, localY, localZ, finiteDepth);
        if (depth >= finiteDepth ||
            (long)localY + depth + 1L < densityVolume.Height)
        {
            return depth;
        }

        // The contiguous run reached the top of the chunk snapshot. Only
        // this short remainder needs world-space density queries.
        var volumeBiome = densityVolume.VolumePlacementAt(
            localX, localZ);
        while (depth < finiteDepth)
        {
            var above = (long)worldY + depth + 1L;
            if (above > int.MaxValue ||
                _terrain.DensityAt(
                    surfaceBiome,
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

    private void MaterializeStructures(
        Chunk chunk,
        ChunkCoord coord,
        int originY,
        int topExclusive)
    {
        if (!_structures.HasRules)
        {
            return;
        }

        var origin =
            VoxelCoordinates.ChunkOrigin(
                coord);
        var placements =
            _structures.PlacementsForChunk(
                coord.X,
                coord.Z);

        // Placement bounds include blocks, fluids and explicit clears.
        // Surface structures must not scan underground occupancy or
        // rasterize every payload in unrelated vertical chunk bands.
        bool IntersectsThisChunk(SurfaceStructurePlacement placement) =>
            placement.MinimumY < topExclusive &&
            placement.MaximumY >= originY;

        if (!placements.Any(IntersectsThisChunk))
        {
            return;
        }

        var needsBaseOccupancy =
            placements.Any(placement =>
                IntersectsThisChunk(placement) &&
                placement.Generation.ReplacePolicy ==
                StructureReplacePolicy.AirOnly);
        var baseOccupied =
            needsBaseOccupancy
                ? new HashSet<int>()
                : null;

        if (baseOccupied is not null)
        {
            chunk.VisitBlockCells(
                (x, y, z, _) =>
                    baseOccupied.Add(
                        StructureCellKey(
                            x,
                            y,
                            z)));
            chunk.VisitFluidCells(
                (x, y, z, _) =>
                    baseOccupied.Add(
                        StructureCellKey(
                            x,
                            y,
                            z)));
        }

        var claimed =
            new HashSet<int>();

        foreach (var placement in
                 placements)
        {
            if (!IntersectsThisChunk(placement))
            {
                continue;
            }

            foreach (var voxel in
                     placement.ClearVoxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    VoxelCell.Empty);
                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    FluidCell.Empty);
                claimed.Add(
                    key);
            }

            foreach (var voxel in
                     placement.Voxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    FluidCell.Empty);
                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    voxel.Cell);
                if (!voxel.Mask.IsEmpty)
                    chunk.SetMicroblockMask(localX, localY, localZ, voxel.Mask);
                if (!voxel.Surface.IsEmpty)
                    chunk.SetSurfaceState(localX, localY, localZ, voxel.Surface);
                claimed.Add(
                    key);
            }

            foreach (var voxel in
                     placement.FluidVoxels)
            {
                if (!TryStructureLocal(
                        voxel.X,
                        voxel.Y,
                        voxel.Z,
                        origin,
                        originY,
                        topExclusive,
                        out var localX,
                        out var localY,
                        out var localZ))
                {
                    continue;
                }

                var key =
                    StructureCellKey(
                        localX,
                        localY,
                        localZ);
                if (!CanReplaceStructureCell(
                        placement.Generation.ReplacePolicy,
                        key,
                        claimed,
                        baseOccupied))
                {
                    continue;
                }

                chunk.SetCell(
                    localX,
                    localY,
                    localZ,
                    VoxelCell.Empty);
                chunk.SetFluid(
                    localX,
                    localY,
                    localZ,
                    voxel.Fluid);
                claimed.Add(
                    key);
            }
        }
    }

    private bool TryStructureLocal(
        int worldX,
        int worldY,
        int worldZ,
        (int X, int Y, int Z) origin,
        int originY,
        int topExclusive,
        out int localX,
        out int localY,
        out int localZ)
    {
        localX =
            localY =
            localZ =
            0;

        if (worldY < originY ||
            worldY >= topExclusive ||
            worldX < origin.X ||
            worldX >=
                origin.X +
                Chunk.Size ||
            worldZ < origin.Z ||
            worldZ >=
                origin.Z +
                Chunk.Size ||
            (_floorY is { } floorY &&
             worldY == floorY) ||
            (_roofY is { } roofY &&
             worldY == roofY))
        {
            return false;
        }

        localX =
            worldX -
            origin.X;
        localY =
            worldY -
            originY;
        localZ =
            worldZ -
            origin.Z;
        return true;
    }

    private static bool CanReplaceStructureCell(
        StructureReplacePolicy policy,
        int key,
        IReadOnlySet<int> claimed,
        IReadOnlySet<int>? baseOccupied) =>
        policy switch
        {
            StructureReplacePolicy.Any =>
                true,
            StructureReplacePolicy.AirOnly =>
                !claimed.Contains(
                    key) &&
                !(baseOccupied?.Contains(
                      key) ??
                  false),
            StructureReplacePolicy.Terrain =>
                !claimed.Contains(
                    key),
            _ =>
                throw new InvalidOperationException(
                    $"Unknown structure replace policy {policy}."),
        };

    private static int StructureCellKey(
        int x,
        int y,
        int z) =>
        x |
        y << 8 |
        z << 16;

    // Keep each slab inside one vertical chunk band so occupancy validity bits
    // can be published and reused by the adjacent chunk as one compact snapshot.
    private (int OriginY, DensityOccupancySlab Volume)[] SampleVerticalDensitySlabs(
        SurfaceTerrainColumn column,
        int originX,
        int originZ,
        int startY,
        int endExclusive,
        out int densityProbes)
    {
        densityProbes = 0;
        if (startY >= endExclusive)
            return Array.Empty<(int OriginY, DensityOccupancySlab Volume)>();

        var slabs = new List<(int OriginY, DensityOccupancySlab Volume)>();
        var nextY = startY;
        while (nextY < endExclusive)
        {
            var bandOffset = nextY % Chunk.Size;
            var height = Math.Min(Chunk.Size - bandOffset, endExclusive - nextY);
            var volume = _columns.SampleDensitySlab(
                column, originX, nextY, originZ, height, out var probes);
            densityProbes = checked(densityProbes + probes);
            slabs.Add((nextY, volume));
            nextY = checked(nextY + height);
        }
        return slabs.ToArray();
    }

    private DirectionalFaceOccupancyMasks BuildDirectionalFaceOccupancyMasks(
        TerrainDensityVolume densityVolume,
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        out int densityProbes,
        out int boundaryDensityProbes,
        out DirectionalFaceOccupancyPreparationProfile preparationProfile) =>
        BuildDirectionalFaceOccupancyMasks(
            densityVolume.IsSolidAtUnchecked,
            column,
            originX,
            originY,
            originZ,
            out densityProbes,
            out boundaryDensityProbes,
            out preparationProfile);

    private DirectionalFaceOccupancyMasks BuildDirectionalFaceOccupancyMasks(
        Chunk chunk,
        ChunkCoord coord)
    {
        var origin = VoxelCoordinates.ChunkOrigin(coord);
        var column = _columns.Get(coord.X, coord.Z);
        return BuildDirectionalFaceOccupancyMasks(
            (x, y, z) => !chunk.GetCell(x, y, z).IsEmpty,
            column,
            origin.X,
            origin.Y,
            origin.Z,
            out _,
            out _,
            out _);
    }

    private BoundaryMaskReadMetrics ReadDirectionalFaceXBoundaryMasks(
        int startLocalX,
        int endLocalX,
        int maximumDepth,
        int originX,
        int originY,
        int originZ,
        ushort[] boundaryMasks,
        Span<int> columnIndices,
        Span<ushort> solidMasks)
    {
        var cursor = startLocalX;
        var densityProbes = 0;
        var batchCount = 0;
        var columnCount = 0;

        while (cursor < endLocalX)
        {
            var firstWorldX = checked(originX + cursor);
            var first = VoxelCoordinates.FromWorld(
                firstWorldX, originY, originZ);
            var sliceEnd = cursor + 1;
            while (sliceEnd < endLocalX &&
                   VoxelCoordinates.FromWorld(
                       checked(originX + sliceEnd), originY, originZ)
                       .Chunk.X == first.Chunk.X)
            {
                sliceEnd++;
            }

            var count = checked((sliceEnd - cursor) * Chunk.Size);
            if (count > columnIndices.Length || count > solidMasks.Length)
                throw new InvalidOperationException(
                    "A horizontal X boundary batch exceeded one chunk's column capacity.");

            var writeIndex = 0;
            for (var localX = cursor; localX < sliceEnd; localX++)
            {
                var local = VoxelCoordinates.FromWorld(
                    checked(originX + localX), originY, originZ).Local;
                for (var z = 0; z < Chunk.Size; z++)
                    columnIndices[writeIndex++] = z * Chunk.Size + local.X;
            }

            _columns.ReadDensityColumnMasks(
                first.Chunk.X,
                originY,
                first.Chunk.Z,
                columnIndices[..count],
                solidMasks[..count],
                out var probes);
            densityProbes = checked(densityProbes + probes);

            writeIndex = 0;
            for (var localX = cursor; localX < sliceEnd; localX++)
            for (var z = 0; z < Chunk.Size; z++)
            {
                var destinationIndex =
                    (localX + maximumDepth) * Chunk.Size + z;
                boundaryMasks[destinationIndex] = solidMasks[writeIndex++];
            }

            batchCount++;
            columnCount = checked(columnCount + count);
            cursor = sliceEnd;
        }

        return new BoundaryMaskReadMetrics(
            densityProbes, batchCount, columnCount);
    }

    private BoundaryMaskReadMetrics ReadDirectionalFaceZBoundaryMasks(
        int startLocalZ,
        int endLocalZ,
        int maximumDepth,
        int originX,
        int originY,
        int originZ,
        int regionSize,
        ushort[] boundaryMasks,
        Span<int> columnIndices,
        Span<ushort> solidMasks)
    {
        var cursor = startLocalZ;
        var densityProbes = 0;
        var batchCount = 0;
        var columnCount = 0;

        while (cursor < endLocalZ)
        {
            var firstWorldZ = checked(originZ + cursor);
            var first = VoxelCoordinates.FromWorld(
                originX, originY, firstWorldZ);
            var sliceEnd = cursor + 1;
            while (sliceEnd < endLocalZ &&
                   VoxelCoordinates.FromWorld(
                       originX, originY, checked(originZ + sliceEnd))
                       .Chunk.Z == first.Chunk.Z)
            {
                sliceEnd++;
            }

            var count = checked((sliceEnd - cursor) * Chunk.Size);
            if (count > columnIndices.Length || count > solidMasks.Length)
                throw new InvalidOperationException(
                    "A horizontal Z boundary batch exceeded one chunk's column capacity.");

            var writeIndex = 0;
            for (var localZ = cursor; localZ < sliceEnd; localZ++)
            {
                var local = VoxelCoordinates.FromWorld(
                    originX, originY, checked(originZ + localZ)).Local;
                for (var x = 0; x < Chunk.Size; x++)
                    columnIndices[writeIndex++] = local.Z * Chunk.Size + x;
            }

            _columns.ReadDensityColumnMasks(
                first.Chunk.X,
                originY,
                first.Chunk.Z,
                columnIndices[..count],
                solidMasks[..count],
                out var probes);
            densityProbes = checked(densityProbes + probes);

            writeIndex = 0;
            for (var localZ = cursor; localZ < sliceEnd; localZ++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var destinationIndex =
                    x * regionSize + localZ + maximumDepth;
                boundaryMasks[destinationIndex] = solidMasks[writeIndex++];
            }

            batchCount++;
            columnCount = checked(columnCount + count);
            cursor = sliceEnd;
        }

        return new BoundaryMaskReadMetrics(
            densityProbes, batchCount, columnCount);
    }

    private DirectionalFaceOccupancyMasks BuildDirectionalFaceOccupancyMasks(
        Func<int, int, int, bool> isSolid,
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        out int densityProbes,
        out int boundaryDensityProbes,
        out DirectionalFaceOccupancyPreparationProfile preparationProfile)
    {
        ArgumentNullException.ThrowIfNull(isSolid);
        ArgumentNullException.ThrowIfNull(column);
        const int size = Chunk.Size;
        var maximumDepth = checked((int)_materials.MaximumDirectionalPaintDepth);
        if (maximumDepth is < 0 or > 64)
            throw new InvalidOperationException(
                "Directional face palette depth must be between 0 and 64 voxels.");

        densityProbes = 0;
        boundaryDensityProbes = 0;
        preparationProfile = default;
        var densityProbeCount = 0;
        var boundaryDensityProbeCount = 0;
        if (maximumDepth == 0)
        {
            return new DirectionalFaceOccupancyMasks(
                0, 1,
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                new ushort[Chunk.Area]);
        }

        var regionSize = checked(size + maximumDepth * 2);
        var wordCount = (regionSize + 63) >> 6;
        var xRows = new ulong[Chunk.Area * wordCount];
        var zRows = new ulong[Chunk.Area * wordCount];
        var yRows = new ulong[Chunk.Area * wordCount];

#if DEBUG
        var slabReadStarted = Stopwatch.GetTimestamp();
#endif
        var bottomSlabs = SampleVerticalDensitySlabs(
            column, originX, originZ,
            Math.Max(0, originY - maximumDepth), originY,
            out var bottomDensityProbes);
        var topStartY = checked(originY + size);
        var topSlabs = SampleVerticalDensitySlabs(
            column, originX, originZ,
            topStartY, checked(topStartY + maximumDepth),
            out var topDensityProbes);
#if DEBUG
        var densitySlabReadMilliseconds =
            Stopwatch.GetElapsedTime(slabReadStarted).TotalMilliseconds;
#else
        const double densitySlabReadMilliseconds = 0d;
#endif
        densityProbeCount = checked(bottomDensityProbes + topDensityProbes);
        boundaryDensityProbeCount = densityProbeCount;

        bool VerticalHaloIsSolid(int localX, int localY, int localZ)
        {
            var worldY = checked(originY + localY);
            var slabs = localY < 0 ? bottomSlabs : topSlabs;
            foreach (var slab in slabs)
            {
                var slabLocalY = worldY - slab.OriginY;
                if ((uint)slabLocalY < (uint)slab.Volume.Height)
                    return slab.Volume.IsSolidAtUnchecked(localX, slabLocalY, localZ);
            }
            throw new InvalidOperationException(
                $"No sampled vertical halo slab covers world Y={worldY}.");
        }

        static void MarkSolid(ulong[] rows, int rowOffset, int bitIndex) =>
            rows[rowOffset + (bitIndex >> 6)] |= 1UL << (bitIndex & 63);

        static bool IsSolid(ulong[] rows, int rowOffset, int bitIndex) =>
            (rows[rowOffset + (bitIndex >> 6)] &
             (1UL << (bitIndex & 63))) != 0;

        var xBoundaryMasks = new ushort[regionSize * size];
        var zBoundaryMasks = new ushort[size * regionSize];
        Span<int> boundaryColumnIndices = stackalloc int[Chunk.Area];
        Span<ushort> boundarySolidMasks = stackalloc ushort[Chunk.Area];

#if DEBUG
        var horizontalBoundaryReadStarted = Stopwatch.GetTimestamp();
#endif
        var leftXBoundary = ReadDirectionalFaceXBoundaryMasks(
            -maximumDepth, 0, maximumDepth,
            originX, originY, originZ,
            xBoundaryMasks, boundaryColumnIndices, boundarySolidMasks);
        var rightXBoundary = ReadDirectionalFaceXBoundaryMasks(
            size, size + maximumDepth, maximumDepth,
            originX, originY, originZ,
            xBoundaryMasks, boundaryColumnIndices, boundarySolidMasks);
        var negativeZBoundary = ReadDirectionalFaceZBoundaryMasks(
            -maximumDepth, 0, maximumDepth,
            originX, originY, originZ, regionSize,
            zBoundaryMasks, boundaryColumnIndices, boundarySolidMasks);
        var positiveZBoundary = ReadDirectionalFaceZBoundaryMasks(
            size, size + maximumDepth, maximumDepth,
            originX, originY, originZ, regionSize,
            zBoundaryMasks, boundaryColumnIndices, boundarySolidMasks);
#if DEBUG
        var horizontalBoundaryReadMilliseconds =
            Stopwatch.GetElapsedTime(horizontalBoundaryReadStarted).TotalMilliseconds;
#else
        const double horizontalBoundaryReadMilliseconds = 0d;
#endif
        var horizontalBoundaryDensityProbes = checked(
            leftXBoundary.DensityProbes +
            rightXBoundary.DensityProbes +
            negativeZBoundary.DensityProbes +
            positiveZBoundary.DensityProbes);
        var horizontalBoundaryBatchCount = checked(
            leftXBoundary.BatchCount +
            rightXBoundary.BatchCount +
            negativeZBoundary.BatchCount +
            positiveZBoundary.BatchCount);
        var horizontalBoundaryColumnCount = checked(
            leftXBoundary.ColumnCount +
            rightXBoundary.ColumnCount +
            negativeZBoundary.ColumnCount +
            positiveZBoundary.ColumnCount);
        densityProbeCount = checked(
            densityProbeCount + horizontalBoundaryDensityProbes);
        boundaryDensityProbeCount = checked(
            boundaryDensityProbeCount + horizontalBoundaryDensityProbes);

#if DEBUG
        var occupancyRowAssemblyStarted = Stopwatch.GetTimestamp();
#endif
        for (var z = 0; z < size; z++)
        for (var x = 0; x < size; x++)
        {
            var rowOffset = (z * size + x) * wordCount;
            for (var regionY = 0; regionY < regionSize; regionY++)
            {
                var localY = regionY - maximumDepth;
                bool solid;
                if ((uint)localY < size)
                    solid = isSolid(x, localY, z);
                else
                {
                    var worldY = checked(originY + localY);
                    solid = worldY < 0 || VerticalHaloIsSolid(x, localY, z);
                }
                if (solid)
                    MarkSolid(yRows, rowOffset, regionY);
            }
        }

        for (var y = 0; y < size; y++)
        for (var z = 0; z < size; z++)
        {
            var rowOffset = (y * size + z) * wordCount;
            for (var regionX = 0; regionX < regionSize; regionX++)
            {
                var localX = regionX - maximumDepth;
                bool solid;
                if ((uint)localX < size)
                {
                    var yRowOffset = (z * size + localX) * wordCount;
                    solid = IsSolid(yRows, yRowOffset, maximumDepth + y);
                }
                else
                {
                    var boundaryIndex =
                        (localX + maximumDepth) * size + z;
                    solid = (xBoundaryMasks[boundaryIndex] & (1u << y)) != 0;
                }
                if (solid)
                    MarkSolid(xRows, rowOffset, regionX);
            }
        }

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var rowOffset = (y * size + x) * wordCount;
            for (var regionZ = 0; regionZ < regionSize; regionZ++)
            {
                var localZ = regionZ - maximumDepth;
                bool solid;
                if ((uint)localZ < size)
                {
                    var yRowOffset = (localZ * size + x) * wordCount;
                    solid = IsSolid(yRows, yRowOffset, maximumDepth + y);
                }
                else
                {
                    var boundaryIndex =
                        x * regionSize + localZ + maximumDepth;
                    solid = (zBoundaryMasks[boundaryIndex] & (1u << y)) != 0;
                }
                if (solid)
                    MarkSolid(zRows, rowOffset, regionZ);
            }
        }
#if DEBUG
        var occupancyRowAssemblyMilliseconds =
            Stopwatch.GetElapsedTime(occupancyRowAssemblyStarted).TotalMilliseconds;
#else
        const double occupancyRowAssemblyMilliseconds = 0d;
#endif

#if DEBUG
        var candidateMaskBuildStarted = Stopwatch.GetTimestamp();
#endif
        var candidateColumns = DirectionalFaceCandidateMask.Build(
            regionSize, wordCount, maximumDepth,
            xRows, zRows, yRows);
#if DEBUG
        var candidateMaskBuildMilliseconds =
            Stopwatch.GetElapsedTime(candidateMaskBuildStarted).TotalMilliseconds;
#else
        const double candidateMaskBuildMilliseconds = 0d;
#endif
        densityProbes = densityProbeCount;
        boundaryDensityProbes = boundaryDensityProbeCount;
        preparationProfile = new DirectionalFaceOccupancyPreparationProfile(
            densitySlabReadMilliseconds,
            horizontalBoundaryReadMilliseconds,
            occupancyRowAssemblyMilliseconds,
            candidateMaskBuildMilliseconds,
            horizontalBoundaryBatchCount,
            horizontalBoundaryColumnCount);
        return new DirectionalFaceOccupancyMasks(
            maximumDepth, wordCount, xRows, zRows, yRows,
            candidateColumns);
    }

    private DirectionalPaletteColumnData SampleDirectionalPaletteColumnData(int worldX, int worldZ)
    {
        var (coord, local) = VoxelCoordinates.FromWorld(worldX, 0, worldZ);
        var column = _columns.Get(coord.X, coord.Z);
        var surfaceBiome = column.BiomeAt(local.X, local.Z);
        var emptyBiome = column.CaveBiomeAt(local.X, local.Z);
        var emptyPalette = emptyBiome is null
            ? default
            : _materials.DirectionalPaletteFor(
                new BiomeRegionOwner(emptyBiome, BiomeRegionOwnerKind.Volume));
        return new DirectionalPaletteColumnData(
            surfaceBiome, emptyBiome, column.VolumeBiomes[local.X, local.Z],
            column.BaseHeightAt(local.X, local.Z),
            column.SurfaceFluidCutDepthAt(local.X, local.Z), emptyPalette);
    }

    private bool ReapplyDirectionalFacePaletteAt(
        Chunk chunk,
        TerrainDensityVolume densityVolume,
        int index,
        int worldX,
        int worldY,
        int worldZ,
        Func<int, int, DirectionalPaletteColumnData> columnAt)
    {
        chunk.RestoreDirectionalFaceBaseBlockAtLinearIndex(index);
        var data = chunk.DirectionalFaceDataAtLinearIndex(index);
        if (!data.IsTerrainVoxel ||
            worldY <= 0 ||
            (_floorY is { } floorY && worldY <= floorY) ||
            (_roofY is { } roofY && worldY >= roofY))
            return false;
        if (!data.HasCandidates)
        {
            chunk.SetDirectionalFaceDataAtLinearIndex(index, default);
            return false;
        }

        var sourceColumn = columnAt(worldX, worldZ);
        var (sourceCoord, sourceLocal) = VoxelCoordinates.FromWorld(worldX, worldY, worldZ);
        var surfaceOwner = new BiomeRegionOwner(sourceColumn.SurfaceBiome, BiomeRegionOwnerKind.Surface);
        // The chunk snapshot already owns the immutable volume-placement
        // grid and its normalized per-column owners. Reuse it instead of
        // resampling the global placement field for every solid candidate.
        var volumeBiome = worldY > sourceColumn.BaseY
            ? densityVolume.VolumeBiomeAt(
                sourceLocal.X, worldY, sourceLocal.Z)
            : null;
        var solidOwner = volumeBiome is null
            ? surfaceOwner
            : new BiomeRegionOwner(volumeBiome, BiomeRegionOwnerKind.Volume);
        var solidPalette = _materials.DirectionalPaletteFor(solidOwner);

        var maximumDepth = checked((int)_materials.MaximumDirectionalPaintDepth);
        if (maximumDepth == 0)
            return false;

        var up = data.Up;
        var negativeX = data.NegativeX;
        var positiveX = data.PositiveX;
        var negativeZ = data.NegativeZ;
        var positiveZ = data.PositiveZ;
        var down = data.Down;
        var depthMask = maximumDepth == 64
            ? ulong.MaxValue
            : (1UL << maximumDepth) - 1UL;

        static int FirstCandidateDistance(ulong candidates, ulong validDepthMask)
        {
            candidates &= validDepthMask;
            return candidates == 0
                ? int.MaxValue
                : System.Numerics.BitOperations.TrailingZeroCount(candidates) + 1;
        }

        while (true)
        {
            var nearestDistance = int.MaxValue;
            var direction = -1;

            // Strict comparison intentionally preserves the legacy tie order:
            // Up, -X, +X, -Z, +Z, Down.
            var candidateDistance = FirstCandidateDistance(up, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 0;
            }
            candidateDistance = FirstCandidateDistance(negativeX, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 1;
            }
            candidateDistance = FirstCandidateDistance(positiveX, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 2;
            }
            candidateDistance = FirstCandidateDistance(negativeZ, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 3;
            }
            candidateDistance = FirstCandidateDistance(positiveZ, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 4;
            }
            candidateDistance = FirstCandidateDistance(down, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 5;
            }

            if (direction < 0)
                return false;

            var distance = nearestDistance;
            var candidateBit = 1UL << (distance - 1);
            switch (direction)
            {
                case 0: up &= ~candidateBit; break;
                case 1: negativeX &= ~candidateBit; break;
                case 2: positiveX &= ~candidateBit; break;
                case 3: negativeZ &= ~candidateBit; break;
                case 4: positiveZ &= ~candidateBit; break;
                case 5: down &= ~candidateBit; break;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }

            var dx = direction switch
            {
                1 => -distance,
                2 => distance,
                _ => 0,
            };
            var dy = direction switch
            {
                0 => distance,
                5 => -distance,
                _ => 0,
            };
            var dz = direction switch
            {
                3 => -distance,
                4 => distance,
                _ => 0,
            };
            var neighborY = worldY + dy;
            if (neighborY < 0)
                continue;

            var face = direction switch
            {
                0 => BiomePaletteFace.Floor,
                1 or 2 or 3 or 4 => BiomePaletteFace.Walls,
                5 => BiomePaletteFace.Ceiling,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };
            var targetColumn = columnAt(worldX + dx, worldZ + dz);
            var emptyPalette = targetColumn.EmptyRegionPalette;
            var shellBoundary = false;
            var emptyCanOwn =
                emptyPalette.Has(face) &&
                targetColumn.EmptyRegionBiome is not null &&
                neighborY > 0 &&
                (long)neighborY <= (long)targetColumn.BaseY - targetColumn.SurfaceFluidCutDepth;
            if (emptyCanOwn)
            {
                shellBoundary = IsSphereShellBoundaryOrLining(neighborY);
                emptyCanOwn = !shellBoundary;
            }
            if (!solidPalette.Has(face) && !emptyCanOwn)
                continue;

            var owner = solidOwner;
            var palette = solidPalette;
            if (emptyCanOwn)
            {
                var resolved = SurfaceTerrainField.ResolveRegionOwner(
                    targetColumn.SurfaceBiome,
                    additiveVolumeBiome: null,
                    emptyRegionVolumeBiome: targetColumn.EmptyRegionBiome,
                    baseY: targetColumn.BaseY,
                    surfaceFluidCutDepth: targetColumn.SurfaceFluidCutDepth,
                    worldY: neighborY,
                    isEmpty: true,
                    isSphereShellBoundaryOrLining: shellBoundary);
                if (resolved.Kind == BiomeRegionOwnerKind.Volume)
                {
                    owner = resolved;
                    palette = emptyPalette;
                }
            }

            var depth = checked((uint)distance - 1u);
            if (!palette.Has(face) || depth >= palette.MaxDepth)
                continue;

            var replacement = _materials.VolumeBlockAt(
                owner.Biome, worldX, worldY, worldZ, face, depth);
            return chunk.SetDirectionalFacePaletteBlockAtLinearIndex(index, replacement);
        }
        return false;
    }

    private void MaterializeDirectionalFacePalettes(MaterializationJob job)
    {
#if DEBUG
        var applyStarted = Stopwatch.GetTimestamp();
#endif
        var chunk = job.Chunk;
        var origin = VoxelCoordinates.ChunkOrigin(job.Coord);
        var column = RequiredColumn(job);
        var densityVolume = RequiredDensity(job);
        var columns = new DirectionalPaletteColumnData[Chunk.Area];
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var biome = column.BiomeAt(x, z);
            var emptyBiome = column.CaveBiomeAt(x, z);
            var emptyPalette = emptyBiome is null
                ? default
                : _materials.DirectionalPaletteFor(
                    new BiomeRegionOwner(emptyBiome, BiomeRegionOwnerKind.Volume));
            columns[z * Chunk.Size + x] = new DirectionalPaletteColumnData(
                biome, emptyBiome, column.VolumeBiomes[x, z],
                column.BaseHeightAt(x, z), column.SurfaceFluidCutDepthAt(x, z),
                emptyPalette);
        }
        var externalColumns =
            new Dictionary<(int X, int Z), DirectionalPaletteColumnData>();
        var neighborColumnLookups = 0;
        DirectionalPaletteColumnData ColumnAt(int x, int z)
        {
            var localX = x - origin.X;
            var localZ = z - origin.Z;
            if ((uint)localX < Chunk.Size && (uint)localZ < Chunk.Size)
                return columns[localZ * Chunk.Size + localX];

            var key = (x, z);
            if (!externalColumns.TryGetValue(key, out var data))
            {
                data = SampleDirectionalPaletteColumnData(x, z);
                externalColumns.Add(key, data);
                neighborColumnLookups++;
            }
            return data;
        }

        var faceProbes = 0;
        var solidVoxels = 0;
        var writes = 0;
        chunk.VisitDirectionalFaceVoxels((index, data) =>
        {
            if (!data.HasCandidates ||
                chunk.GetCellAtLinearIndexUnchecked(index).IsEmpty)
                return;

            solidVoxels++;
            faceProbes += System.Numerics.BitOperations.PopCount(data.Up);
            faceProbes += System.Numerics.BitOperations.PopCount(data.NegativeX);
            faceProbes += System.Numerics.BitOperations.PopCount(data.PositiveX);
            faceProbes += System.Numerics.BitOperations.PopCount(data.NegativeZ);
            faceProbes += System.Numerics.BitOperations.PopCount(data.PositiveZ);
            faceProbes += System.Numerics.BitOperations.PopCount(data.Down);

            var y = index / Chunk.Area;
            var withinLayer = index % Chunk.Area;
            var z = withinLayer / Chunk.Size;
            var x = withinLayer % Chunk.Size;
            if (ReapplyDirectionalFacePaletteAt(
                    chunk, densityVolume, index,
                    origin.X + x, origin.Y + y, origin.Z + z, ColumnAt))
                writes++;
        });
#if DEBUG
        var applyMilliseconds = Stopwatch.GetElapsedTime(applyStarted).TotalMilliseconds;
        var preparationMilliseconds = job.DirectionalFacePalettePreparationMilliseconds;
        var totalMilliseconds = preparationMilliseconds + applyMilliseconds;
#else
        const double applyMilliseconds = 0;
        const double preparationMilliseconds = 0;
        const double totalMilliseconds = 0;
#endif
        job.DirectionalFacePaletteMetrics = new DirectionalFacePaletteMetrics(
#if DEBUG
            HasTimingBreakdown: true,
#else
            HasTimingBreakdown: false,
#endif
            TimingSampleInterval: 1,
            SurfaceColumns: Chunk.Area,
            SolidVoxels: solidVoxels,
            FaceProbes: faceProbes,
            NeighborColumnLookups: neighborColumnLookups,
            SolidPaletteLookups: 0,
            EmptyRegionPaletteLookups: 0,
            DensityProbes: job.DirectionalFacePaletteDensityProbes,
            BoundaryDensityProbes: job.DirectionalFacePaletteBoundaryDensityProbes,
            EmptyRegionOwnerResolutions: 0,
            MaskRejectedProbes: 0,
            EmptyRegionEligibilityRejectedProbes: 0,
            PaletteRejectedProbes: 0,
            DepthRejectedProbes: 0,
            PaletteWrites: writes,
            TotalMilliseconds: totalMilliseconds,
            SurfaceColumnPreparationEstimatedMilliseconds: preparationMilliseconds,
            SolidOwnerResolutionEstimatedMilliseconds: 0,
            NeighborOwnerLookupEstimatedMilliseconds: 0,
            DensityProbeEstimatedMilliseconds: 0,
            EmptyRegionOwnerResolutionEstimatedMilliseconds: 0,
            PaletteWriteEstimatedMilliseconds: applyMilliseconds,
            OtherEstimatedMilliseconds: 0,
            DensitySlabReadMilliseconds:
                job.DirectionalFacePalettePreparationProfile.DensitySlabReadMilliseconds,
            HorizontalBoundaryReadMilliseconds:
                job.DirectionalFacePalettePreparationProfile.HorizontalBoundaryReadMilliseconds,
            OccupancyRowAssemblyMilliseconds:
                job.DirectionalFacePalettePreparationProfile.OccupancyRowAssemblyMilliseconds,
            CandidateMaskBuildMilliseconds:
                job.DirectionalFacePalettePreparationProfile.CandidateMaskBuildMilliseconds,
            HorizontalBoundaryBatchCount:
                job.DirectionalFacePalettePreparationProfile.HorizontalBoundaryBatchCount,
            HorizontalBoundaryColumnCount:
                job.DirectionalFacePalettePreparationProfile.HorizontalBoundaryColumnCount);
    }

    internal IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrival(
        VoxelWorld world, ChunkCoord arriving)
    {
        ArgumentNullException.ThrowIfNull(world);
        var maximumDepth = checked((int)_materials.MaximumDirectionalPaintDepth);
        if (maximumDepth <= 0)
            return Array.Empty<WorldVoxelCoord>();

        var changedSources = new HashSet<(ChunkCoord Coord, int Index)>();
        var dirtyChunks = new HashSet<ChunkCoord>();
        var ensured = new HashSet<ChunkCoord>();
        var maxChunkOffset = (maximumDepth + Chunk.Size - 1) / Chunk.Size;

        for (var axis = 0; axis < 3; axis++)
        for (var sign = -1; sign <= 1; sign += 2)
        for (var offset = 0; offset <= maxChunkOffset; offset++)
        {
            var sourceCoord = OffsetChunk(arriving, axis, -sign * offset);
            ReconcileChunkOutward(
                world, sourceCoord, axis, sign, maximumDepth,
                changedSources, dirtyChunks, ensured);
        }
        return ApplyDirectionalFaceChanges(world, changedSources, dirtyChunks);
    }

    private void ReconcileChunkOutward(
        VoxelWorld world,
        ChunkCoord sourceCoord,
        int axis,
        int sign,
        int maximumDepth,
        HashSet<(ChunkCoord Coord, int Index)> changedSources,
        HashSet<ChunkCoord> dirtyChunks,
        HashSet<ChunkCoord> ensured)
    {
        if (!world.TryGetSavedChunk(sourceCoord, out var sourceChunk))
            return;
        EnsureDirectionalFaceData(
            sourceCoord, world, changedSources, dirtyChunks, ensured);

        var origin = VoxelCoordinates.ChunkOrigin(sourceCoord);
        var direction = DirectionForAxis(axis, sign);
        var (dx, dy, dz) = DirectionOffset(direction);

        for (var tangentA = 0; tangentA < Chunk.Size; tangentA++)
        for (var tangentB = 0; tangentB < Chunk.Size; tangentB++)
        for (var sourceDepth = 0; sourceDepth < Chunk.Size; sourceDepth++)
        {
            var sourceLocal = BoundaryLocal(
                axis, sign, sourceDepth, tangentA, tangentB);
            var sourceCell = sourceChunk.GetCell(
                sourceLocal.X, sourceLocal.Y, sourceLocal.Z);
            if (sourceCell.IsEmpty || sourceCell.IsPlayerPlaced)
                continue;

            var sourcePosition = new WorldVoxelCoord(
                origin.X + sourceLocal.X,
                origin.Y + sourceLocal.Y,
                origin.Z + sourceLocal.Z);
            var sourceIndex = sourceLocal.X +
                Chunk.Size * (sourceLocal.Z + Chunk.Size * sourceLocal.Y);

            // Reconcile all offsets independently. The original directional
            // scan skipped solid offsets and continued to later empty cells.
            for (var distance = sourceDepth + 1; distance <= maximumDepth; distance++)
            {
                var targetPosition = new WorldVoxelCoord(
                    checked(sourcePosition.X + dx * distance),
                    checked(sourcePosition.Y + dy * distance),
                    checked(sourcePosition.Z + dz * distance));
                if (!world.TryGetSavedCell(targetPosition, out var targetCell))
                    continue;

                if (sourceChunk.UpdateDirectionalFaceCandidateAtLinearIndex(
                        sourceIndex, direction, distance,
                        targetCell.IsEmpty, updateRevision: false))
                {
                    changedSources.Add((sourceCoord, sourceIndex));
                    dirtyChunks.Add(sourceCoord);
                }
            }
        }
    }

    private void EnsureDirectionalFaceData(
        ChunkCoord coord,
        VoxelWorld world,
        HashSet<(ChunkCoord Coord, int Index)> changedSources,
        HashSet<ChunkCoord> dirtyChunks,
        HashSet<ChunkCoord> ensured)
    {
        if (!ensured.Add(coord) ||
            !world.TryGetSavedChunk(coord, out var chunk) ||
            chunk.DirectionalFaceDataInitialized)
            return;

        var masks = BuildDirectionalFaceOccupancyMasks(chunk, coord);
        var candidateColumns = masks.CandidateColumns;
        for (var index = 0; index < Chunk.Volume; index++)
        {
            var cell = chunk.GetCellAtLinearIndexUnchecked(index);
            if (cell.IsEmpty || cell.IsPlayerPlaced)
                continue;
            var y = index / Chunk.Area;
            var layerIndex = index % Chunk.Area;
            var z = layerIndex / Chunk.Size;
            var x = layerIndex % Chunk.Size;
            var columnIndex = z * Chunk.Size + x;
            if ((candidateColumns[columnIndex] & (1u << y)) == 0)
                continue;
            var data = masks.AtCandidate(x, y, z);
            if (!data.HasCandidates)
                continue;
            chunk.SetDirectionalFaceDataAtLinearIndex(
                index, data, updateRevision: false);
            changedSources.Add((coord, index));
        }
        chunk.MarkDirectionalFaceDataInitialized(updateRevision: false);
        dirtyChunks.Add(coord);
    }

    private IReadOnlyList<WorldVoxelCoord> ApplyDirectionalFaceChanges(
        VoxelWorld world,
        HashSet<(ChunkCoord Coord, int Index)> changedSources,
        HashSet<ChunkCoord> dirtyChunks)
    {
        var visualChanges = new HashSet<WorldVoxelCoord>();
        foreach (var group in changedSources.GroupBy(entry => entry.Coord))
        {
            if (!world.TryGetSavedChunk(group.Key, out var chunk))
                continue;
            var origin = VoxelCoordinates.ChunkOrigin(group.Key);
            var column = _columns.Get(group.Key.X, group.Key.Z);
            var densityVolume = _columns.SampleDensityVolume(
                column, origin.X, origin.Y, origin.Z);
            var columns = new DirectionalPaletteColumnData[Chunk.Area];
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var surface = column.BiomeAt(x, z);
                var empty = column.CaveBiomeAt(x, z);
                var emptyPalette = empty is null
                    ? default
                    : _materials.DirectionalPaletteFor(
                        new BiomeRegionOwner(empty, BiomeRegionOwnerKind.Volume));
                columns[z * Chunk.Size + x] = new DirectionalPaletteColumnData(
                    surface, empty, column.VolumeBiomes[x, z],
                    column.BaseHeightAt(x, z), column.SurfaceFluidCutDepthAt(x, z),
                    emptyPalette);
            }
            var externalColumns =
                new Dictionary<(int X, int Z), DirectionalPaletteColumnData>();
            DirectionalPaletteColumnData ColumnAt(int x, int z)
            {
                var localX = x - origin.X;
                var localZ = z - origin.Z;
                if ((uint)localX < Chunk.Size && (uint)localZ < Chunk.Size)
                    return columns[localZ * Chunk.Size + localX];

                var key = (x, z);
                if (!externalColumns.TryGetValue(key, out var data))
                {
                    data = SampleDirectionalPaletteColumnData(x, z);
                    externalColumns.Add(key, data);
                }
                return data;
            }
            foreach (var (_, index) in group)
            {
                var y = index / Chunk.Area;
                var layer = index % Chunk.Area;
                var z = layer / Chunk.Size;
                var x = layer % Chunk.Size;
                var before = chunk.GetCellAtLinearIndexUnchecked(index);
                ReapplyDirectionalFacePaletteAt(
                    chunk, densityVolume, index,
                    origin.X + x, origin.Y + y, origin.Z + z, ColumnAt);
                var after = chunk.GetCellAtLinearIndexUnchecked(index);
                if (before.Block != after.Block)
                {
                    visualChanges.Add(new WorldVoxelCoord(
                        origin.X + x, origin.Y + y, origin.Z + z));
                    dirtyChunks.Add(group.Key);
                }
            }
        }
        foreach (var coord in dirtyChunks)
            world.MarkDerivedChunkMutation(coord);
        return visualChanges
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private static ChunkCoord OffsetChunk(ChunkCoord coord, int axis, int delta) => axis switch
    {
        0 => coord with { X = checked(coord.X + delta) },
        1 => coord with { Y = checked(coord.Y + delta) },
        2 => coord with { Z = checked(coord.Z + delta) },
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    private static DirectionalFaceDirection DirectionForAxis(int axis, int sign) =>
        (axis, sign) switch
        {
            (0, > 0) => DirectionalFaceDirection.PositiveX,
            (0, < 0) => DirectionalFaceDirection.NegativeX,
            (1, > 0) => DirectionalFaceDirection.Up,
            (1, < 0) => DirectionalFaceDirection.Down,
            (2, > 0) => DirectionalFaceDirection.PositiveZ,
            (2, < 0) => DirectionalFaceDirection.NegativeZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

    private static (int X, int Y, int Z) DirectionOffset(
        DirectionalFaceDirection direction) => direction switch
    {
        DirectionalFaceDirection.Up => (0, 1, 0),
        DirectionalFaceDirection.NegativeX => (-1, 0, 0),
        DirectionalFaceDirection.PositiveX => (1, 0, 0),
        DirectionalFaceDirection.NegativeZ => (0, 0, -1),
        DirectionalFaceDirection.PositiveZ => (0, 0, 1),
        DirectionalFaceDirection.Down => (0, -1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static LocalVoxelCoord BoundaryLocal(
        int axis, int sign, int depth, int tangentA, int tangentB) =>
        axis switch
        {
            0 => new LocalVoxelCoord(sign > 0 ? Chunk.Size - 1 - depth : depth, tangentA, tangentB),
            1 => new LocalVoxelCoord(tangentA, sign > 0 ? Chunk.Size - 1 - depth : depth, tangentB),
            2 => new LocalVoxelCoord(tangentA, tangentB, sign > 0 ? Chunk.Size - 1 - depth : depth),
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

    private void MaterializeVolumeStructures(Chunk chunk, ChunkCoord coord)
    {
        if (_volumeStructures is not { HasRules: true } formations)
            return;

        var origin = VoxelCoordinates.ChunkOrigin(coord);
        foreach (var voxel in formations.VoxelsForChunk(coord))
        {
            var x = voxel.X - origin.X;
            var y = voxel.Y - origin.Y;
            var z = voxel.Z - origin.Z;
            if ((uint)x >= Chunk.Size ||
                (uint)y >= Chunk.Size ||
                (uint)z >= Chunk.Size ||
                !chunk.GetCell(x, y, z).IsEmpty ||
                !chunk.GetFluid(x, y, z).IsEmpty)
                continue;

            chunk.SetBlock(x, y, z, voxel.Block);
        }
    }

    private void MaterializeVerticalDecorations(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ,
        ref List<PendingGeneratedObject>? pendingObjects,
        bool includeObjects)
    {
        if (!_decorations.HasVerticalDecorations)
            return;

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var baseY = column.BaseHeightAt(localX, localZ);
            var surfaceBiome = column.BiomeAt(localX, localZ);
            var worldX = originX + localX;
            var worldZ = originZ + localZ;
            var underground = column.CaveBiomeAt(localX, localZ);
            var volumePlacement = densityVolume.VolumePlacementAt(localX, localZ);
            var hasCaveDecorations = underground is not null &&
                _decorations.HasVerticalDecorationsFor(underground.Primary);
            var hasVolumeDecorations = volumePlacement is not null &&
                _decorations.HasVerticalDecorationsFor(volumePlacement.Primary);
            // A column with neither a decorated cave owner nor a decorated
            // additive owner cannot receive vertical decorations at any Y.
            // Skip its density and support scans before entering the Y loop.
            if (!hasCaveDecorations && !hasVolumeDecorations)
                continue;

            BiomeSurfaceMaterialColumn? volumeMaterials = null;

            // Include adjacent vertical seam targets: an Object is stored on
            // its host chunk, even when the visible sprite crosses a seam.
            for (var localY = -1; localY <= Chunk.Size; localY++)
            {
                var worldY = originY + localY;
                if (worldY <= 0 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY))
                    continue;

                var targetInChunk = (uint)localY < Chunk.Size;
                var targetSolid = targetInChunk
                    ? densityVolume.IsSolidAtUnchecked(localX, localY, localZ)
                    : _terrain.DensityAt(surfaceBiome, baseY,
                        worldX, worldY, worldZ, volumePlacement) >= 0d;
                if (targetSolid ||
                    (targetInChunk &&
                     !chunk.GetCell(localX, localY, localZ).IsEmpty))
                    continue;
                var belowSolid = localY > 0 && localY <= Chunk.Size
                    ? densityVolume.IsSolidAtUnchecked(localX, localY - 1, localZ)
                    : _terrain.DensityAt(
                        surfaceBiome, baseY,
                        worldX, worldY - 1, worldZ, volumePlacement) >= 0d;
                var isFloor = belowSolid;
                // Additive islands may expose their undersides above the
                // base surface. Resolve the author at the solid voxel above.
                // Do not sample unsupported sky or other volume ownership.
                BiomeSample? additiveCeilingBiome = null;
                if (_decorations.HasCeilingDecorations &&
                    worldY > baseY && volumePlacement is not null)
                {
                    additiveCeilingBiome = densityVolume.VolumeBiomeAt(
                        localX, worldY + 1, localZ);
                }

                var isCeiling = false;
                if (_decorations.HasCeilingDecorations &&
                    (worldY < baseY ||
                     (additiveCeilingBiome is not null &&
                      _decorations.HasCeilingDecorationsFor(
                          additiveCeilingBiome.Primary))))
                {
                    var aboveSolid = localY + 1 >= 0 && localY + 1 < Chunk.Size
                        ? densityVolume.IsSolidAtUnchecked(localX, localY + 1, localZ)
                        : _terrain.DensityAt(
                            surfaceBiome, baseY,
                            worldX, worldY + 1, worldZ, volumePlacement) >= 0d;
                    isCeiling = aboveSolid;
                }
                if (!isFloor && !isCeiling)
                    continue;

                BiomeSample? decoratorBiome;
                if (worldY > baseY)
                {
                    // The supporting additive voxel, not the empty sky,
                    // owns both top-facing and hanging decorators.
                    if (isFloor && worldY - 1 > baseY)
                    {
                        decoratorBiome = densityVolume.VolumeBiomeAt(
                            localX, worldY - 1, localZ);
                    }
                    else if (isCeiling)
                    {
                        decoratorBiome = additiveCeilingBiome;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    // A solid face outside a carved void does not authorize
                    // a volume decorator on the ordinary terrain surface.
                    if (_volumeBiomes is not { HasBiomes: true } ||
                        !_terrain.IsCaveVoidAt(
                            baseY, new WorldVoxelCoord(worldX, worldY, worldZ)))
                        continue;
                    decoratorBiome = underground;
                }

                if (decoratorBiome is null ||
                    !_decorations.HasVerticalDecorationsFor(
                        decoratorBiome.Primary))
                    continue;

                var submerged = !_generatedFluids.FluidAt(
                    surfaceBiome,
                    baseY,
                    column.SurfaceFluidCutDepthAt(localX, localZ),
                    worldX, worldY, worldZ,
                    targetSolid).IsEmpty;
                var placement = new SurfacePlacementContext(worldY, 0);

                if (isFloor)
                {
                    var support = localY > 0 && localY <= Chunk.Size
                        ? chunk.GetBlock(localX, localY - 1, localZ)
                        : worldY > baseY
                            ? (volumeMaterials ??= _materials.SampleColumn(
                                decoratorBiome, worldX, worldZ)).BlockAt(0)
                            : _materials.VolumeBlockAt(
                                decoratorBiome, worldX, worldY - 1, worldZ);
                    if (!support.IsAir)
                    {
                        if (targetInChunk)
                        {
                            var decoration = _decorations.BlockAt(
                                decoratorBiome, support, worldX, worldZ,
                                placement, verticalY: worldY,
                                submerged: submerged);
                            if (!decoration.IsAir)
                            {
                                chunk.SetBlock(localX, localY, localZ, decoration);
                                continue;
                            }
                        }
                        if (includeObjects &&
                            (uint)(localY - 1) < Chunk.Size)
                        {
                            var objectId = _decorations.ObjectAt(
                                decoratorBiome, support, worldX, worldZ,
                                placement, verticalY: worldY,
                                submerged: submerged, supportY: worldY - 1);
                            if (objectId is not null)
                            {
                                pendingObjects ??= [];
                                pendingObjects.Add(new PendingGeneratedObject(
                                    localX, localY - 1, localZ,
                                    objectId, BlockFace.Top, submerged));
                            }
                        }
                    }
                }

                if (!isCeiling ||
                    !_decorations.HasCeilingDecorationsFor(
                        decoratorBiome.Primary))
                    continue;

                BlockRuntimeId ceilingSupport;
                if (localY + 1 >= 0 && localY + 1 < Chunk.Size)
                {
                    ceilingSupport =
                        chunk.GetBlock(localX, localY + 1, localZ);
                }
                else if (worldY > baseY)
                {
                    // Resolve the *same* additive palette/depth used by the
                    // solid-material pass, even across a vertical chunk seam.
                    volumeMaterials ??= _materials.SampleColumn(
                        decoratorBiome, worldX, worldZ);
                    var depth = AdditiveDepthAt(
                        densityVolume, surfaceBiome, baseY,
                        localX, localY + 1, localZ,
                        worldX, worldY + 1, worldZ,
                        volumeMaterials.FiniteDepth);
                    ceilingSupport = volumeMaterials.BlockAt(depth);
                }
                else
                {
                    ceilingSupport = _materials.VolumeBlockAt(
                        decoratorBiome, worldX, worldY + 1, worldZ,
                        BiomePaletteFace.Ceiling);
                }
                if (ceilingSupport.IsAir)
                    continue;

                if (targetInChunk)
                {
                    var hanger = _decorations.BlockAt(
                        decoratorBiome, ceilingSupport, worldX, worldZ,
                        placement, verticalY: worldY, submerged: submerged,
                        supportSurface: DecorationSupportSurface.Ceiling);
                    if (!hanger.IsAir)
                        chunk.SetBlock(localX, localY, localZ, hanger);
                }
                if (includeObjects &&
                    (uint)(localY + 1) < Chunk.Size)
                {
                    var objectId = _decorations.ObjectAt(
                        decoratorBiome, ceilingSupport, worldX, worldZ,
                        placement, verticalY: worldY,
                        submerged: submerged, supportY: worldY + 1,
                        supportSurface: DecorationSupportSurface.Ceiling);
                    if (objectId is not null)
                    {
                        pendingObjects ??= [];
                        pendingObjects.Add(new PendingGeneratedObject(
                            localX, localY + 1, localZ,
                            objectId, BlockFace.Bottom, submerged));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies wall-attached objects only to real density-exposed side faces.
    /// Face selection is stable in world space, and adjacent chunk columns
    /// are sampled from the same immutable terrain/material owners.
    /// This never constructs a second world or fluid representation.
    /// </summary>

    /// <summary>
    /// Procedural wall overlays belong to their solid host face. Neighbor
    /// density is immutable even across chunk borders; never write into a
    /// neighbor chunk or create a second wall decoration block.
    /// </summary>
    private void MaterializeWallLayers(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ,
        ref List<PendingGeneratedObject>? pendingObjects)
    {
        if (!_decorations.HasWallLayers && !_decorations.HasWallObjects)
            return;

        ReadOnlySpan<(int Dx, int Dz, BlockFace Face)> faces =
        [
            (1, 0, BlockFace.Right),
            (-1, 0, BlockFace.Left),
            (0, 1, BlockFace.Front),
            (0, -1, BlockFace.Back),
        ];

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldZ = originZ + z;
            var surface = column.BiomeAt(x, z);
            var baseY = column.BaseHeightAt(x, z);
            var surfaceSupportsLayers =
                _decorations.HasWallLayersFor(surface.Primary) ||
                _decorations.HasWallObjectsFor(surface.Primary);
            if (!surfaceSupportsLayers &&
                !CaveNeighborSupportsWallDecorations(
                    column, x, z, worldX, worldZ, 1, 0) &&
                !CaveNeighborSupportsWallDecorations(
                    column, x, z, worldX, worldZ, -1, 0) &&
                !CaveNeighborSupportsWallDecorations(
                    column, x, z, worldX, worldZ, 0, 1) &&
                !CaveNeighborSupportsWallDecorations(
                    column, x, z, worldX, worldZ, 0, -1))
            {
                // A cave wall overlay is owned by the adjacent empty-side
                // column. If none of the four possible owners has wall
                // decorations, this host column cannot receive any layer or
                // projected wall Object at any Y.
                continue;
            }

            // No exterior or cave face below this column can occur in
            // sky-only vertical chunks.
            if (originY > baseY)
                continue;

            var upper = Math.Min(Chunk.Size, baseY - originY + 1);
            for (var y = 0; y < upper; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 ||
                    (_floorY is { } floor && worldY <= floor) ||
                    (_roofY is { } roof && worldY >= roof))
                    continue;

                var host = chunk.GetCell(x, y, z);
                if (host.IsEmpty ||
                    !densityVolume.IsSolidAtUnchecked(x, y, z))
                    continue;

                foreach (var side in faces)
                {
                    var nx = x + side.Dx;
                    var nz = z + side.Dz;
                    var px = worldX + side.Dx;
                    var pz = worldZ + side.Dz;
                    var local = Chunk.Contains(nx, y, nz);

                    if (local)
                    {
                        if (!chunk.GetCell(nx, y, nz).IsEmpty ||
                            densityVolume.IsSolidAtUnchecked(nx, y, nz))
                            continue;
                    }
                    else if (DensityAtWorld(px, worldY, pz) >= 0d)
                        continue;

                    // Resolve the actual empty-side biome without changing
                    // its chunk. For cave walls, the carved void owns the
                    // attached layer; for exterior cliffs, the surface does.
                    var neighborColumn = local ? column :
                        _columns.Get(
                            (int)Math.Floor((double)px / Chunk.Size),
                            (int)Math.Floor((double)pz / Chunk.Size));
                    var sx = ((nx % Chunk.Size) + Chunk.Size) % Chunk.Size;
                    var sz = ((nz % Chunk.Size) + Chunk.Size) % Chunk.Size;
                    var neighborBaseY =
                        neighborColumn.BaseHeightAt(sx, sz);
                    BiomeSample? owner;
                    if (worldY <= neighborBaseY &&
                        (local
                            ? IsLocalDensityCaveVoid(
                                neighborColumn, sx, sz, worldY)
                            : _terrain.IsCaveVoidAt(neighborBaseY,
                                new WorldVoxelCoord(px, worldY, pz))))
                    {
                        owner = neighborColumn.CaveBiomeAt(sx, sz);
                    }
                    else if (surfaceSupportsLayers &&
                             worldY > neighborBaseY &&
                             neighborColumn.BiomeAt(sx, sz).Primary ==
                                 surface.Primary)
                    {
                        owner = surface;
                    }
                    else
                    {
                        continue;
                    }

                    if (owner is null ||
                        (!_decorations.HasWallLayersFor(owner.Primary) &&
                         !_decorations.HasWallObjectsFor(owner.Primary)))
                        continue;

                    if (_decorations.HasWallLayersFor(owner.Primary))
                    {
                        var layerId = _decorations.WallLayerAt(
                            owner, host.Block, worldX, worldY, worldZ);
                        if (layerId is not null)
                        {
                            var previous = chunk.GetSurfaceState(x, y, z);
                            if (previous.TryAttach(
                                new AttachedBlockLayer(side.Face, layerId),
                                out var updated))
                                chunk.SetSurfaceState(x, y, z, updated);
                        }
                    }
                    if (_decorations.HasWallObjectsFor(owner.Primary))
                    {
                        var objectId = _decorations.WallObjectAt(
                            owner, host.Block, worldX, worldY, worldZ);
                        if (objectId is not null)
                        {
                            pendingObjects ??= [];
                            pendingObjects.Add(new PendingGeneratedObject(
                                x, y, z, objectId, side.Face));
                        }
                    }
                }
            }
        }
    }

    private bool CaveNeighborSupportsWallDecorations(
        SurfaceTerrainColumn column,
        int localX,
        int localZ,
        int worldX,
        int worldZ,
        int dx,
        int dz)
    {
        var neighborX = localX + dx;
        var neighborZ = localZ + dz;
        var neighborWorldX = worldX + dx;
        var neighborWorldZ = worldZ + dz;
        var local = (uint)neighborX < Chunk.Size &&
            (uint)neighborZ < Chunk.Size;
        var neighborColumn = local
            ? column
            : _columns.Get(
                (int)Math.Floor((double)neighborWorldX / Chunk.Size),
                (int)Math.Floor((double)neighborWorldZ / Chunk.Size));
        var sampleX = (neighborX + Chunk.Size) % Chunk.Size;
        var sampleZ = (neighborZ + Chunk.Size) % Chunk.Size;
        var caveBiome = neighborColumn.CaveBiomeAt(sampleX, sampleZ);
        return caveBiome is not null &&
            (_decorations.HasWallLayersFor(caveBiome.Primary) ||
             _decorations.HasWallObjectsFor(caveBiome.Primary));
    }

    /// <summary>
    /// Renders physical wall plants as actual objects in the adjacent air
    /// voxel. Flat coatings are owned separately by MaterializeWallLayers on
    /// the solid host's BlockSurfaceState, never as projected block objects.
    /// Neighboring chunks are queried without mutating their cells.
    /// </summary>
    private void MaterializeWallObjects(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ)
    {
        if (!_decorations.HasWallBlocks)
            return;

        // Outward normals point away from solid backing support.
        ReadOnlySpan<(int Dx, int Dz, HorizontalFacing Facing)> supports =
        [
            (0, -1, HorizontalFacing.South),
            (1, 0, HorizontalFacing.West),
            (0, 1, HorizontalFacing.North),
            (-1, 0, HorizontalFacing.East),
        ];

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var worldX = originX + x;
            var worldZ = originZ + z;
            var surface = column.BiomeAt(x, z);
            var baseY = column.BaseHeightAt(x, z);
            var surfaceSupportsObjects = _decorations.HasWallBlocksFor(surface.Primary);
            var caveBiome = column.CaveBiomeAt(x, z);
            var caveSupportsObjects = caveBiome is not null &&
                _decorations.HasWallBlocksFor(caveBiome.Primary);
            if (!surfaceSupportsObjects && !caveSupportsObjects)
                continue;
            if (originY > baseY && !surfaceSupportsObjects)
                continue;

            for (var y = 0; y < Chunk.Size; y++)
            {
                var worldY = originY + y;
                if (worldY <= 0 ||
                    (_floorY is { } floor && worldY <= floor) ||
                    (_roofY is { } roof && worldY >= roof) ||
                    !chunk.GetCell(x, y, z).IsEmpty ||
                    !chunk.GetFluid(x, y, z).IsEmpty ||
                    densityVolume.IsSolidAtUnchecked(x, y, z))
                    continue;

                var exterior = worldY > baseY && surfaceSupportsObjects;
                var cave = worldY <= baseY &&
                    _volumeBiomes is { HasBiomes: true } &&
                    _terrain.IsCaveVoidAt(baseY,
                        new WorldVoxelCoord(worldX, worldY, worldZ));
                if (!exterior && !cave)
                    continue;
                var owner = exterior
                    ? surface
                    : caveBiome;
                if (owner is null ||
                    !_decorations.HasWallBlocksFor(owner.Primary))
                    continue;

                foreach (var side in supports)
                {
                    var nx = x + side.Dx;
                    var nz = z + side.Dz;
                    var neighborX = worldX + side.Dx;
                    var neighborZ = worldZ + side.Dz;
                    var local = Chunk.Contains(nx, y, nz);
                    var solid = local
                        ? densityVolume.IsSolidAtUnchecked(nx, y, nz)
                        : DensityAtWorld(neighborX, worldY, neighborZ) >= 0d;
                    if (!solid)
                        continue;

                    BlockRuntimeId backingBlock;
                    if (exterior)
                    {
                        var (neighborCoord, neighborLocal) =
                            VoxelCoordinates.FromWorld(neighborX, 0, neighborZ);
                        var supportingColumn = local
                            ? column
                            : _columns.Get(neighborCoord.X, neighborCoord.Z);
                        var neighborBiome = supportingColumn.BiomeAt(
                            neighborLocal.X, neighborLocal.Z);
                        var neighborBaseY = supportingColumn.BaseHeightAt(
                            neighborLocal.X, neighborLocal.Z);
                        if (neighborBiome.Primary != owner.Primary ||
                            worldY > neighborBaseY)
                            continue;
                        backingBlock = local
                            ? chunk.GetBlock(nx, y, nz)
                            : _materials.BlockAt(
                                neighborBiome, neighborX, neighborZ,
                                (uint)(neighborBaseY - worldY));
                    }
                    else
                    {
                        backingBlock = local
                            ? chunk.GetBlock(nx, y, nz)
                            : _materials.VolumeBlockAt(
                                owner, neighborX, worldY, neighborZ,
                                BiomePaletteFace.Walls);
                    }
                    if (backingBlock.IsAir)
                        continue;

                    var block = _decorations.WallBlockAt(
                        owner, backingBlock, worldX, worldY, worldZ);
                    if (block.IsAir)
                        continue;

                    chunk.SetCell(x, y, z,
                        new VoxelCell(block, facing: side.Facing));
                    break;
                }
            }
        }
    }

    private void MaterializeCaveSpikes(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ)
    {
        if (_caveSpikes is not { HasRules: true } spikes ||
            _volumeBiomes is not { HasBiomes: true } volumeBiomes)
            return;

        var maximumHeight = spikes.MaximumHeight;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var baseY = column.BaseHeightAt(x, z);
            var first = Math.Max(1, originY - maximumHeight + 1);
            var last = Math.Min(baseY - 1,
                originY + Chunk.Size + maximumHeight - 2);
            // When no cavern anchor could possibly contribute a segment
            // to this chunk, skip biome identity/terrain sampling entirely.
            if (last < first)
                continue;

            // Cave-spike rules are owned by the carved-void biome. Columns
            // without an eligible owner cannot produce spikes, so reject
            // them before scanning the chunk's vertical occupancy mask.
            var biome = column.CaveBiomeAt(x, z);
            if (biome is null || !spikes.HasRulesFor(biome.Primary))
                continue;

            // A generated spike must occupy empty density inside this
            // chunk. A fully solid column has no contributing formation
            // even if an anchor in a neighboring vertical chunk qualifies.
            var containsVoid = false;
            for (var localY = 0; localY < Chunk.Size; localY++)
            {
                // Sky above the surface and gaps in additive masses are
                // not cave space. Only a carved void below the base surface
                // could contain a generated cave spike.
                if (originY + localY < baseY &&
                    !densityVolume.IsSolidAtUnchecked(x, localY, z))
                {
                    containsVoid = true;
                    break;
                }
            }
            if (!containsVoid)
                continue;

            var worldX = originX + x;
            var worldZ = originZ + z;
            var surfaceBiome = column.BiomeAt(x, z);
            var volumeBiome = densityVolume.VolumePlacementAt(x, z);
            for (var anchorY = first; anchorY <= last; anchorY++)
            {
                // TrySample rejects a non-void anchor before producing any
                // placement: IsVoid(anchorY) is its first density check.
                // When the anchor is inside this chunk, reuse the already
                // sampled density and skip both direction probes immediately.
                var localAnchorY = anchorY - originY;
                if ((uint)localAnchorY < Chunk.Size &&
                    densityVolume.IsSolidAtUnchecked(x, localAnchorY, z))
                    continue;

                for (var direction = 0; direction < 2; direction++)
                {
                    var down = direction == 1;
                    if (!spikes.TrySample(
                            biome.Primary, surfaceBiome, volumeBiome, baseY,
                            worldX, anchorY, worldZ, down,
                            densityVolume, originY, x, z,
                            out var placement))
                        continue;

                    // A blocked root or intermediate tier invalidates
                    // this formation in the current chunk. Never emit the
                    // remaining tiers as a floating, disconnected spike.
                    var blocked = false;
                    for (var segment = 0; segment < placement.Height; segment++)
                    {
                        var worldY = anchorY + (down ? -segment : segment);
                        var localY = worldY - originY;
                        if ((uint)localY >= Chunk.Size)
                            continue;
                        if (!chunk.GetCell(x, localY, z).IsEmpty ||
                            !chunk.GetFluid(x, localY, z).IsEmpty)
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked)
                        continue;

                    for (var segment = 0; segment < placement.Height; segment++)
                    {
                        var worldY = anchorY + (down ? -segment : segment);
                        var localY = worldY - originY;
                        if ((uint)localY >= Chunk.Size)
                            continue;

                        chunk.SetCell(x, localY, z,
                            new VoxelCell(placement.Block,
                                state: SpikeSegmentState.Encode(
                                    segment, placement.Height, down)));
                    }
                }
            }
        }
    }

    private bool HasGeneratedFluidAboveSurface(
        BiomeSample fluidBiome,
        BiomeSample surfaceBiome,
        int baseY,
        int surfaceFluidCutDepth,
        int worldX,
        int surfaceY,
        int worldZ,
        BiomeSample? volumePlacement)
    {
        var aboveY = (long)surfaceY + 1L;
        if (aboveY < 0 || aboveY > int.MaxValue)
            return false;

        var y = (int)aboveY;
        // The column and density volume already carry the authoritative
        // surface biome, base height and volume placement for this X/Z.
        // Reuse them instead of constructing new scalar-sample caches.
        var density = _terrain.DensityAt(
            surfaceBiome, baseY, worldX, y, worldZ, volumePlacement);
        var fluid = _generatedFluids.FluidAt(
            fluidBiome, baseY, surfaceFluidCutDepth,
            worldX, y, worldZ, density);
        return !fluid.IsEmpty;
    }

    private void MaterializeGeneratedFluids(
        Chunk chunk,
        TerrainDensityVolume densityVolume,
        ColumnMaterializationCache[] columnMaterializationCaches,
        int originY,
        int topExclusive)
    {
        if (!_generatedFluids.HasRules)
            return;

        // Resolve the authored fluid policy once per column. The reusable
        // column cache already carries biome, base height and surface-cut
        // metadata, so the fluid pass does not need parallel per-column arrays.
        Span<int> firstFluidYByColumn = stackalloc int[Chunk.Area];
        Span<int> lastFluidYByColumn = stackalloc int[Chunk.Area];
        firstFluidYByColumn.Fill(int.MaxValue);
        lastFluidYByColumn.Fill(int.MinValue);

        var firstWorldY = int.MaxValue;
        var lastWorldY = int.MinValue;
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var columnIndex = localX + Chunk.Size * localZ;
                ref var state = ref columnMaterializationCaches[columnIndex];
                var sample = state.Sample ??
                    throw new InvalidOperationException(
                        "Materialization column has no biome sample.");
                var baseY = state.BaseY;
                var surfaceCutDepth = state.SurfaceFluidCutDepth;

                if (!_generatedFluids.TryResolveColumn(
                        sample,
                        baseY,
                        surfaceCutDepth,
                        state.WorldX,
                        state.WorldZ,
                        out var fluidPlan,
                        out var minimumY,
                        out var maximumY))
                {
                    state.GeneratedFluidPlan = default;
                    continue;
                }

                state.GeneratedFluidPlan = fluidPlan;
                var firstY = Math.Max(originY, minimumY);
                var lastY = Math.Min(topExclusive - 1, maximumY);
                if (firstY > lastY)
                    continue;

                firstFluidYByColumn[columnIndex] = firstY;
                lastFluidYByColumn[columnIndex] = lastY;
                firstWorldY = Math.Min(firstWorldY, firstY);
                lastWorldY = Math.Max(lastWorldY, lastY);
            }
        }

        if (firstWorldY > lastWorldY)
            return;

        using var initialFluidWrites = chunk.BeginInitialFluidWrites();
        for (var worldY = firstWorldY; worldY <= lastWorldY; worldY++)
        {
            var localY = worldY - originY;
            for (var localZ = 0; localZ < Chunk.Size; localZ++)
            {
                for (var localX = 0; localX < Chunk.Size; localX++)
                {
                    var columnIndex = localX + Chunk.Size * localZ;
                    if (worldY < firstFluidYByColumn[columnIndex] ||
                        worldY > lastFluidYByColumn[columnIndex])
                        continue;

                    var voxelIndex =
                        localX + Chunk.Size * (localZ + Chunk.Size * localY);
                    var block =
                        chunk.GetCellAtLinearIndexUnchecked(voxelIndex).Block;
                    if ((!block.IsAir &&
                         !FluidBlockOccupancy.AllowsFluid(
                             _blocks.GetDefinition(block))) ||
                        !initialFluidWrites.IsEmptyAtLinearIndex(voxelIndex))
                        continue;

                    ref var state = ref columnMaterializationCaches[columnIndex];
                    var fluid = _generatedFluids.FluidAtResolvedColumn(
                        state.GeneratedFluidPlan,
                        state.BaseY,
                        state.SurfaceFluidCutDepth,
                        worldY,
                        densityVolume.IsSolidAtUnchecked(
                            localX,
                            localY,
                            localZ));

                    if (!fluid.IsEmpty)
                    {
                        initialFluidWrites.SetFluidAtLinearIndexIfEmpty(
                            voxelIndex,
                            fluid);
                    }
                }
            }
        }
    }

    private void ConcealSphereShell(
        Chunk chunk,
        SurfaceTerrainColumn column,
        int originY,
        int topExclusive)
    {
        if (_shellBlock.IsAir)
            return;

        var floorLiningY = _floorY is { } floor
            ? (long)floor + 1L : -1L;
        var roofLiningY = _roofY is { } roof
            ? (long)roof - 1L : -1L;
        bool IsInChunk(long y) => y >= originY && y < topExclusive;
        if (!IsInChunk(_floorY ?? -1) &&
            !IsInChunk(_roofY ?? -1) &&
            !IsInChunk(floorLiningY) &&
            !IsInChunk(roofLiningY))
            return;

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var coreBlock = _materials.CoreLayer(column.BiomeAt(x, z)).Block;

            // Seal the playable face with its biome's deep terrain material.
            SealShellLining(chunk, x, z, originY, topExclusive,
                floorLiningY, coreBlock);
            SealShellLining(chunk, x, z, originY, topExclusive,
                roofLiningY, coreBlock);

            // Apply the unbreakable boundary last, after every worldgen pass.
            if (_floorY is { } floorY)
                SetBoundaryBlock(chunk, x, z, originY, topExclusive,
                    floorY, _shellBlock);
            if (_roofY is { } roofY)
                SetBoundaryBlock(chunk, x, z, originY, topExclusive,
                    roofY, _shellBlock);
        }
    }

    private void SealShellLining(
        Chunk chunk,
        int x,
        int z,
        int originY,
        int topExclusive,
        long worldY,
        BlockRuntimeId coreBlock)
    {
        if (worldY < originY || worldY >= topExclusive)
            return;

        var localY = (int)(worldY - originY);
        var current = chunk.GetBlock(x, localY, z);
        if (!current.IsAir)
        {
            var definition = _blocks.GetDefinition(current);
            if (definition.IsOpaque &&
                definition.Shape.Kind == BlockShapeKind.Cube &&
                !chunk.GetCell(x, localY, z).HasMicroblockGeometry)
            {
                // Preserve the biome's legitimate surface if it already
                // conceals the shell; only repair cleared/partial cells.
                chunk.SetFluid(x, localY, z, FluidCell.Empty);
                return;
            }
        }

        SetBoundaryBlock(chunk, x, z, originY, topExclusive,
            worldY, coreBlock);
    }

    private static void SetBoundaryBlock(
        Chunk chunk,
        int x,
        int z,
        int originY,
        int topExclusive,
        long worldY,
        BlockRuntimeId block)
    {
        if (worldY < originY || worldY >= topExclusive)
            return;

        var localY = (int)(worldY - originY);
        chunk.SetBlock(x, localY, z, block);
        chunk.SetFluid(x, localY, z, FluidCell.Empty);
        chunk.SetSurfaceState(x, localY, z, BlockSurfaceState.Empty);
    }

    private void SetShellIfInChunk(
        Chunk.InitialBlockWriter initialBlocks,
        int localX,
        int localZ,
        int originY,
        int topExclusive,
        int? shellY)
    {
        if (shellY is not { } y ||
            y < originY ||
            y >= topExclusive)
        {
            return;
        }

        initialBlocks.SetBlockAtLinearIndex(
            localX + Chunk.Size * localZ + Chunk.Area * (y - originY),
            _shellBlock);
    }
}
