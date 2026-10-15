namespace Asteria.Core.World;

public readonly record struct ChunkContentRevision(
    ulong ResidencyEpoch,
    ulong ChunkRevision);

public sealed class ChunkContentStamp
{
    private readonly Dictionary<ChunkCoord, ChunkContentRevision>
        _revisions;

    internal ChunkContentStamp(
        Dictionary<ChunkCoord, ChunkContentRevision> revisions)
    {
        _revisions =
            revisions ??
            throw new ArgumentNullException(nameof(revisions));
    }

    public int Count => _revisions.Count;

    internal IEnumerable<KeyValuePair<ChunkCoord, ChunkContentRevision>>
        Entries => _revisions;

    // No per-meshlet dictionary allocations during repeated restore/frontier
    // checks: each captured footprint is already represented by the stamp.
    internal bool MatchesMaterializedFootprint(
        VoxelWorld world, ChunkResidencyStamp footprint)
    {
        foreach (var (coord, _) in footprint.Entries)
        {
            var expected = _revisions.TryGetValue(coord, out var prior);
            var present = world.TryGetMaterializedRevision(
                coord, out var currentRevision);
            if (present != expected ||
                (present && prior.ChunkRevision != currentRevision))
                return false;
        }

        return true;
    }

    internal bool HasSameSpatialEntriesAs(ChunkContentStamp other) =>
        _revisions.Count == other._revisions.Count &&
        _revisions.All(entry =>
            other._revisions.TryGetValue(entry.Key, out var revision) &&
            revision.ChunkRevision == entry.Value.ChunkRevision);

    internal bool HasSameEntriesAs(ChunkContentStamp other) =>
        _revisions.Count == other._revisions.Count &&
        _revisions.All(entry =>
            other._revisions.TryGetValue(
                entry.Key, out var revision) &&
            revision == entry.Value);
}

public readonly record struct ChunkColumnCoord(int X, int Z)
{
    public static ChunkColumnCoord FromChunk(ChunkCoord coord) =>
        new(coord.X, coord.Z);
}

public sealed class ChunkColumnResidencyStamp
{
    private readonly Dictionary<ChunkColumnCoord, ulong> _revisions;

    internal ChunkColumnResidencyStamp(
        Dictionary<ChunkColumnCoord, ulong> revisions)
    {
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
    }

    internal IEnumerable<KeyValuePair<ChunkColumnCoord, ulong>> Entries => _revisions;
}

public sealed class ChunkResidencyStamp
{
    private readonly Dictionary<ChunkCoord, ulong>
        _epochs;

    internal ChunkResidencyStamp(
        Dictionary<ChunkCoord, ulong> epochs)
    {
        _epochs =
            epochs ??
            throw new ArgumentNullException(nameof(epochs));
    }

    internal IEnumerable<KeyValuePair<ChunkCoord, ulong>>
        Entries => _epochs;

    internal bool HasSameCoordinatesAs(ChunkResidencyStamp other) =>
        _epochs.Count == other._epochs.Count &&
        _epochs.Keys.All(other._epochs.ContainsKey);

    internal bool HasSameEntriesAs(ChunkResidencyStamp other) =>
        _epochs.Count == other._epochs.Count &&
        _epochs.All(entry =>
            other._epochs.TryGetValue(entry.Key, out var epoch) &&
            epoch == entry.Value);
}
