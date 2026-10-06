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
}
