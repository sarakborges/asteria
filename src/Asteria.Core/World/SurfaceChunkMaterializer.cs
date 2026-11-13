using System.Buffers;
using System.Runtime.CompilerServices;

#if DEBUG
using System.Diagnostics;
#endif

namespace Asteria.Core.World;

internal readonly record struct DirectionalFacePaletteReapplicationDiagnostic(
    WorldVoxelCoord Position,
    BlockRuntimeId RestoredBlock,
    bool WasTerrainVoxel,
    bool HadCandidates,
    string? SolidOwnerBiome,
    BiomeRegionOwnerKind? SolidOwnerKind,
    string? AppliedOwnerBiome,
    BiomeRegionOwnerKind? AppliedOwnerKind,
    BiomePaletteFace? Face,
    uint? Depth,
    WorldVoxelCoord? EmptyNeighbor,
    BlockRuntimeId? Replacement,
    bool Applied);

/// <summary>
/// Only the materialization stage writes chunk voxels. It consumes immutable
/// terrain/biome snapshots and material/decorator capabilities.
/// </summary>
public sealed partial class SurfaceChunkMaterializer
{
    private const int TerrainRowsPerWorkerSlice = 4;
    private const int VolumeStructureCandidatesPerAdvance = 4;
    private const int VolumeStructureVoxelsPerAdvance = 64;
    private readonly record struct DirectionalFaceOccupancyPreparationProfile(
        double DensitySlabReadMilliseconds,
        double HorizontalBoundaryReadMilliseconds,
        double OccupancyRowAssemblyMilliseconds,
        double CandidateMaskBuildMilliseconds,
        double BoundaryColumnBoundsMilliseconds,
        double CaveSamplerBuildMilliseconds,
        double BoundaryColumnEvaluationMilliseconds,
        double DensityTerrainColumnMilliseconds,
        double OccupancyCacheAccessMilliseconds,
        double SurfaceBiomeSamplingMilliseconds,
        double BaseHeightSamplingMilliseconds,
        double VolumeBiomeSamplingMilliseconds,
        int HorizontalBoundaryBatchCount,
        int HorizontalBoundaryColumnCount);

    private readonly record struct BoundaryMaskReadMetrics(
        int DensityProbes,
        int BatchCount,
        int ColumnCount,
        double ColumnBoundsMilliseconds,
        double CaveSamplerBuildMilliseconds,
        double ColumnEvaluationMilliseconds,
        double DensityTerrainColumnMilliseconds,
        double OccupancyCacheAccessMilliseconds,
        double SurfaceBiomeSamplingMilliseconds,
        double BaseHeightSamplingMilliseconds,
        double VolumeBiomeSamplingMilliseconds);

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
        public BiomeSurfaceMaterialField.GeologySampler SurfaceGeology;
        public bool HasSurfaceMaterialMetadata;
        public BiomeSurfaceMaterialColumn? SurfaceMaterials;
        public BiomeSample? VolumeSample;
        public BiomeSurfaceMaterialField.GeologySampler VolumeGeology;
        public BiomeSurfaceMaterialColumn? VolumeMaterials;
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
                MaterializeSurfaceDecorations(job, job.SupportOutputColumns);
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
                if (MaterializeVolumeStructures(job))
                    job.Stage = ChunkMaterializationStage.GeneratedFluids;
                break;

            case ChunkMaterializationStage.GeneratedFluids:
                MaterializeGeneratedFluids(
                    job.Chunk, RequiredDensity(job),
                    job.ColumnMaterializationCaches ??
                        throw new InvalidOperationException(
                            "Materialization column cache is missing."),
                    job.OriginY, job.TopExclusive,
                    job.SupportMaterializationColumns);
                job.Stage = ChunkMaterializationStage.SurfaceStructures;
                break;

            case ChunkMaterializationStage.SurfaceStructures:
                MaterializeStructures(
                    job.Chunk, job.Coord, job.OriginY, job.TopExclusive,
                    job.SupportMaterializationColumns);
                job.Stage = ChunkMaterializationStage.CaveSpikes;
                break;

            case ChunkMaterializationStage.CaveSpikes:
                MaterializeCaveSpikes(
                    job.Chunk, RequiredColumn(job), RequiredDensity(job),
                    job.OriginX, job.OriginY, job.OriginZ,
                    job.SupportOutputColumns);
                job.Stage = ChunkMaterializationStage.VerticalDecorations;
                break;

            case ChunkMaterializationStage.VerticalDecorations:
            {
                var pendingObjects = job.PendingObjects;
                MaterializeVerticalDecorations(
                    job.Chunk, RequiredColumn(job), RequiredDensity(job),
                    job.OriginX, job.OriginY, job.OriginZ,
                    ref pendingObjects,
                    includeObjects: !job.SupportOnly,
                    columnMask: job.SupportOutputColumns);
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
                    job.OriginX, job.OriginY, job.OriginZ,
                    job.SupportMaterializationColumns);
                job.Stage = ChunkMaterializationStage.ShellFinalization;
                break;

            case ChunkMaterializationStage.ShellFinalization:
                ConcealSphereShell(
                    job.Chunk, RequiredColumn(job), job.OriginY, job.TopExclusive,
                    job.SupportMaterializationColumns);
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
                ValidateGravitySupportBeforePublication(job);
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
        var densityVolume = job.DensityVolume =
            job.SupportFaceFastPath || job.IsSparseSupportFallback
                ? _terrain.CreateLazyDensityVolume(
                    column, originX, originY, originZ)
                : _columns.SampleDensityVolume(
                    column,
                    originX,
                    originY,
                    originZ);
#if DEBUG
        var directionalPreparationStarted = Stopwatch.GetTimestamp();
#endif
        // Occupancy-preserving palettes are skipped by support-only jobs, so
        // those jobs must not build directional candidates they will never consume.
        if (_materials.HasDirectionalFacePalettes &&
            !job.SupportFaceFastPath &&
            !(job.SupportOnly && _materials.FacePalettesPreserveOccupancy))
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
            if (job.SupportMaterializationColumns is { } supportColumns &&
                !supportColumns[index])
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
            job.NextTerrainLocalZ = 0;
            return;
        }

        job.NextTerrainLocalY = firstTerrainWorldY - originY;
        job.NextTerrainLocalZ = 0;

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
        var columnSolidMasks = densityVolume.ColumnSolidMasks;
        var localY = job.NextTerrainLocalY;
        var solidBit = (ushort)(1u << localY);
        var worldY = checked(originY + localY);
        var minimumTerrainY = _floorY ?? 0;
        var maximumTerrainY = _roofY ?? int.MaxValue;
        if (worldY < minimumTerrainY ||
            worldY > maximumTerrainY ||
            (!_shellBlock.IsAir &&
             (worldY == _floorY || worldY == _roofY)))
        {
            // Every column in this layer is outside the playable terrain
            // range or belongs to an already-written shell plane.
            job.NextTerrainLocalY++;
            job.NextTerrainLocalZ = 0;
            return;
        }

        // One Z row is the cooperative terrain quantum. The sparse support-face
        // fast path is intentionally kept as one layer: it visits only the
        // requested boundary columns and must finish its two-voxel query before
        // the parent gravity-support continuation resumes.
        var firstLocalZ = job.SupportFaceFastPath ? 0 : job.NextTerrainLocalZ;
        var lastLocalZExclusive = job.SupportFaceFastPath
            ? Chunk.Size
            : Math.Min(
                Chunk.Size, firstLocalZ + TerrainRowsPerWorkerSlice);
        for (var localZ = firstLocalZ;
             localZ < lastLocalZExclusive;
             localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            var columnIndex = localX + Chunk.Size * localZ;
            if (job.SupportMaterializationColumns is { } supportColumns &&
                !supportColumns[columnIndex])
                continue;

            ref var state = ref columnMaterializationCaches[columnIndex];
            var surfaceY = state.SurfaceY;
            if (worldY > surfaceY ||
                (columnSolidMasks is null
                    ? !densityVolume.IsSolidAtUnchecked(
                        localX, localY, localZ)
                    : (columnSolidMasks[columnIndex] & solidBit) == 0))
            {
                continue;
            }

            // Only solid cells below their column surface need the
            // heavier biome/material metadata from the column cache.
            var sample = state.Sample ??
                throw new InvalidOperationException(
                    "Materialization column has no biome sample.");
            var baseY = state.BaseY;
            var surfaceFluidCutDepth = state.SurfaceFluidCutDepth;
            var worldX = state.WorldX;
            var worldZ = state.WorldZ;

            if (!state.HasSurfaceMaterialMetadata)
            {
                state.SurfaceCore = _materials.CoreLayer(sample);
                state.SurfaceGeology = _materials.ResolveGeology(sample);
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
                        (columnSolidMasks is null
                            ? densityVolume.IsSolidAtUnchecked(
                                localX, aboveLocalY, localZ)
                            : (columnSolidMasks[columnIndex] &
                               (1u << aboveLocalY)) != 0))
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
                var exposedGeology = state.SurfaceGeology;
                BiomeSample surfaceOwner;
                if (worldY > baseY)
                {
                    surfaceOwner = state.VolumeSample ??
                        densityVolume.VolumeBiomeAt(
                            localX, worldY, localZ) ??
                        throw new InvalidOperationException(
                            "Additive surface has no volume biome owner.");
                    if (state.VolumeSample is null)
                    {
                        state.VolumeSample = surfaceOwner;
                        state.VolumeGeology =
                            _materials.ResolveGeology(surfaceOwner);
                    }
                    exposedGeology = state.VolumeGeology;
                }
                else
                {
                    surfaceOwner = sample;
                }

                if (!exposedGeology.RequiresSubmergedSurface ||
                    HasGeneratedFluidAboveSurface(
                        surfaceOwner, sample, baseY,
                        surfaceFluidCutDepth, worldX, worldY, worldZ,
                        densityVolume.VolumePlacementAt(localX, localZ)))
                {
                    hasExposedGeology = exposedGeology.TrySurfaceBlockAtCached(
                        worldX, worldY, worldZ,
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
                    state.SurfaceGeology.SurfaceCoverDepth is { } authoredCoverDepth
                        ? depth < authoredCoverDepth
                        : depth == 0 || depth < surfaceMaterials.FiniteDepth;
                if (useSurfaceCover)
                {
                    block = surfaceMaterials.BlockAt(depth);
                }
                else if (state.SurfaceGeology.SurfaceCoverDepth is not null &&
                         state.SurfaceGeology.TryBlockAtCached(
                             worldX, worldY, worldZ,
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
                    state.VolumeGeology =
                        _materials.ResolveGeology(volumeSample);
                }

                var volumeMaterials = state.VolumeMaterials ??=
                    _materials.SampleColumn(volumeSample, worldX, worldZ);
                var depth = AdditiveDepthAt(
                    densityVolume, sample, baseY, localX, localY, localZ,
                    worldX, worldY, worldZ, volumeMaterials.FiniteDepth);
                var useSurfaceCover =
                    state.VolumeGeology.SurfaceCoverDepth is { } authoredCoverDepth
                        ? depth < authoredCoverDepth
                        : depth == 0 || depth < volumeMaterials.FiniteDepth;
                if (useSurfaceCover)
                {
                    block = volumeMaterials.BlockAt(depth);
                }
                else if (state.VolumeGeology.SurfaceCoverDepth is not null &&
                         state.VolumeGeology.TryBlockAtCached(
                             worldX, worldY, worldZ,
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
            if (initialBlocks.SetBlockAtLinearIndexFreshUnchecked(voxelIndex, block) &&
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

        if (job.SupportFaceFastPath)
        {
            job.NextTerrainLocalZ = 0;
            job.NextTerrainLocalY++;
        }
        else
        {
            job.NextTerrainLocalZ = lastLocalZExclusive;
            if (job.NextTerrainLocalZ >= Chunk.Size)
            {
                job.NextTerrainLocalZ = 0;
                job.NextTerrainLocalY++;
            }
        }
    }

    private void MaterializeSurfaceDecorations(
        MaterializationJob job,
        bool[]? columnMask)
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
                var columnIndex = localX + Chunk.Size * localZ;
                if (columnMask is not null &&
                    !columnMask[columnIndex])
                    continue;

                ref var state = ref columnMaterializationCaches[columnIndex];
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

                var considerGroundBlock = normalInChunk &&
                    _decorations.HasSurfacePlacementCandidates(
                        topSample, topBlock,
                        requireObject: false, onFluidSurface: false);
                var considerObjects = !job.SupportOnly &&
                    objectSupportInChunk &&
                    _decorations.HasSurfacePlacementCandidates(
                        topSample, topBlock,
                        requireObject: true, onFluidSurface: false);
                if (considerGroundBlock || considerObjects)
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

                    if (considerGroundBlock)
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
                    if (considerObjects)
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
                var considerFluidBlock = fluidTopInChunk &&
                    _decorations.HasSurfacePlacementCandidates(
                        topSample, topBlock,
                        requireObject: false, onFluidSurface: true);
                var considerFluidObject = !job.SupportOnly &&
                    fluidObjectSupportInChunk &&
                    _decorations.HasSurfacePlacementCandidates(
                        topSample, topBlock,
                        requireObject: true, onFluidSurface: true);
                if (considerFluidBlock || considerFluidObject)
                {
                    var supportFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY, worldZ);
                    var aboveFluid = _generatedFluids.FluidAtEmptyVoxel(
                        sample, baseY, surfaceFluidCutDepth,
                        worldX, (int)decorationY + 1, worldZ);
                    if (!supportFluid.IsEmpty && aboveFluid.IsEmpty)
                    {
                        if (considerFluidBlock)
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
                            if (!decoration.IsAir)
                                chunk.SetBlock(
                                    localX, (int)decorationY + 1 - originY,
                                    localZ, decoration);
                        }
                        if (considerFluidObject)
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
                // A support-only query must validate the complete gravity
                // stack in each requested X/Z column. Checking just the top
                // face can accept a gravity block resting on another gravity
                // block whose own support is deeper in an unloaded chunk.
                var supportColumns = job.SupportFaceColumns;
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var columnIndex = x + Chunk.Size * z;
                    if (supportColumns is not null &&
                        !supportColumns[columnIndex])
                        continue;

                    for (var y = 0; y < Chunk.Size; y++)
                    {
                        var cell = job.Chunk.GetCell(x, y, z);
                        if (_gravityBlocks.Contains(cell.Block))
                            positions.Add((x, y, z));
                    }
                }
            }
            else
            {
                job.Chunk.VisitBlockCells((x, y, z, cell) =>
                {
                    if (_gravityBlocks.Contains(cell.Block))
                        positions.Add((x, y, z));
                });
            }

            positions.Sort(static (left, right) =>
            {
                var result = left.Y.CompareTo(right.Y);
                if (result != 0) return result;
                result = left.Z.CompareTo(right.Z);
                return result != 0 ? result : left.X.CompareTo(right.X);
            });
            job.GravityPositions = positions;

            // The child dependency must survive across multiple worker slices.
            if (job.OriginY > 0 &&
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
                        supportOnly: true,
                        supportFaceColumns: fallbackColumns);
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
        var top = supportJob.Chunk.GetCell(localX, topLocalY, localZ);
        if (top.IsEmpty || !_materials.FacePalettesPreserveOccupancy)
            return false;

        // A gravity block may appear supported by another gravity block
        // whose own support lies deeper in the chunk. This sparse query only
        // materializes the top two rows, so it cannot prove that the entire
        // gravity stack reaches stable terrain. Let the complete materializer
        // resolve any gravity-owned support face before it is trusted.
        if (_gravityBlocks.Contains(top.Block))
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

    private void ValidateGravitySupportBeforePublication(
        MaterializationJob job)
    {
        var visitedBlockCells = 0;
        var visitedGravityCells = 0;
        var directlyReadBlockCells = 0;
        var directlyReadGravityCells = 0;
        var gravitySetMisses = 0;
        List<string>? discrepancies = null;
        List<string>? unsupported = null;

        // Compare sparse palette enumeration with authoritative indexed reads.
        // This is deliberately a full-volume verification: it detects a stale
        // occupancy bitmap that could hide a generated gravity voxel from the
        // GravitySupport pass but expose it after later palette mutations.
        job.Chunk.VisitBlockCells((x, y, z, cell) =>
        {
            visitedBlockCells++;
            var definition = _blocks.GetDefinition(cell.Block);
            if (!definition.HasTag(BlockPhysicsCapabilities.Gravity))
                return;

            visitedGravityCells++;
            if (!_gravityBlocks.Contains(cell.Block))
                gravitySetMisses++;
        });

        for (var y = 0; y < Chunk.Size; y++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var cell = job.Chunk.GetCell(x, y, z);
            if (cell.IsEmpty)
                continue;

            directlyReadBlockCells++;
            var definition = _blocks.GetDefinition(cell.Block);
            if (!definition.HasTag(BlockPhysicsCapabilities.Gravity))
                continue;

            directlyReadGravityCells++;
            var recognizedBySet = _gravityBlocks.Contains(cell.Block);
            var supported = y > 0
                ? !job.Chunk.GetCell(x, y - 1, z).IsEmpty
                : job.OriginY == 0 ||
                  (job.SupportChunkTopOccupancy is { } supportFace &&
                   supportFace[x + Chunk.Size * z]);

            if (recognizedBySet && supported)
                continue;

            unsupported ??= new List<string>(8);
            if (unsupported.Count < 16)
            {
                var below = y > 0
                    ? _blocks.GetDefinition(job.Chunk.GetBlock(x, y - 1, z)).Id
                    : "chunk-seam";
                unsupported.Add(
                    $"{definition.Id}@{job.OriginX + x},{job.OriginY + y},{job.OriginZ + z}" +
                    $"(local={x},{y},{z},set={recognizedBySet},below={below})");
            }
        }

        if (visitedBlockCells != directlyReadBlockCells ||
            visitedGravityCells != directlyReadGravityCells)
        {
            discrepancies = new List<string>(1)
            {
                $"sparseVisit={visitedBlockCells}/{visitedGravityCells} " +
                $"directRead={directlyReadBlockCells}/{directlyReadGravityCells}"
            };
        }

        if (unsupported is { Count: > 0 } ||
            discrepancies is { Count: > 0 } ||
            gravitySetMisses != 0)
        {
            throw new InvalidOperationException(
                $"Generated chunk {job.Coord} failed the final gravity/occupancy invariant " +
                $"(gravityCells={directlyReadGravityCells}, visitedGravityCells={visitedGravityCells}, " +
                $"gravitySetMisses={gravitySetMisses}, gravityPositions={job.GravityPositions?.Count ?? -1}): " +
                string.Join("; ", discrepancies ?? []) +
                string.Join("; ", unsupported ?? []));
        }
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

    private sealed class MaterializationJob : IChunkMaterializationJob, IChunkMaterializationStorageDiagnostics
    {
        private readonly SurfaceChunkMaterializer _owner;
        private readonly int _supportRequestedColumnCount;
        private readonly int _supportMaterializationColumnCount;
        private WorldGenerationEntropy.ValueNoise3DLatticeCache? _geologyNoiseCache;
        private Chunk? _chunk;
#if DEBUG
        private bool _storageDiagnosticsAvailable = true;
        private long _terrainLayerBlockPaletteGrowthBytes;
        private int _terrainLayerBlockPaletteGrowthEvents;
        private long _terrainLayerDirectionalPaletteGrowthBytes;
        private int _terrainLayerDirectionalPaletteGrowthEvents;
        private long _directionalFaceBlockPaletteGrowthBytes;
        private int _directionalFaceBlockPaletteGrowthEvents;
        private long _directionalFaceMetadataPaletteGrowthBytes;
        private int _directionalFaceMetadataPaletteGrowthEvents;
        private int _terrainLayerGeologyNoiseCacheCapacity;
        private long _terrainLayerGeologyNoiseCacheHits;
        private long _terrainLayerGeologyNoiseCacheMisses;
        private long _terrainLayerGeologyNoiseCacheCollisions;
#endif

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
            IsSparseSupportFallback =
                supportOnly &&
                !supportFaceFastPath &&
                owner._materials.FacePalettesPreserveOccupancy &&
                supportFaceColumns is not null;
            SupportMaterializationColumns = supportFaceFastPath
                ? supportFaceColumns
                : IsSparseSupportFallback
                    ? ExpandCardinalSupportColumns(supportFaceColumns!)
                    : null;
            _supportRequestedColumnCount = CountColumns(supportFaceColumns);
            _supportMaterializationColumnCount =
                SupportMaterializationColumns is { } materializationColumns
                    ? CountColumns(materializationColumns)
                    : supportOnly ? Chunk.Area : 0;
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

        public bool ActiveSupportOnly =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveSupportOnly
                : SupportOnly;

        public bool ActiveSupportFaceFastPath =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveSupportFaceFastPath
                : SupportFaceFastPath;

        public bool ActiveSparseSupportFallback =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveSparseSupportFallback
                : IsSparseSupportFallback;

        public int ActiveSupportRequestedColumnCount =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveSupportRequestedColumnCount
                : _supportRequestedColumnCount;

        public int ActiveSupportMaterializationColumnCount =>
            SupportChunkJob is { IsComplete: false } child
                ? child.ActiveSupportMaterializationColumnCount
                : _supportMaterializationColumnCount;

        public bool SupportOnly { get; }

        public bool SupportFaceFastPath { get; }

        public bool[]? SupportFaceColumns { get; }

        public bool IsSparseSupportFallback { get; }

        public bool[]? SupportMaterializationColumns { get; }

        public bool[]? SupportOutputColumns =>
            IsSparseSupportFallback
                ? SupportFaceColumns
                : SupportMaterializationColumns;

        public bool[]? SupportChunkFallbackColumns { get; set; }

        public List<(int X, int Y, int Z)>? GravityPositions { get; set; }

        public MaterializationJob? SupportChunkJob { get; set; }

        public bool[]? SupportChunkTopOccupancy { get; set; }

        public VolumeStructureField.ChunkCursor? VolumeStructureCursor { get; set; }

        public Chunk Chunk => _chunk ??
            throw new InvalidOperationException(
                "Chunk materialization has not started.");

        public int OriginX { get; private set; }

        public int OriginY { get; private set; }

        public int OriginZ { get; private set; }

        private static int CountColumns(bool[]? columns)
        {
            if (columns is null)
                return 0;

            var count = 0;
            foreach (var included in columns)
                if (included)
                    count++;
            return count;
        }

        private static bool[] ExpandCardinalSupportColumns(bool[] requestedColumns)
        {
            if (requestedColumns.Length != Chunk.Area)
                throw new ArgumentException(
                    "Support column mask must match chunk area.",
                    nameof(requestedColumns));

            var expanded = (bool[])requestedColumns.Clone();
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                if (!requestedColumns[z * Chunk.Size + x])
                    continue;

                if (x > 0)
                    expanded[z * Chunk.Size + x - 1] = true;
                if (x + 1 < Chunk.Size)
                    expanded[z * Chunk.Size + x + 1] = true;
                if (z > 0)
                    expanded[(z - 1) * Chunk.Size + x] = true;
                if (z + 1 < Chunk.Size)
                    expanded[(z + 1) * Chunk.Size + x] = true;
            }

            return expanded;
        }

        public void InitializeChunk()
        {
            if (_chunk is not null)
                return;

            _chunk = new Chunk();
            (OriginX, OriginY, OriginZ) = VoxelCoordinates.ChunkOrigin(Coord);
            // Allocation and palette setup occur on the materialization worker.
            if (_owner._voidSpawnPlatform is null)
            {
                InitialBlocks = _chunk.BeginInitialBlockWrites(
                    checked(_owner._blocks.AuthoredCount + 1));
            }
        }

        public SurfaceTerrainColumn? Column { get; set; }

        public TerrainDensityVolume? DensityVolume { get; set; }

        public int TopExclusive { get; set; }

        public int FirstTerrainWorldY { get; set; }

        public int LastTerrainWorldY { get; set; }

        public int NextTerrainLocalY { get; set; }

        public int NextTerrainLocalZ { get; set; }

        public DirectionalFaceOccupancyMasks? DirectionalFaceOccupancyMasks { get; set; }

        public double DirectionalFacePalettePreparationMilliseconds { get; set; }

        public DirectionalFaceOccupancyPreparationProfile
            DirectionalFacePalettePreparationProfile { get; set; }

        public double DirectionalFaceBoundaryColumnBoundsMilliseconds =>
            DirectionalFacePalettePreparationProfile.BoundaryColumnBoundsMilliseconds;

        public double DirectionalFaceCaveSamplerBuildMilliseconds =>
            DirectionalFacePalettePreparationProfile.CaveSamplerBuildMilliseconds;

        public double DirectionalFaceBoundaryColumnEvaluationMilliseconds =>
            DirectionalFacePalettePreparationProfile.BoundaryColumnEvaluationMilliseconds;

        public double DirectionalFaceSurfaceBiomeSamplingMilliseconds =>
            DirectionalFacePalettePreparationProfile.SurfaceBiomeSamplingMilliseconds;

        public double DirectionalFaceBaseHeightSamplingMilliseconds =>
            DirectionalFacePalettePreparationProfile.BaseHeightSamplingMilliseconds;

        public double DirectionalFaceVolumeBiomeSamplingMilliseconds =>
            DirectionalFacePalettePreparationProfile.VolumeBiomeSamplingMilliseconds;

        public int DirectionalFacePaletteDensityProbes { get; set; }

        public int DirectionalFacePaletteBoundaryDensityProbes { get; set; }

        public ColumnMaterializationCache[]? ColumnMaterializationCaches { get; set; }

        public List<PendingGeneratedObject>? PendingObjects { get; set; }

        public Chunk.InitialBlockWriter? InitialBlocks { get; set; }

        public ChunkMaterializationStage Stage { get; internal set; }

        public bool IsComplete => Stage == ChunkMaterializationStage.Complete;

        public Chunk? Result { get; internal set; }

        public ChunkMaterializationStorageDiagnostics StorageDiagnostics
        {
            get
            {
#if DEBUG
                return new ChunkMaterializationStorageDiagnostics(
                    _storageDiagnosticsAvailable,
                    new PaletteStorageGrowthDelta(
                        _terrainLayerBlockPaletteGrowthBytes,
                        _terrainLayerBlockPaletteGrowthEvents),
                    new PaletteStorageGrowthDelta(
                        _terrainLayerDirectionalPaletteGrowthBytes,
                        _terrainLayerDirectionalPaletteGrowthEvents),
                    new PaletteStorageGrowthDelta(
                        _directionalFaceBlockPaletteGrowthBytes,
                        _directionalFaceBlockPaletteGrowthEvents),
                    new PaletteStorageGrowthDelta(
                        _directionalFaceMetadataPaletteGrowthBytes,
                        _directionalFaceMetadataPaletteGrowthEvents),
                    new WorldGenerationEntropy.ValueNoise3DLatticeCacheMetrics(
                        _terrainLayerGeologyNoiseCacheCapacity,
                        _terrainLayerGeologyNoiseCacheHits,
                        _terrainLayerGeologyNoiseCacheMisses,
                        _terrainLayerGeologyNoiseCacheCollisions));
#else
                return default;
#endif
            }
        }

        private DirectionalFacePaletteMetrics? _directionalFacePaletteMetrics;

        public DirectionalFacePaletteMetrics? DirectionalFacePaletteMetrics
        {
            get => SupportChunkJob?.DirectionalFacePaletteMetrics ??
                _directionalFacePaletteMetrics;
            set => _directionalFacePaletteMetrics = value;
        }

        public void Advance()
        {
#if DEBUG
            var stage = Stage;
            if (stage is ChunkMaterializationStage.TerrainLayer or
                ChunkMaterializationStage.DirectionalFacePalettes)
            {
                if (_chunk is not { } chunk)
                {
                    _storageDiagnosticsAvailable = false;
                    _owner.Advance(this);
                    return;
                }

                var blockBefore = chunk.CellStorageCapacityMetrics;
                var metadataBefore =
                    chunk.DirectionalFaceStorageCapacityMetrics ?? default;
                var noiseCacheBefore = stage == ChunkMaterializationStage.TerrainLayer
                    ? _geologyNoiseCache?.Metrics ?? default
                    : default;
                _owner.Advance(this);
                var blockAfter = chunk.CellStorageCapacityMetrics;
                var metadataAfter =
                    chunk.DirectionalFaceStorageCapacityMetrics ?? default;

                _storageDiagnosticsAvailable &=
                    blockBefore.GrowthDiagnosticsAvailable &&
                    blockAfter.GrowthDiagnosticsAvailable;

                var blockDelta = GrowthDelta(blockBefore, blockAfter);
                var metadataDelta = GrowthDelta(metadataBefore, metadataAfter);
                if (stage == ChunkMaterializationStage.TerrainLayer)
                {
                    var noiseCacheAfter = _geologyNoiseCache?.Metrics ?? default;
                    _terrainLayerGeologyNoiseCacheCapacity = noiseCacheAfter.Capacity;
                    _terrainLayerGeologyNoiseCacheHits +=
                        noiseCacheAfter.Hits - noiseCacheBefore.Hits;
                    _terrainLayerGeologyNoiseCacheMisses +=
                        noiseCacheAfter.Misses - noiseCacheBefore.Misses;
                    _terrainLayerGeologyNoiseCacheCollisions +=
                        noiseCacheAfter.Collisions - noiseCacheBefore.Collisions;
                    AddGrowth(
                        ref _terrainLayerBlockPaletteGrowthBytes,
                        ref _terrainLayerBlockPaletteGrowthEvents,
                        blockDelta);
                    AddGrowth(
                        ref _terrainLayerDirectionalPaletteGrowthBytes,
                        ref _terrainLayerDirectionalPaletteGrowthEvents,
                        metadataDelta);
                }
                else
                {
                    AddGrowth(
                        ref _directionalFaceBlockPaletteGrowthBytes,
                        ref _directionalFaceBlockPaletteGrowthEvents,
                        blockDelta);
                    AddGrowth(
                        ref _directionalFaceMetadataPaletteGrowthBytes,
                        ref _directionalFaceMetadataPaletteGrowthEvents,
                        metadataDelta);
                }
                return;
            }
#endif
            _owner.Advance(this);
        }

#if DEBUG
        private static PaletteStorageGrowthDelta GrowthDelta(
            PaletteStorageCapacityMetrics before,
            PaletteStorageCapacityMetrics after) =>
            new(
                after.PaletteGrowthArrayBytes - before.PaletteGrowthArrayBytes +
                after.UsageGrowthArrayBytes - before.UsageGrowthArrayBytes +
                after.LookupBucketGrowthArrayBytes - before.LookupBucketGrowthArrayBytes +
                after.LookupEntryGrowthArrayBytes - before.LookupEntryGrowthArrayBytes +
                after.FreeSlotGrowthArrayBytes - before.FreeSlotGrowthArrayBytes,
                after.PaletteGrowthEvents - before.PaletteGrowthEvents +
                after.UsageGrowthEvents - before.UsageGrowthEvents +
                after.LookupGrowthEvents - before.LookupGrowthEvents +
                after.FreeSlotGrowthEvents - before.FreeSlotGrowthEvents);

        private static void AddGrowth(
            ref long bytes,
            ref int events,
            PaletteStorageGrowthDelta delta)
        {
            bytes += delta.ArrayPayloadBytes;
            events += delta.GrowthEvents;
        }
#endif

        public void Dispose()
        {
            SupportChunkJob?.Dispose();
            SupportChunkJob = null;
            SupportChunkTopOccupancy = null;
            VolumeStructureCursor = null;
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
        int topExclusive,
        bool[]? columnMask)
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
        bool IncludesColumn(int x, int z) =>
            columnMask is null || columnMask[z * Chunk.Size + x];

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
                {
                    if (IncludesColumn(x, z))
                        baseOccupied.Add(StructureCellKey(x, y, z));
                });
            chunk.VisitFluidCells(
                (x, y, z, _) =>
                {
                    if (IncludesColumn(x, z))
                        baseOccupied.Add(StructureCellKey(x, y, z));
                });
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
                        out var localZ) ||
                    !IncludesColumn(localX, localZ))
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
                        out var localZ) ||
                    !IncludesColumn(localX, localZ))
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
                        out var localZ) ||
                    !IncludesColumn(localX, localZ))
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
            out preparationProfile,
            densityVolume);

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
        TerrainDensityVolume? sourceDensityVolume,
        ushort[] boundaryMasks,
        Span<int> columnIndices,
        Span<ushort> requestedYMasks,
        Span<ushort> solidMasks)
    {
        var cursor = startLocalX;
        var hasSourceDensityMasks =
            sourceDensityVolume is not null &&
            sourceDensityVolume.Width == Chunk.Size &&
            sourceDensityVolume.Height == Chunk.Size &&
            sourceDensityVolume.Depth == Chunk.Size;
        const int cumulativeMaskStride = Chunk.Size + 1;
        Span<ushort> cumulativeYMasksByLocalZ =
            stackalloc ushort[Chunk.Size * cumulativeMaskStride];
        if (hasSourceDensityMasks)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                var offset = z * cumulativeMaskStride;
                if (startLocalX < 0)
                {
                    cumulativeYMasksByLocalZ[offset] = 0;
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        cumulativeYMasksByLocalZ[offset + x + 1] =
                            (ushort)(
                                cumulativeYMasksByLocalZ[offset + x] |
                                sourceDensityVolume!.SolidColumnMaskAtUnchecked(x, z));
                    }
                }
                else
                {
                    cumulativeYMasksByLocalZ[offset + Chunk.Size] = 0;
                    for (var x = Chunk.Size - 1; x >= 0; x--)
                    {
                        cumulativeYMasksByLocalZ[offset + x] =
                            (ushort)(
                                cumulativeYMasksByLocalZ[offset + x + 1] |
                                sourceDensityVolume!.SolidColumnMaskAtUnchecked(x, z));
                    }
                }
            }
        }
        var densityProbes = 0;
        var batchCount = 0;
        var columnCount = 0;
        var columnBoundsMilliseconds = 0d;
        var caveSamplerBuildMilliseconds = 0d;
        var columnEvaluationMilliseconds = 0d;
        var densityTerrainColumnMilliseconds = 0d;
        var occupancyCacheAccessMilliseconds = 0d;
        var surfaceBiomeSamplingMilliseconds = 0d;
        var baseHeightSamplingMilliseconds = 0d;
        var volumeBiomeSamplingMilliseconds = 0d;

        while (cursor < endLocalX)
        {
            var firstWorldX = checked(originX + cursor);
            var first = VoxelCoordinates.FromWorld(
                firstWorldX, originY, originZ);
            var firstLocalX = first.Local.X;
            var sliceEnd = Math.Min(
                endLocalX,
                checked(cursor + Chunk.Size - firstLocalX));

            var count = checked((sliceEnd - cursor) * Chunk.Size);
            if (count > columnIndices.Length ||
                count > requestedYMasks.Length ||
                count > solidMasks.Length)
                throw new InvalidOperationException(
                    "A horizontal X boundary batch exceeded one chunk's column capacity.");

            var writeIndex = 0;
            for (var localX = cursor; localX < sliceEnd; localX++)
            {
                var neighborLocalX = firstLocalX + localX - cursor;
                var sourceBoundaryIndex = startLocalX < 0
                    ? Math.Clamp(maximumDepth + localX + 1, 0, Chunk.Size)
                    : Math.Clamp(localX - maximumDepth, 0, Chunk.Size);
                for (var z = 0; z < Chunk.Size; z++)
                {
                    columnIndices[writeIndex] =
                        z * Chunk.Size + neighborLocalX;
                    requestedYMasks[writeIndex] = hasSourceDensityMasks
                        ? cumulativeYMasksByLocalZ[
                            z * cumulativeMaskStride + sourceBoundaryIndex]
                        : ushort.MaxValue;
                    writeIndex++;
                }
            }

            _columns.ReadDensityColumnMasksWithSamplingMetrics(
                first.Chunk.X,
                originY,
                first.Chunk.Z,
                columnIndices[..count],
                requestedYMasks[..count],
                solidMasks[..count],
                out var probes,
                out var boundsMilliseconds,
                out var samplerBuildMilliseconds,
                out var evaluationMilliseconds,
                out var terrainColumnMilliseconds,
                out var occupancyCacheMilliseconds,
                out var surfaceSamplingMilliseconds,
                out var baseHeightMilliseconds,
                out var volumeSamplingMilliseconds);
            surfaceBiomeSamplingMilliseconds += surfaceSamplingMilliseconds;
            baseHeightSamplingMilliseconds += baseHeightMilliseconds;
            volumeBiomeSamplingMilliseconds += volumeSamplingMilliseconds;
            densityProbes = checked(densityProbes + probes);
            columnBoundsMilliseconds += boundsMilliseconds;
            caveSamplerBuildMilliseconds += samplerBuildMilliseconds;
            columnEvaluationMilliseconds += evaluationMilliseconds;
            densityTerrainColumnMilliseconds += terrainColumnMilliseconds;
            occupancyCacheAccessMilliseconds += occupancyCacheMilliseconds;

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
            densityProbes, batchCount, columnCount,
            columnBoundsMilliseconds,
            caveSamplerBuildMilliseconds,
            columnEvaluationMilliseconds,
            densityTerrainColumnMilliseconds,
            occupancyCacheAccessMilliseconds,
            surfaceBiomeSamplingMilliseconds,
            baseHeightSamplingMilliseconds,
            volumeBiomeSamplingMilliseconds);
    }

    private BoundaryMaskReadMetrics ReadDirectionalFaceZBoundaryMasks(
        int startLocalZ,
        int endLocalZ,
        int maximumDepth,
        int originX,
        int originY,
        int originZ,
        int regionSize,
        TerrainDensityVolume? sourceDensityVolume,
        ushort[] boundaryMasks,
        Span<int> columnIndices,
        Span<ushort> requestedYMasks,
        Span<ushort> solidMasks)
    {
        var cursor = startLocalZ;
        var hasSourceDensityMasks =
            sourceDensityVolume is not null &&
            sourceDensityVolume.Width == Chunk.Size &&
            sourceDensityVolume.Height == Chunk.Size &&
            sourceDensityVolume.Depth == Chunk.Size;
        const int cumulativeMaskStride = Chunk.Size + 1;
        Span<ushort> cumulativeYMasksByLocalX =
            stackalloc ushort[Chunk.Size * cumulativeMaskStride];
        if (hasSourceDensityMasks)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                var offset = x * cumulativeMaskStride;
                if (startLocalZ < 0)
                {
                    cumulativeYMasksByLocalX[offset] = 0;
                    for (var z = 0; z < Chunk.Size; z++)
                    {
                        cumulativeYMasksByLocalX[offset + z + 1] =
                            (ushort)(
                                cumulativeYMasksByLocalX[offset + z] |
                                sourceDensityVolume!.SolidColumnMaskAtUnchecked(x, z));
                    }
                }
                else
                {
                    cumulativeYMasksByLocalX[offset + Chunk.Size] = 0;
                    for (var z = Chunk.Size - 1; z >= 0; z--)
                    {
                        cumulativeYMasksByLocalX[offset + z] =
                            (ushort)(
                                cumulativeYMasksByLocalX[offset + z + 1] |
                                sourceDensityVolume!.SolidColumnMaskAtUnchecked(x, z));
                    }
                }
            }
        }
        var densityProbes = 0;
        var batchCount = 0;
        var columnCount = 0;
        var columnBoundsMilliseconds = 0d;
        var caveSamplerBuildMilliseconds = 0d;
        var columnEvaluationMilliseconds = 0d;
        var densityTerrainColumnMilliseconds = 0d;
        var occupancyCacheAccessMilliseconds = 0d;
        var surfaceBiomeSamplingMilliseconds = 0d;
        var baseHeightSamplingMilliseconds = 0d;
        var volumeBiomeSamplingMilliseconds = 0d;

        while (cursor < endLocalZ)
        {
            var firstWorldZ = checked(originZ + cursor);
            var first = VoxelCoordinates.FromWorld(
                originX, originY, firstWorldZ);
            var firstLocalZ = first.Local.Z;
            var sliceEnd = Math.Min(
                endLocalZ,
                checked(cursor + Chunk.Size - firstLocalZ));

            var count = checked((sliceEnd - cursor) * Chunk.Size);
            if (count > columnIndices.Length ||
                count > requestedYMasks.Length ||
                count > solidMasks.Length)
                throw new InvalidOperationException(
                    "A horizontal Z boundary batch exceeded one chunk's column capacity.");

            var writeIndex = 0;
            // Keep local X fixed while traversing the Z strip. The cave-noise
            // grid sampler caches its X-interpolated lattice planes, so this
            // column-major order reuses them across every requested Z column.
            // The batch is already split at chunk boundaries, so neighbor Z is
            // contiguous and can be derived without another world conversion.
            for (var x = 0; x < Chunk.Size; x++)
            for (var localZ = cursor; localZ < sliceEnd; localZ++)
            {
                var neighborLocalZ = firstLocalZ + localZ - cursor;
                var sourceBoundaryIndex = startLocalZ < 0
                    ? Math.Clamp(maximumDepth + localZ + 1, 0, Chunk.Size)
                    : Math.Clamp(localZ - maximumDepth, 0, Chunk.Size);
                columnIndices[writeIndex] =
                    neighborLocalZ * Chunk.Size + x;
                requestedYMasks[writeIndex] = hasSourceDensityMasks
                    ? cumulativeYMasksByLocalX[
                        x * cumulativeMaskStride + sourceBoundaryIndex]
                    : ushort.MaxValue;
                writeIndex++;
            }

            _columns.ReadDensityColumnMasksWithSamplingMetrics(
                first.Chunk.X,
                originY,
                first.Chunk.Z,
                columnIndices[..count],
                requestedYMasks[..count],
                solidMasks[..count],
                out var probes,
                out var boundsMilliseconds,
                out var samplerBuildMilliseconds,
                out var evaluationMilliseconds,
                out var terrainColumnMilliseconds,
                out var occupancyCacheMilliseconds,
                out var surfaceSamplingMilliseconds,
                out var baseHeightMilliseconds,
                out var volumeSamplingMilliseconds);
            surfaceBiomeSamplingMilliseconds += surfaceSamplingMilliseconds;
            baseHeightSamplingMilliseconds += baseHeightMilliseconds;
            volumeBiomeSamplingMilliseconds += volumeSamplingMilliseconds;
            densityProbes = checked(densityProbes + probes);
            columnBoundsMilliseconds += boundsMilliseconds;
            caveSamplerBuildMilliseconds += samplerBuildMilliseconds;
            columnEvaluationMilliseconds += evaluationMilliseconds;
            densityTerrainColumnMilliseconds += terrainColumnMilliseconds;
            occupancyCacheAccessMilliseconds += occupancyCacheMilliseconds;

            writeIndex = 0;
            for (var x = 0; x < Chunk.Size; x++)
            for (var localZ = cursor; localZ < sliceEnd; localZ++)
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
            densityProbes, batchCount, columnCount,
            columnBoundsMilliseconds,
            caveSamplerBuildMilliseconds,
            columnEvaluationMilliseconds,
            densityTerrainColumnMilliseconds,
            occupancyCacheAccessMilliseconds,
            surfaceBiomeSamplingMilliseconds,
            baseHeightSamplingMilliseconds,
            volumeBiomeSamplingMilliseconds);
    }

    private DirectionalFaceOccupancyMasks BuildDirectionalFaceOccupancyMasks(
        Func<int, int, int, bool> isSolid,
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        out int densityProbes,
        out int boundaryDensityProbes,
        out DirectionalFaceOccupancyPreparationProfile preparationProfile,
        TerrainDensityVolume? densityVolume = null)
    {
        var buildJob = new DirectionalFaceOccupancyBuildJob(
            this,
            isSolid,
            column,
            originX,
            originY,
            originZ,
            densityVolume);
        while (!buildJob.IsComplete)
            buildJob.Advance();

        densityProbes = buildJob.DensityProbes;
        boundaryDensityProbes = buildJob.BoundaryDensityProbes;
        preparationProfile = buildJob.PreparationProfile;
        return buildJob.Result;
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
        Func<int, int, DirectionalPaletteColumnData> columnAt,
        VoxelWorld? supportWorld = null,
        Action<DirectionalFacePaletteReapplicationDiagnostic>? diagnostic = null)
    {
        chunk.RestoreDirectionalFaceBaseBlockAtLinearIndex(index);
        var data = chunk.DirectionalFaceDataAtLinearIndex(index);
        var restoredBlock = chunk.GetCellAtLinearIndexUnchecked(index).Block;
        var position = new WorldVoxelCoord(worldX, worldY, worldZ);
        if (!data.IsTerrainVoxel ||
            worldY <= 0 ||
            (_floorY is { } floorY && worldY <= floorY) ||
            (_roofY is { } roofY && worldY >= roofY))
        {
            diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                position, restoredBlock, data.IsTerrainVoxel, data.HasCandidates,
                null, null, null, null, null, null, null, null, false));
            return false;
        }
        if (!data.HasCandidates)
        {
            diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                position, restoredBlock, data.IsTerrainVoxel, false,
                null, null, null, null, null, null, null, null, false));
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
        {
            diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                position, restoredBlock, data.IsTerrainVoxel, data.HasCandidates,
                solidOwner.Biome.Primary, solidOwner.Kind,
                null, null, null, null, null, null, false));
            return false;
        }

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
            {
                diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                    position, restoredBlock, data.IsTerrainVoxel, data.HasCandidates,
                    solidOwner.Biome.Primary, solidOwner.Kind,
                    null, null, null, null, null, null, false));
                return false;
            }

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
            var emptyNeighbor = new WorldVoxelCoord(
                checked(worldX + dx), neighborY, checked(worldZ + dz));
            if (supportWorld is not null &&
                _gravityBlocks.Contains(replacement) &&
                !HasStableGravitySupportBelow(supportWorld, position))
            {
                // Arrival-time reconciliation runs after GravitySupport has
                // validated the generated chunk. Do not introduce a new
                // unsupported gravity block after that invariant pass.
                // The restored base material remains the safe representation.
                diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                    position, restoredBlock, data.IsTerrainVoxel, data.HasCandidates,
                    solidOwner.Biome.Primary, solidOwner.Kind,
                    owner.Biome.Primary, owner.Kind, face, depth,
                    emptyNeighbor, replacement, false));
                return false;
            }

            var applied = chunk.SetDirectionalFacePaletteBlockAtLinearIndex(
                index, replacement);
            diagnostic?.Invoke(new DirectionalFacePaletteReapplicationDiagnostic(
                position, restoredBlock, data.IsTerrainVoxel, data.HasCandidates,
                solidOwner.Biome.Primary, solidOwner.Kind,
                owner.Biome.Primary, owner.Kind, face, depth,
                emptyNeighbor, replacement, applied));
            return applied;
        }
    }

    private bool HasStableGravitySupportBelow(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        var support = position + (0, -1, 0);
        while (support.Y >= 0)
        {
            if (!world.TryGetSavedCell(support, out var cell) || cell.IsEmpty)
                return false;
            if (!_gravityBlocks.Contains(cell.Block))
                return true;

            // A lower gravity block is not terminal support. Follow the full
            // stack, including archived chunks, until it reaches stable rock.
            support += (0, -1, 0);
        }

        // The world floor is terminal support.
        return true;
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
                job.DirectionalFacePalettePreparationProfile.HorizontalBoundaryColumnCount,
            DensityTerrainColumnMilliseconds:
                job.DirectionalFacePalettePreparationProfile.DensityTerrainColumnMilliseconds,
            OccupancyCacheAccessMilliseconds:
                job.DirectionalFacePalettePreparationProfile.OccupancyCacheAccessMilliseconds,
            SurfaceBiomeSamplingMilliseconds:
                job.DirectionalFacePalettePreparationProfile.SurfaceBiomeSamplingMilliseconds,
            BaseHeightSamplingMilliseconds:
                job.DirectionalFacePalettePreparationProfile.BaseHeightSamplingMilliseconds,
            VolumeBiomeSamplingMilliseconds:
                job.DirectionalFacePalettePreparationProfile.VolumeBiomeSamplingMilliseconds);
    }

    internal IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrival(
        VoxelWorld world, ChunkCoord arriving) =>
        ReconcileChunkArrivalCore(world, arriving, diagnostic: null, out _);

    internal IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrivalWithDiagnostics(
        VoxelWorld world,
        ChunkCoord arriving,
        Action<DirectionalFacePaletteReapplicationDiagnostic> diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return ReconcileChunkArrivalCore(world, arriving, diagnostic, out _);
    }

    internal DirectionalFacePaletteReconciliationSnapshot
        CaptureChunkArrivalSnapshot(VoxelWorld world, ChunkCoord arriving)
    {
        ArgumentNullException.ThrowIfNull(world);
        var maximumDepth = checked((int)_materials.MaximumDirectionalPaintDepth);
        var maximumChunkOffset = (maximumDepth + Chunk.Size - 1) / Chunk.Size;
        var sourceChunks = new HashSet<ChunkCoord>();

        // Every source ray and every sampled target lies on one of the three
        // axis-aligned chunk lines crossing the arriving chunk.
        for (var axis = 0; axis < 3; axis++)
        for (var offset = -maximumChunkOffset; offset <= maximumChunkOffset; offset++)
        {
            var coord = OffsetChunk(arriving, axis, offset);
            if (coord.Y >= 0)
                sourceChunks.Add(coord);
        }

        var dependencies = new HashSet<ChunkCoord>(sourceChunks);
        // Gravity-palette validation follows downward stacks until a stable
        // support, air gap, or missing chunk. Capture each affected column down
        // to its first missing chunk so a newly arriving support invalidates
        // this worker instead of allowing a stale gravity decision to publish.
        foreach (var column in sourceChunks
                     .GroupBy(coord => new ChunkColumnCoord(coord.X, coord.Z)))
        {
            var minimumSourceY = column.Min(coord => coord.Y);
            for (var y = minimumSourceY - 1; y >= 0; y--)
            {
                var supportCoord = new ChunkCoord(column.Key.X, y, column.Key.Z);
                dependencies.Add(supportCoord);
                if (!world.TryGetSavedChunk(supportCoord, out _))
                    break;
            }
        }

        var captured = world.CaptureDirectionalFaceWorkerSnapshot(dependencies);
        return new DirectionalFacePaletteReconciliationSnapshot(
            arriving, captured.World, captured.Dependencies);
    }

    internal DirectionalFacePaletteReconciliationResult
        ReconcileChunkArrival(DirectionalFacePaletteReconciliationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var visualChanges = ReconcileChunkArrivalCore(
            snapshot.WorkerWorld, snapshot.Arriving, diagnostic: null,
            out var changedChunks);
        return new DirectionalFacePaletteReconciliationResult(
            snapshot.Arriving,
            snapshot.WorkerWorld,
            snapshot.Dependencies,
            changedChunks,
            visualChanges);
    }

    internal bool TryApplyChunkArrivalReconciliation(
        VoxelWorld world,
        DirectionalFacePaletteReconciliationResult result,
        out IReadOnlyList<WorldVoxelCoord> visualChanges)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(result);
        visualChanges = Array.Empty<WorldVoxelCoord>();
        if (!result.Dependencies.IsCurrent(world))
            return false;

        // Preflight every changed chunk before transferring any of them, so a
        // rejected result can never partially publish its derived state.
        foreach (var coord in result.ChangedChunks)
        {
            if (!world.TryGetSavedChunk(coord, out _) ||
                !result.WorkerWorld.TryGetChunk(coord, out _))
                return false;
        }

        foreach (var coord in result.ChangedChunks)
        {
            if (!world.ReplaceDerivedChunkFromWorker(result.WorkerWorld, coord))
                throw new InvalidOperationException(
                    $"Validated directional-face chunk disappeared during publication: {coord}.");
        }

        visualChanges = result.VisualChanges;
        return true;
    }

    private IReadOnlyList<WorldVoxelCoord> ReconcileChunkArrivalCore(
        VoxelWorld world,
        ChunkCoord arriving,
        Action<DirectionalFacePaletteReapplicationDiagnostic>? diagnostic,
        out IReadOnlyList<ChunkCoord> changedChunks)
    {
        ArgumentNullException.ThrowIfNull(world);
        var maximumDepth = checked((int)_materials.MaximumDirectionalPaintDepth);
        if (maximumDepth <= 0)
        {
            changedChunks = Array.Empty<ChunkCoord>();
            return Array.Empty<WorldVoxelCoord>();
        }

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

        var visualChanges = ApplyDirectionalFaceChanges(
            world, changedSources, dirtyChunks, diagnostic);
        changedChunks = dirtyChunks
            .OrderBy(coord => coord.Y)
            .ThenBy(coord => coord.Z)
            .ThenBy(coord => coord.X)
            .ToArray();
        return visualChanges;
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
        HashSet<ChunkCoord> dirtyChunks,
        Action<DirectionalFacePaletteReapplicationDiagnostic>? diagnostic)
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
                    origin.X + x, origin.Y + y, origin.Z + z, ColumnAt,
                    supportWorld: world,
                    diagnostic: diagnostic);
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

    private bool MaterializeVolumeStructures(MaterializationJob job)
    {
        if (_volumeStructures is not { HasRules: true } formations)
            return true;

        var cursor = job.VolumeStructureCursor ??=
            formations.CreateChunkCursor(job.Coord);
        Span<VolumeStructureVoxel> voxels =
            stackalloc VolumeStructureVoxel[VolumeStructureVoxelsPerAdvance];
        var complete = cursor.Advance(
            voxels,
            VolumeStructureCandidatesPerAdvance,
            out var written);

        var chunk = job.Chunk;
        var columnMask = job.SupportMaterializationColumns;
        for (var index = 0; index < written; index++)
        {
            var voxel = voxels[index];
            var x = voxel.X - job.OriginX;
            var y = voxel.Y - job.OriginY;
            var z = voxel.Z - job.OriginZ;
            if ((uint)x >= Chunk.Size ||
                (uint)y >= Chunk.Size ||
                (uint)z >= Chunk.Size ||
                (columnMask is not null &&
                 !columnMask[z * Chunk.Size + x]) ||
                !chunk.GetCell(x, y, z).IsEmpty ||
                !chunk.GetFluid(x, y, z).IsEmpty)
                continue;

            chunk.SetBlock(x, y, z, voxel.Block);
        }

        if (complete)
            job.VolumeStructureCursor = null;
        return complete;
    }

    private void MaterializeVerticalDecorations(
        Chunk chunk,
        SurfaceTerrainColumn column,
        TerrainDensityVolume densityVolume,
        int originX,
        int originY,
        int originZ,
        ref List<PendingGeneratedObject>? pendingObjects,
        bool includeObjects,
        bool[]? columnMask)
    {
        if (!_decorations.HasVerticalDecorations)
            return;

        var hasCaveBiomeField = _volumeBiomes is { HasBiomes: true };
        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        for (var localX = 0; localX < Chunk.Size; localX++)
        {
            if (columnMask is not null &&
                !columnMask[localZ * Chunk.Size + localX])
                continue;
            var baseY = column.BaseHeightAt(localX, localZ);
            var surfaceBiome = column.BiomeAt(localX, localZ);
            var worldX = originX + localX;
            var worldZ = originZ + localZ;
            var volumePlacement = densityVolume.VolumePlacementAt(localX, localZ);
            var hasVolumeDecorations = volumePlacement is not null &&
                _decorations.HasVerticalDecorationsFor(volumePlacement.Primary);
            if (!hasCaveBiomeField && !hasVolumeDecorations)
                continue;

            // Cave ownership is resolved only after an actual supported void
            // face is found. Most columns never need a cave-biome lookup.
            BiomeSample? underground = null;
            var undergroundResolved = false;
            var hasCaveDecorations = false;
            BiomeSurfaceMaterialColumn? volumeMaterials = null;

            // Include adjacent vertical seam targets: an Object is stored on
            // its host chunk, even when the visible sprite crosses a seam.
            for (var localY = -1; localY <= Chunk.Size; localY++)
            {
                var worldY = originY + localY;
                if (worldY <= 0 ||
                    (_floorY is { } floorY && worldY <= floorY) ||
                    (_roofY is { } roofY && worldY >= roofY) ||
                    (worldY > baseY && !hasVolumeDecorations))
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
                    if (!hasCaveBiomeField ||
                        !_terrain.IsCaveVoidAt(
                            baseY, new WorldVoxelCoord(worldX, worldY, worldZ)))
                        continue;
                    if (!undergroundResolved)
                    {
                        underground = column.CaveBiomeAt(localX, localZ);
                        hasCaveDecorations = underground is not null &&
                            _decorations.HasVerticalDecorationsFor(underground.Primary);
                        undergroundResolved = true;
                    }
                    if (!hasCaveDecorations)
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
        int originZ,
        bool[]? columnMask)
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
            if (columnMask is not null &&
                !columnMask[z * Chunk.Size + x])
                continue;
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
        int originZ,
        bool[]? columnMask)
    {
        if (_caveSpikes is not { HasRules: true } spikes ||
            _volumeBiomes is not { HasBiomes: true } volumeBiomes)
            return;

        var maximumHeight = spikes.MaximumHeight;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            if (columnMask is not null &&
                !columnMask[z * Chunk.Size + x])
                continue;
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

            // The density snapshot stores the complete vertical occupancy
            // of this column as one bit mask. Bits below the base surface
            // identify whether this column contains any cave void; adjacent
            // solid bits identify the only interior Y values that can anchor
            // a spike. Chunk-edge anchors remain eligible because their
            // supporting voxel may live in the neighboring chunk.
            var solidColumnMask =
                densityVolume.SolidColumnMaskAtUnchecked(x, z);
            var belowSurfaceHeight = (int)Math.Clamp(
                (long)baseY - originY, 0L, Chunk.Size);
            var belowSurfaceMask = (1u << belowSurfaceHeight) - 1u;
            if ((~(uint)solidColumnMask & belowSurfaceMask) == 0u)
                continue;

            var supportedInteriorMask =
                ((uint)solidColumnMask << 1) |
                ((uint)solidColumnMask >> 1);
            var candidateAnchorMask = (ushort)(
                ~(uint)solidColumnMask &
                (supportedInteriorMask | 0x8001u));

            var worldX = originX + x;
            var worldZ = originZ + z;
            var surfaceBiome = column.BiomeAt(x, z);
            var spikeColumn = spikes.PrepareColumn(biome.Primary, surfaceBiome);
            if (!spikeColumn.HasRules)
                continue;

            var volumeBiome = densityVolume.VolumePlacementAt(x, z);
            for (var anchorY = first; anchorY <= last; anchorY++)
            {
                // An interior cave spike must touch a solid voxel directly
                // above or below. Reject unsupported void anchors before
                // hashing placement chance, spacing or cluster noise.
                // Keep chunk-edge planes for cross-chunk checks, and keep
                // anchors adjacent to the world-height bounds because IsVoid
                // treats those out-of-range probes as non-void support.
                var localAnchorY = anchorY - originY;
                if ((uint)localAnchorY < Chunk.Size &&
                    anchorY > 1 && anchorY < baseY - 1 &&
                    (candidateAnchorMask & (1u << localAnchorY)) == 0)
                    continue;
                var anchorKnownVoid =
                    (uint)localAnchorY < densityVolume.Height &&
                    (solidColumnMask & (1u << localAnchorY)) == 0;

                for (var direction = 0; direction < 2; direction++)
                {
                    var down = direction == 1;
                    var supportY = anchorY + (down ? 1 : -1);
                    var localSupportY = supportY - originY;
                    if (supportY > 0 && supportY < baseY &&
                        (uint)localSupportY < densityVolume.Height &&
                        (solidColumnMask & (1u << localSupportY)) == 0)
                        continue;

                    if (!spikeColumn.TrySample(
                            volumeBiome, baseY,
                            worldX, anchorY, worldZ, down,
                            densityVolume, originY, x, z,
                            solidColumnMask, anchorKnownVoid,
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        int topExclusive,
        bool[]? columnMask)
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
                if (columnMask is not null && !columnMask[columnIndex])
                    continue;
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
                    if ((columnMask is not null && !columnMask[columnIndex]) ||
                        worldY < firstFluidYByColumn[columnIndex] ||
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
        int topExclusive,
        bool[]? columnMask)
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
            if (columnMask is not null &&
                !columnMask[z * Chunk.Size + x])
                continue;
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
