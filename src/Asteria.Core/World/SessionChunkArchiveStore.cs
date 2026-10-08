namespace Asteria.Core.World;

public enum ChunkArchiveResult
{
    NotResident,
    ArchivedPristine,
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

        var dirty =
            IsDirty(
                coord,
                chunk.Revision);

        if (dirty)
        {
            _dirty.Add(
                coord);
        }

        // Session persistence is intentionally zero-copy. Every materialized
        // chunk becomes authoritative spatial state, including pristine and
        // empty chunks. The exact resident object moves into archive ownership;
        // query-only generator access never enters this store.
        _archived[coord] = chunk;

        return dirty
            ? ChunkArchiveResult.ArchivedDirty
            : ChunkArchiveResult.ArchivedPristine;
    }

    // Persistence reads both residency and archive state without moving chunks.
    // The caller captures at a quiescent world boundary.
    internal IEnumerable<(ChunkCoord Coord, Chunk Chunk)> ArchivedChunks =>
        _archived.Select(entry => (entry.Key, entry.Value));

    internal void RestoreArchived(ChunkCoord coord, Chunk chunk, bool dirty)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (coord.Y < 0 || _materializedBaselines.ContainsKey(coord))
            throw new InvalidOperationException(
                $"Saved chunk {coord} has an invalid or duplicate archive identity.");

        _archived.Add(coord, chunk);
        _materializedBaselines.Add(coord, chunk.Revision);
        if (dirty)
            _dirty.Add(coord);
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
