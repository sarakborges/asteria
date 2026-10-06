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
