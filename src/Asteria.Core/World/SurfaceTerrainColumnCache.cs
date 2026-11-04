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
        ushort> _densityColumns;

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
            ushort>(densityColumnCapacity);
    }

    public int CachedColumnCount => _columns.Count;

    public int CachedDensityColumnCount => _densityColumns.Count;

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
    /// Materializes a chunk density snapshot while reusing complete vertical
    /// occupancy masks previously sampled by adjacent chunks' face halos.
    /// Newly computed masks are published for later neighboring chunks.
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
        var cachedMaskAvailable = new bool[Chunk.Area];
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var index = x + Chunk.Size * z;
            if (_densityColumns.TryGet(
                    (originX + x, originY, originZ + z),
                    out var cachedMask))
            {
                cachedMasks[index] = cachedMask;
                cachedMaskAvailable[index] = true;
            }
        }

        var volume = _terrain.SampleDensityVolume(
            column,
            originX,
            originY,
            originZ,
            cachedMasks,
            cachedMaskAvailable);

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            var key = (originX + x, originY, originZ + z);
            var mask = volume.SolidColumnMaskAtUnchecked(x, z);
            _densityColumns.GetOrAdd(key, () => mask);
        }

        return volume;
    }

    /// <summary>
    /// Returns a complete 16-voxel occupancy mask for one world X/Z column
    /// and vertical chunk band. A miss samples that column once; adjacent
    /// chunk materializations can then reuse the same result.
    /// </summary>
    internal ushort GetDensityColumnMask(
        int worldX,
        int originY,
        int worldZ,
        out bool cacheHit)
    {
        if (originY < 0 || originY % Chunk.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(originY));

        var key = (X: worldX, Y: originY, Z: worldZ);
        if (_densityColumns.TryGet(key, out var cachedMask))
        {
            cacheHit = true;
            return cachedMask;
        }

        cacheHit = false;
        var (coord, local) = VoxelCoordinates.FromWorld(
            worldX, originY, worldZ);
        var column = Get(coord.X, coord.Z);
        return _densityColumns.GetOrAdd(
            key,
            () => _terrain.SampleDensityColumnMask(
                column,
                local.X,
                originY,
                local.Z,
                worldX,
                worldZ));
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
