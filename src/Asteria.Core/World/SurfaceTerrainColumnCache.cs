namespace Asteria.Core.World;

/// <summary>
/// Query-side cache of immutable terrain samples shared by range selection,
/// vertical materialization and directional-face boundary occupancy.
/// Both caches are bounded and dimension-local; eviction only causes
/// deterministic recomputation.
/// </summary>
public sealed class SurfaceTerrainColumnCache
{
    private const int DefaultDensityColumnCapacity = Chunk.Area * 128;

    private readonly SurfaceTerrainField _terrain;
    private readonly BoundedMemoCache<
        (int X, int Z),
        SurfaceTerrainColumn> _columns;
    private readonly BoundedMemoCache<
        (int X, int Y, int Z),
        DensityColumnOccupancy> _densityColumns;

    public SurfaceTerrainColumnCache(
        SurfaceTerrainField terrain,
        int capacity = 128,
        int densityColumnCapacity = DefaultDensityColumnCapacity)
    {
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _columns = new BoundedMemoCache<
            (int X, int Z),
            SurfaceTerrainColumn>(capacity);
        _densityColumns = new BoundedMemoCache<
            (int X, int Y, int Z),
            DensityColumnOccupancy>(densityColumnCapacity);
    }

    public int CachedColumnCount => _columns.Count;

    public int CachedDensityColumnCount => _densityColumns.Count;

    /// <summary>
    /// Monotonic occupancy knowledge for one vertical X/Z column. Known
    /// values never change; concurrent overlapping samples must agree.
    /// </summary>
    private sealed class DensityColumnOccupancy
    {
        private readonly object _gate = new();
        private ushort _solidMask;
        private ushort _sampledMask;

        public void Read(out ushort solidMask, out ushort sampledMask)
        {
            lock (_gate)
            {
                solidMask = _solidMask;
                sampledMask = _sampledMask;
            }
        }

        public void Merge(ushort solidMask, ushort sampledMask)
        {
            lock (_gate)
            {
                var overlap = (ushort)(_sampledMask & sampledMask);
                var disagreement = (ushort)((_solidMask ^ solidMask) & overlap);
                if (disagreement != 0)
                    throw new InvalidOperationException(
                        "Deterministic terrain occupancy disagreed for an already sampled voxel.");

                _solidMask = (ushort)(
                    (_solidMask & (ushort)~sampledMask) |
                    (solidMask & sampledMask));
                _sampledMask |= sampledMask;
            }
        }
    }

    private DensityColumnOccupancy GetDensityColumn((int X, int Y, int Z) key) =>
        _densityColumns.GetOrAdd(key, static () => new DensityColumnOccupancy());

    private static ushort HeightMask(int height) =>
        height == Chunk.Size ? ushort.MaxValue : (ushort)((1u << height) - 1u);

    private static int CountSetBits(ushort value)
    {
        var count = 0;
        while (value != 0)
        {
            value = (ushort)(value & (value - 1));
            count++;
        }
        return count;
    }

    public SurfaceTerrainColumn Get(int chunkX, int chunkZ)
    {
        var key = (X: chunkX, Z: chunkZ);
        if (_columns.TryGet(key, out var cached))
            return cached;

        return _columns.GetOrAdd(
            key,
            () => _terrain.SampleColumn(chunkX, chunkZ));
    }

    /// <summary>
    /// Materializes a full chunk while reusing any complete or partial
    /// occupancy bits previously sampled by neighboring face halos.
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

        var cachedMasks = new ushort[Chunk.Area];
        var cachedMaskAvailability = new ushort[Chunk.Area];
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var index = x + Chunk.Size * z;
            if (_densityColumns.TryGet((originX + x, originY, originZ + z), out var cached))
                cached.Read(out cachedMasks[index], out cachedMaskAvailability[index]);
        }

        var volume = _terrain.SampleDensityVolume(
            column, originX, originY, originZ,
            Chunk.Size, cachedMasks, cachedMaskAvailability);

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var key = (originX + x, originY, originZ + z);
            GetDensityColumn(key).Merge(
                volume.SolidColumnMaskAtUnchecked(x, z),
                ushort.MaxValue);
        }

        return volume;
    }

    /// <summary>
    /// Samples a bounded Y slab within one vertical chunk band and publishes
    /// the known cells into the same cache used by full chunk materialization.
    /// </summary>
    internal TerrainDensityVolume SampleDensitySlab(
        SurfaceTerrainColumn column,
        int originX,
        int originY,
        int originZ,
        int height,
        out int densityProbes)
    {
        ArgumentNullException.ThrowIfNull(column);
        var verticalOffset = originY % Chunk.Size;
        if (originY < 0 || height is < 1 or > Chunk.Size ||
            verticalOffset + height > Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(height));

        var bandOriginY = originY - verticalOffset;
        var requestedBits = HeightMask(height);
        var bandBits = (ushort)(requestedBits << verticalOffset);
        var cachedMasks = new ushort[Chunk.Area];
        var cachedMaskAvailability = new ushort[Chunk.Area];
        var cachedBitCount = 0;

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var index = x + Chunk.Size * z;
            if (!_densityColumns.TryGet(
                    (originX + x, bandOriginY, originZ + z), out var cached))
                continue;

            cached.Read(out var solidMask, out var sampledMask);
            cachedMasks[index] =
                (ushort)((solidMask >> verticalOffset) & requestedBits);
            cachedMaskAvailability[index] =
                (ushort)((sampledMask >> verticalOffset) & requestedBits);
            cachedBitCount += CountSetBits(cachedMaskAvailability[index]);
        }

        densityProbes = checked(Chunk.Area * height - cachedBitCount);
        var volume = _terrain.SampleDensityVolume(
            column, originX, originY, originZ,
            height, cachedMasks, cachedMaskAvailability);

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var key = (originX + x, bandOriginY, originZ + z);
            var solidMask = (ushort)(
                volume.SolidColumnMaskAtUnchecked(x, z) & requestedBits);
            GetDensityColumn(key).Merge(
                (ushort)(solidMask << verticalOffset),
                bandBits);
        }

        return volume;
    }

    /// <summary>
    /// Returns a full 16-voxel mask. Existing partial bits are retained, and
    /// only missing positions are evaluated before the mask is published.
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

        var key = (X: worldX, Y: originY, Z: worldZ);
        var occupancy = GetDensityColumn(key);
        occupancy.Read(out var cachedMask, out var sampledMask);
        if (sampledMask == ushort.MaxValue)
        {
            cacheHit = true;
            densityProbes = 0;
            return cachedMask;
        }

        cacheHit = false;
        var (coord, local) = VoxelCoordinates.FromWorld(worldX, originY, worldZ);
        var column = Get(coord.X, coord.Z);
        var mask = _terrain.SampleDensityColumnMask(
            column, local.X, originY, local.Z, worldX, worldZ,
            cachedMask, sampledMask, out densityProbes);
        occupancy.Merge(mask, ushort.MaxValue);
        occupancy.Read(out var completeMask, out _);
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
