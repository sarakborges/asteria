namespace Asteria.Core.World;

/// <summary>
/// Query-side cache of immutable 32x32 biome/terrain samples shared by range
/// selection and vertical chunk materialization. Entries belong to one
/// dimension's generator lifetime, and eviction changes no generated fact.
/// </summary>
public sealed class SurfaceTerrainColumnCache
{
    private readonly SurfaceTerrainField _terrain;
    private readonly BoundedMemoCache<
        (int X, int Z),
        SurfaceTerrainColumn> _columns;

    public SurfaceTerrainColumnCache(
        SurfaceTerrainField terrain,
        int capacity = 128)
    {
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        _columns = new BoundedMemoCache<
            (int X, int Z),
            SurfaceTerrainColumn>(capacity);
    }

    public int CachedColumnCount => _columns.Count;

    public SurfaceTerrainColumn Get(int chunkX, int chunkZ) =>
        _columns.GetOrAdd(
            (chunkX, chunkZ),
            () => _terrain.SampleColumn(chunkX, chunkZ));

    /// <summary>
    /// Samples the 33x33 vertex grid for one chunk from its four immutable
    /// neighboring 32x32 column snapshots. The extra row/column preserves
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
