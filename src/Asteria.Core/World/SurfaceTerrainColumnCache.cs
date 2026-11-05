#if DEBUG
using System.Diagnostics;
#endif

namespace Asteria.Core.World;

/// <summary>
/// Compact local-Y occupancy snapshot for a partial vertical halo slab.
/// </summary>
internal readonly record struct DensityOccupancySlab(
    int Height,
    ushort[] ColumnSolidMasks)
{
    public bool IsSolidAtUnchecked(int localX, int localY, int localZ) =>
        (ColumnSolidMasks[localZ * Chunk.Size + localX] &
         (1u << localY)) != 0;
}

internal readonly record struct DensityTerrainColumnSamplingMetrics(
    double SurfaceBiomeSamplingMilliseconds,
    double BaseHeightSamplingMilliseconds,
    double VolumeBiomeSamplingMilliseconds);

/// <summary>
/// Query-side cache of immutable terrain samples shared by range selection,
/// vertical materialization and directional-face boundary occupancy.
/// Cache entries accumulate deterministic occupancy bits and are bounded;
/// eviction only causes recomputation.
/// </summary>
public sealed class SurfaceTerrainColumnCache
{
    private const int DefaultDensityColumnCapacity = Chunk.Area * 512;

    private readonly SurfaceTerrainField _terrain;
    private readonly BoundedMemoCache<
        (int X, int Z),
        SurfaceTerrainColumn> _columns;
    private readonly BoundedMemoCache<
        (int X, int Z),
        DensityTerrainColumnEntry> _densityTerrainColumns;
    private readonly BoundedMemoCache<
        (int X, int Y, int Z),
        DensityChunkOccupancy> _densityChunks;

    public SurfaceTerrainColumnCache(
        SurfaceTerrainField terrain,
        int capacity = 128,
        int densityColumnCapacity = DefaultDensityColumnCapacity)
    {
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        if (densityColumnCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(densityColumnCapacity));

        _columns = new BoundedMemoCache<
            (int X, int Z),
            SurfaceTerrainColumn>(capacity);
        _densityTerrainColumns = new BoundedMemoCache<
            (int X, int Z),
            DensityTerrainColumnEntry>(capacity);
        _densityChunks = new BoundedMemoCache<
            (int X, int Y, int Z),
            DensityChunkOccupancy>(
                Math.Max(1, densityColumnCapacity / Chunk.Area));
    }

    public int CachedColumnCount => _columns.Count;

    // Preserve the previous column-based diagnostic unit.
    public int CachedDensityColumnCount =>
        checked(_densityChunks.Count * Chunk.Area);

    /// <summary>
    /// Holds the 16-bit solidity and validity masks for an entire vertical
    /// chunk band. A single lock protects all 256 X/Z columns in that band.
    /// </summary>
    private sealed class DensityChunkOccupancy
    {
        private readonly object _gate = new();
        private readonly ushort[] _solidMasks = new ushort[Chunk.Area];
        private readonly ushort[] _sampledMasks = new ushort[Chunk.Area];

        public void Snapshot(ushort[] solidMasks, ushort[] sampledMasks)
        {
            if (solidMasks.Length != Chunk.Area ||
                sampledMasks.Length != Chunk.Area)
                throw new ArgumentException("Density chunk snapshots must contain 256 columns.");

            lock (_gate)
            {
                Array.Copy(_solidMasks, solidMasks, Chunk.Area);
                Array.Copy(_sampledMasks, sampledMasks, Chunk.Area);
            }
        }

        public void ReadColumn(
            int index,
            out ushort solidMask,
            out ushort sampledMask)
        {
            lock (_gate)
            {
                solidMask = _solidMasks[index];
                sampledMask = _sampledMasks[index];
            }
        }

        public void ReadColumns(
            ReadOnlySpan<int> columnIndices,
            Span<ushort> solidMasks,
            Span<ushort> sampledMasks)
        {
            if (solidMasks.Length != columnIndices.Length ||
                sampledMasks.Length != columnIndices.Length)
                throw new ArgumentException(
                    "Density column read buffers must have matching lengths.");

            lock (_gate)
            {
                for (var index = 0; index < columnIndices.Length; index++)
                {
                    var columnIndex = columnIndices[index];
                    if ((uint)columnIndex >= Chunk.Area)
                        throw new ArgumentOutOfRangeException(nameof(columnIndices));

                    solidMasks[index] = _solidMasks[columnIndex];
                    sampledMasks[index] = _sampledMasks[columnIndex];
                }
            }
        }

        public void ReadSlab(
            int verticalOffset,
            int height,
            ushort[] solidMasks,
            ushort[] sampledMasks)
        {
            var mask = HeightMask(height);
            lock (_gate)
            {
                for (var index = 0; index < Chunk.Area; index++)
                {
                    solidMasks[index] =
                        (ushort)((_solidMasks[index] >> verticalOffset) & mask);
                    sampledMasks[index] =
                        (ushort)((_sampledMasks[index] >> verticalOffset) & mask);
                }
            }
        }

        public void MergeFull(TerrainDensityVolume volume)
        {
            lock (_gate)
            {
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var index = z * Chunk.Size + x;
                    var incoming = volume.SolidColumnMaskAtUnchecked(x, z);
                    var overlap = _sampledMasks[index];
                    var disagreement =
                        (ushort)((_solidMasks[index] ^ incoming) & overlap);
                    if (disagreement != 0)
                        ThrowOccupancyMismatch();

                    _solidMasks[index] = incoming;
                    _sampledMasks[index] = ushort.MaxValue;
                }
            }
        }

        public void MergeSlab(
            ushort[] solidMasks,
            int verticalOffset,
            int height)
        {
            if (solidMasks.Length != Chunk.Area)
                throw new ArgumentException("Density slab must contain 256 columns.");

            var localMask = HeightMask(height);
            var bandMask = (ushort)(localMask << verticalOffset);
            lock (_gate)
            {
                for (var index = 0; index < Chunk.Area; index++)
                {
                    var incoming =
                        (ushort)(solidMasks[index] << verticalOffset);
                    var overlap = (ushort)(_sampledMasks[index] & bandMask);
                    var disagreement =
                        (ushort)((_solidMasks[index] ^ incoming) & overlap);
                    if (disagreement != 0)
                        ThrowOccupancyMismatch();

                    _solidMasks[index] = (ushort)(
                        (_solidMasks[index] & (ushort)~bandMask) |
                        (incoming & bandMask));
                    _sampledMasks[index] |= bandMask;
                }
            }
        }

        public void MergeColumn(
            int index,
            ushort solidMask,
            ushort sampledMask)
        {
            lock (_gate)
            {
                var overlap = (ushort)(_sampledMasks[index] & sampledMask);
                var disagreement =
                    (ushort)((_solidMasks[index] ^ solidMask) & overlap);
                if (disagreement != 0)
                    ThrowOccupancyMismatch();

                _solidMasks[index] = (ushort)(
                    (_solidMasks[index] & (ushort)~sampledMask) |
                    (solidMask & sampledMask));
                _sampledMasks[index] |= sampledMask;
            }
        }

        public void MergeColumns(
            ReadOnlySpan<int> columnIndices,
            ReadOnlySpan<ushort> solidMasks,
            ReadOnlySpan<ushort> sampledMasks)
        {
            if (solidMasks.Length != columnIndices.Length ||
                sampledMasks.Length != columnIndices.Length)
                throw new ArgumentException(
                    "Density column merge buffers must have matching lengths.");

            lock (_gate)
            {
                for (var index = 0; index < columnIndices.Length; index++)
                {
                    var columnIndex = columnIndices[index];
                    if ((uint)columnIndex >= Chunk.Area)
                        throw new ArgumentOutOfRangeException(nameof(columnIndices));

                    var incomingSampled = sampledMasks[index];
                    var overlap = (ushort)(
                        _sampledMasks[columnIndex] & incomingSampled);
                    var disagreement = (ushort)(
                        (_solidMasks[columnIndex] ^ solidMasks[index]) & overlap);
                    if (disagreement != 0)
                        ThrowOccupancyMismatch();

                    _solidMasks[columnIndex] = (ushort)(
                        (_solidMasks[columnIndex] & (ushort)~incomingSampled) |
                        (solidMasks[index] & incomingSampled));
                    _sampledMasks[columnIndex] |= incomingSampled;
                }
            }
        }

        private static void ThrowOccupancyMismatch() =>
            throw new InvalidOperationException(
                "Deterministic terrain occupancy disagreed for an already sampled voxel.");
    }

    /// <summary>
    /// Incrementally fills only the X/Z columns requested by horizontal
    /// boundary batches. Sampled entries are immutable after publication.
    /// </summary>
    private sealed class DensityTerrainColumnEntry
    {
        private readonly object _gate = new();
        private readonly BiomeSample?[] _biomes = new BiomeSample?[Chunk.Area];
        private readonly BiomeSample?[] _volumeBiomes = new BiomeSample?[Chunk.Area];
        private readonly int[] _baseHeights = new int[Chunk.Area];
        private readonly bool[] _sampled = new bool[Chunk.Area];
        private readonly ulong[] _volumeSampledMasks = new ulong[Chunk.Area / 64];
        private readonly BiomeField.Sampler _surfaceSampler;
        private BiomeField.Sampler? _volumeSampler;

        public DensityTerrainColumnEntry(SurfaceTerrainField terrain)
        {
            _surfaceSampler = terrain.CreateDensitySurfaceSampler();
        }

        public SurfaceTerrainDensityColumn GetOrSample(
            SurfaceTerrainField terrain,
            int chunkX,
            int chunkZ,
            ReadOnlySpan<int> columnIndices,
            bool includeVolumeBiomes,
            out DensityTerrainColumnSamplingMetrics samplingMetrics)
        {
            var surfaceBiomeSamplingMilliseconds = 0d;
            var baseHeightSamplingMilliseconds = 0d;
            var volumeBiomeSamplingMilliseconds = 0d;
            lock (_gate)
            {
                Span<bool> requested = stackalloc bool[Chunk.Area];
                Span<int> missingSurface = stackalloc int[Chunk.Area];
                Span<int> missingVolume = stackalloc int[Chunk.Area];
                requested.Clear();
                var missingSurfaceCount = 0;
                var missingVolumeCount = 0;

                for (var index = 0; index < columnIndices.Length; index++)
                {
                    var columnIndex = columnIndices[index];
                    if ((uint)columnIndex >= Chunk.Area)
                        throw new ArgumentOutOfRangeException(nameof(columnIndices));
                    if (requested[columnIndex])
                        continue;

                    requested[columnIndex] = true;
                    if (!_sampled[columnIndex])
                        missingSurface[missingSurfaceCount++] = columnIndex;

                    var volumeMask = 1UL << (columnIndex & 63);
                    if (includeVolumeBiomes &&
                        (_volumeSampledMasks[columnIndex >> 6] & volumeMask) == 0)
                    {
                        missingVolume[missingVolumeCount++] = columnIndex;
                    }
                }

                if (missingSurfaceCount > 0)
                {
                    terrain.SampleDensitySurfaceColumns(
                        chunkX,
                        chunkZ,
                        missingSurface[..missingSurfaceCount],
                        _surfaceSampler,
                        _biomes,
                        _baseHeights,
                        out var biomeSamplingMilliseconds,
                        out var baseHeightMilliseconds);
                    surfaceBiomeSamplingMilliseconds += biomeSamplingMilliseconds;
                    baseHeightSamplingMilliseconds += baseHeightMilliseconds;
                    for (var index = 0; index < missingSurfaceCount; index++)
                        _sampled[missingSurface[index]] = true;
                }

                if (missingVolumeCount > 0)
                {
                    _volumeSampler ??= terrain.CreateDensityVolumeSampler();
#if DEBUG
                    var volumeSamplingStarted = Stopwatch.GetTimestamp();
#endif
                    terrain.SampleDensityVolumeColumns(
                        chunkX,
                        chunkZ,
                        missingVolume[..missingVolumeCount],
                        _volumeSampler,
                        _volumeBiomes);
#if DEBUG
                    volumeBiomeSamplingMilliseconds =
                        Stopwatch.GetElapsedTime(volumeSamplingStarted).TotalMilliseconds;
#endif
                    for (var index = 0; index < missingVolumeCount; index++)
                    {
                        var columnIndex = missingVolume[index];
                        _volumeSampledMasks[columnIndex >> 6] |=
                            1UL << (columnIndex & 63);
                    }
                }

                samplingMetrics = new DensityTerrainColumnSamplingMetrics(
                    surfaceBiomeSamplingMilliseconds,
                    baseHeightSamplingMilliseconds,
                    volumeBiomeSamplingMilliseconds);
                return new SurfaceTerrainDensityColumn(
                    _biomes, _volumeBiomes, _baseHeights);
            }
        }
    }

    private static ushort HeightMask(int height) =>
        height == Chunk.Size
            ? ushort.MaxValue
            : (ushort)((1u << height) - 1u);

    private DensityChunkOccupancy GetDensityChunk(
        int chunkX,
        int originY,
        int chunkZ) =>
        _densityChunks.GetOrAdd(
            (chunkX, originY, chunkZ),
            static () => new DensityChunkOccupancy());

    public SurfaceTerrainColumn Get(int chunkX, int chunkZ)
    {
        var key = (X: chunkX, Z: chunkZ);
        if (_columns.TryGet(key, out var cached))
            return cached;

        return _columns.GetOrAdd(
            key,
            () => _terrain.SampleColumn(chunkX, chunkZ));
    }

    private SurfaceTerrainDensityColumn GetDensityTerrainColumn(
        int chunkX,
        int chunkZ,
        ReadOnlySpan<int> columnIndices,
        bool includeVolumeBiomes,
        out DensityTerrainColumnSamplingMetrics samplingMetrics)
    {
        var key = (X: chunkX, Z: chunkZ);
        if (_columns.TryGet(key, out var fullColumn))
        {
            samplingMetrics = default;
            return new SurfaceTerrainDensityColumn(fullColumn);
        }

        var entry = _densityTerrainColumns.GetOrAdd(
            key,
            () => new DensityTerrainColumnEntry(_terrain));
        return entry.GetOrSample(
            _terrain,
            chunkX,
            chunkZ,
            columnIndices,
            includeVolumeBiomes,
            out samplingMetrics);
    }

    /// <summary>
    /// Materializes a full chunk while reusing previously sampled occupancy
    /// bits from its top and bottom neighbors.
    /// </summary>
    internal TerrainDensityVolume SampleDensityVolume(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        var (coord, _) = VoxelCoordinates.FromWorld(originX, originY, originZ);
        var occupancy = GetDensityChunk(coord.X, originY, coord.Z);
        var cachedMasks = new ushort[Chunk.Area];
        var cachedMaskAvailability = new ushort[Chunk.Area];
        occupancy.Snapshot(cachedMasks, cachedMaskAvailability);

        var volume = _terrain.SampleDensityVolume(
            column,
            originX,
            originY,
            originZ,
            Chunk.Size,
            cachedMasks,
            cachedMaskAvailability);
        occupancy.MergeFull(volume);
        return volume;
    }

    /// <summary>
    /// Samples a bounded Y slab in one vertical band. Cached columns are
    /// copied in a single operation; a fully cached slab skips terrain work.
    /// </summary>
    internal DensityOccupancySlab SampleDensitySlab(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        int height,
        out int densityProbes)
    {
        ArgumentNullException.ThrowIfNull(column);
        var verticalOffset = originY % Chunk.Size;
        if (originY < 0 ||
            height is < 1 or > Chunk.Size ||
            verticalOffset + height > Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(height));

        var bandOriginY = originY - verticalOffset;
        var (coord, _) = VoxelCoordinates.FromWorld(
            originX, bandOriginY, originZ);
        var occupancy = GetDensityChunk(coord.X, bandOriginY, coord.Z);
        var solidMasks = new ushort[Chunk.Area];
        var sampledMasks = new ushort[Chunk.Area];
        occupancy.ReadSlab(
            verticalOffset, height, solidMasks, sampledMasks);

        var requestedMask = HeightMask(height);
        var cachedBitCount = 0;
        for (var index = 0; index < Chunk.Area; index++)
            cachedBitCount += System.Numerics.BitOperations.PopCount(
                (uint)sampledMasks[index]);
        densityProbes = checked(Chunk.Area * height - cachedBitCount);

        if (densityProbes != 0)
        {
            var volume = _terrain.SampleDensityVolume(
                column,
                originX,
                originY,
                originZ,
                height,
                solidMasks,
                sampledMasks);
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                solidMasks[z * Chunk.Size + x] =
                    volume.SolidColumnMaskAtUnchecked(x, z);

            occupancy.MergeSlab(solidMasks, verticalOffset, height);
        }

        return new DensityOccupancySlab(height, solidMasks);
    }

    /// <summary>
    /// Reads selected Y bits from a set of columns in one vertical chunk band.
    /// Missing requested bits are completed before the batch is returned, then
    /// published under one merge lock.
    /// </summary>
    internal void ReadDensityColumnMasks(
        int chunkX,
        int originY,
        int chunkZ,
        ReadOnlySpan<int> columnIndices,
        ReadOnlySpan<ushort> requestedMasks,
        Span<ushort> solidMasks,
        out int densityProbes,
        out double columnBoundsMilliseconds,
        out double caveSamplerBuildMilliseconds,
        out double columnEvaluationMilliseconds,
        out double densityTerrainColumnMilliseconds,
        out double occupancyCacheAccessMilliseconds) =>
        ReadDensityColumnMasksWithSamplingMetrics(
            chunkX,
            originY,
            chunkZ,
            columnIndices,
            requestedMasks,
            solidMasks,
            out densityProbes,
            out columnBoundsMilliseconds,
            out caveSamplerBuildMilliseconds,
            out columnEvaluationMilliseconds,
            out densityTerrainColumnMilliseconds,
            out occupancyCacheAccessMilliseconds,
            out _,
            out _,
            out _);

    internal void ReadDensityColumnMasksWithSamplingMetrics(
        int chunkX,
        int originY,
        int chunkZ,
        ReadOnlySpan<int> columnIndices,
        ReadOnlySpan<ushort> requestedMasks,
        Span<ushort> solidMasks,
        out int densityProbes,
        out double columnBoundsMilliseconds,
        out double caveSamplerBuildMilliseconds,
        out double columnEvaluationMilliseconds,
        out double densityTerrainColumnMilliseconds,
        out double occupancyCacheAccessMilliseconds,
        out double surfaceBiomeSamplingMilliseconds,
        out double baseHeightSamplingMilliseconds,
        out double volumeBiomeSamplingMilliseconds)
    {
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));
        if (columnIndices.Length != requestedMasks.Length ||
            columnIndices.Length != solidMasks.Length ||
            columnIndices.Length > Chunk.Area)
            throw new ArgumentException(
                "Density column batch must contain matching buffers of at most 256 columns.");

        densityProbes = 0;
        columnBoundsMilliseconds = 0d;
        caveSamplerBuildMilliseconds = 0d;
        columnEvaluationMilliseconds = 0d;
        densityTerrainColumnMilliseconds = 0d;
        occupancyCacheAccessMilliseconds = 0d;
        surfaceBiomeSamplingMilliseconds = 0d;
        baseHeightSamplingMilliseconds = 0d;
        volumeBiomeSamplingMilliseconds = 0d;
        if (columnIndices.IsEmpty)
            return;

        var hasRequestedBits = false;
        for (var index = 0; index < requestedMasks.Length; index++)
        {
            hasRequestedBits |= requestedMasks[index] != 0;
            if (requestedMasks[index] == 0)
                solidMasks[index] = 0;
        }
        if (!hasRequestedBits)
            return;

        var occupancy = GetDensityChunk(chunkX, originY, chunkZ);
        Span<ushort> sampledMasks = stackalloc ushort[columnIndices.Length];
#if DEBUG
        var occupancyReadStarted = Stopwatch.GetTimestamp();
#endif
        occupancy.ReadColumns(columnIndices, solidMasks, sampledMasks);
        for (var index = 0; index < columnIndices.Length; index++)
        {
            solidMasks[index] &= requestedMasks[index];
            sampledMasks[index] &= requestedMasks[index];
        }
#if DEBUG
        occupancyCacheAccessMilliseconds +=
            Stopwatch.GetElapsedTime(occupancyReadStarted).TotalMilliseconds;
#endif

        Span<int> missingColumnIndices = stackalloc int[columnIndices.Length];
        Span<int> missingInputIndices = stackalloc int[columnIndices.Length];
        Span<ushort> missingRequestedMasks = stackalloc ushort[columnIndices.Length];
        Span<ushort> missingSolidMasks = stackalloc ushort[columnIndices.Length];
        Span<ushort> missingSampledMasks = stackalloc ushort[columnIndices.Length];
        var missingCount = 0;
        for (var index = 0; index < columnIndices.Length; index++)
        {
            var missingMask = (ushort)(
                requestedMasks[index] & (ushort)~sampledMasks[index]);
            if (missingMask == 0)
                continue;

            missingColumnIndices[missingCount] = columnIndices[index];
            missingInputIndices[missingCount] = index;
            missingRequestedMasks[missingCount] = requestedMasks[index];
            missingSolidMasks[missingCount] = solidMasks[index];
            missingSampledMasks[missingCount] = sampledMasks[index];
            missingCount++;
        }

        if (missingCount > 0)
        {
            var requestedColumns = missingColumnIndices[..missingCount];
            var requestedMasksForColumns = missingRequestedMasks[..missingCount];
            var solidMasksForColumns = missingSolidMasks[..missingCount];
            var sampledMasksForColumns = missingSampledMasks[..missingCount];
#if DEBUG
            var densityColumnStarted = Stopwatch.GetTimestamp();
#endif
            var includeVolumeBiomes =
                _terrain.RequiresVolumeBiomeForDensityBand(originY);
            var column = GetDensityTerrainColumn(
                chunkX,
                chunkZ,
                requestedColumns,
                includeVolumeBiomes,
                out var samplingMetrics);
            surfaceBiomeSamplingMilliseconds =
                samplingMetrics.SurfaceBiomeSamplingMilliseconds;
            baseHeightSamplingMilliseconds =
                samplingMetrics.BaseHeightSamplingMilliseconds;
            volumeBiomeSamplingMilliseconds =
                samplingMetrics.VolumeBiomeSamplingMilliseconds;
#if DEBUG
            densityTerrainColumnMilliseconds =
                Stopwatch.GetElapsedTime(densityColumnStarted).TotalMilliseconds;
#endif
            var chunkOriginX = checked(chunkX * Chunk.Size);
            var chunkOriginZ = checked(chunkZ * Chunk.Size);
            _terrain.SampleDensityColumnMasks(
                column,
                chunkOriginX,
                originY,
                chunkOriginZ,
                requestedColumns,
                requestedMasksForColumns,
                solidMasksForColumns,
                sampledMasksForColumns,
                out densityProbes,
                out columnBoundsMilliseconds,
                out caveSamplerBuildMilliseconds,
                out columnEvaluationMilliseconds);
#if DEBUG
            var occupancyMergeStarted = Stopwatch.GetTimestamp();
#endif
            occupancy.MergeColumns(
                requestedColumns,
                solidMasksForColumns,
                sampledMasksForColumns);
#if DEBUG
            occupancyCacheAccessMilliseconds +=
                Stopwatch.GetElapsedTime(occupancyMergeStarted).TotalMilliseconds;
#endif
            for (var index = 0; index < missingCount; index++)
                solidMasks[missingInputIndices[index]] = missingSolidMasks[index];
        }
    }

    /// <summary>
    /// Returns the complete occupancy mask for one world X/Z column,
    /// evaluating only bits not already sampled by adjacent halo slabs.
    /// </summary>
    internal ushort GetDensityColumnMask(
        int worldX,
        int originY,
        int worldZ,
        out bool cacheHit,
        out int densityProbes)
    {
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        var (coord, local) = VoxelCoordinates.FromWorld(
            worldX, originY, worldZ);
        var occupancy = GetDensityChunk(coord.X, originY, coord.Z);
        var index = local.Z * Chunk.Size + local.X;
        occupancy.ReadColumn(index, out var cachedMask, out var sampledMask);
        if (sampledMask == ushort.MaxValue)
        {
            cacheHit = true;
            densityProbes = 0;
            return cachedMask;
        }

        cacheHit = false;
        Span<int> requestedColumn = stackalloc int[1];
        requestedColumn[0] = index;
        var column = GetDensityTerrainColumn(
            coord.X,
            coord.Z,
            requestedColumn,
            _terrain.RequiresVolumeBiomeForDensityBand(originY));
        var mask = _terrain.SampleDensityColumnMask(
            column,
            local.X,
            originY,
            local.Z,
            worldX,
            worldZ,
            cachedMask,
            sampledMask,
            out densityProbes);
        occupancy.MergeColumn(index, mask, ushort.MaxValue);
        occupancy.ReadColumn(index, out var completeMask, out _);
        return completeMask;
    }

    /// <summary>
    /// Samples the 17x17 vertex grid for one chunk from its four immutable
    /// neighboring 16x16 column snapshots. The extra row/column preserves
    /// the same world-space biome values on both sides of a chunk seam.
    /// </summary>
    public BiomeSampleGrid SampleChunkBiomes(int chunkX, int chunkZ)
    {
        var nextX = checked(chunkX + 1);
        var nextZ = checked(chunkZ + 1);
        var (originX, _, originZ) = VoxelCoordinates.ChunkOrigin(
            new ChunkCoord(chunkX, 0, chunkZ));
        var columns = new[,]
        {
            { Get(chunkX, chunkZ), Get(chunkX, nextZ) },
            { Get(nextX, chunkZ), Get(nextX, nextZ) },
        };
        var width = Chunk.Size + 1;
        var samples = new BiomeSample[width * width];

        for (var z = 0; z < width; z++)
        {
            var tileZ = z / Chunk.Size;
            var localZ = z % Chunk.Size;
            for (var x = 0; x < width; x++)
            {
                var tileX = x / Chunk.Size;
                var localX = x % Chunk.Size;
                samples[z * width + x] =
                    columns[tileX, tileZ].BiomeAt(localX, localZ);
            }
        }

        return new BiomeSampleGrid(
            originX,
            originZ,
            width,
            width,
            samples);
    }
}
