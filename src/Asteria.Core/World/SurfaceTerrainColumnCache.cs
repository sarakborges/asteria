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
}
