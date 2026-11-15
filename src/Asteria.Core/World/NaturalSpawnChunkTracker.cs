namespace Asteria.Core.World;

/// <summary>
/// Minecraft's naturalSpawnChunkCounter is a third, fixed-radius tracker,
/// independent of both ticket distance graphs. It selects candidate columns;
/// the entity-ticking graph remains a separate gate before spawning.
/// </summary>
public sealed class NaturalSpawnChunkTracker
{
    public const int RadiusChunks = 8;

    private readonly HashSet<ChunkColumnCoord> _columns = [];
    public ChunkColumnCoord? Center { get; private set; }

    public int Count => _columns.Count;

    public bool Contains(ChunkColumnCoord column) =>
        _columns.Contains(column);

    public bool UpdateCenter(ChunkColumnCoord center)
    {
        if (Center == center)
            return false;

        _columns.Clear();
        for (var dz = -RadiusChunks; dz <= RadiusChunks; dz++)
        for (var dx = -RadiusChunks; dx <= RadiusChunks; dx++)
        {
            var x = (long)center.X + dx;
            var z = (long)center.Z + dz;
            if (x < int.MinValue || x > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
                continue;

            _columns.Add(ChunkColumnCoord.FromChunk(
                new ChunkCoord((int)x, 0, (int)z)));
        }

        Center = center;
        return true;
    }
}
