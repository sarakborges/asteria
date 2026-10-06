namespace Asteria.Core.World;

public enum ChunkArchiveResult
{
    NotResident,
    DroppedPristine,
    ArchivedDirty,
}

public enum ChunkRestoreResult
{
    Missing,
    AlreadyResident,
    Restored,
}

public sealed class SessionChunkArchiveStore
{
    private readonly Dictionary<ChunkCoord, Chunk> _archived = [];
    private readonly Dictionary<ChunkCoord, ulong> _materializedBaselines = [];
    private readonly HashSet<ChunkCoord> _dirty = [];

    public int ArchivedCount => _archived.Count;

    public int DirtyCount => _dirty.Count;

    public bool HasArchived(ChunkCoord coord) =>
        _archived.ContainsKey(coord);

    public bool IsDirty(
        ChunkCoord coord,
        ulong currentRevision) =>
        _dirty.Contains(coord) ||
        (_materializedBaselines.TryGetValue(
             coord,
             out var baseline) &&
         baseline != currentRevision);

    public void TrackMaterialized(
        ChunkCoord coord,
        ulong contentRevision)
    {
        _materializedBaselines[coord] = contentRevision;
    }

    public void MarkDirty(ChunkCoord coord)
    {
        _dirty.Add(coord);
    }

    public ChunkArchiveResult Archive(
        ChunkCoord coord,
        Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!IsDirty(coord, chunk.Revision))
        {
            _archived.Remove(coord);
            return ChunkArchiveResult.DroppedPristine;
        }

        _dirty.Add(coord);

        // Session persistence is intentionally zero-copy. The exact resident
        // chunk object moves into archive ownership; no palette expansion,
        // serialization or clone is needed just to leave residency.
        _archived[coord] = chunk;
        return ChunkArchiveResult.ArchivedDirty;
    }

    public bool TryRestore(
        ChunkCoord coord,
        out Chunk chunk)
    {
        if (!_archived.Remove(coord, out chunk!))
        {
            return false;
        }

        return true;
    }
}
