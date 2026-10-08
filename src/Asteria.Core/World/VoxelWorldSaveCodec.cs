namespace Asteria.Core.World;

/// <summary>
/// Immutable, bounded saved-chunk payload. Copy-on-read prevents filesystem
/// adapters from mutating the bytes retained by the snapshot.
/// </summary>
public sealed class SavedWorldChunk
{
    private readonly byte[] _bytes;

    public SavedWorldChunk(ChunkCoord coordinate, bool resident, bool dirty,
        ReadOnlySpan<byte> encodedChunk)
    {
        if (coordinate.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(coordinate),
                "Saved chunks cannot have a negative Y coordinate.");
        if (encodedChunk.IsEmpty || encodedChunk.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Saved chunk payload is empty or oversized.");

        Coordinate = coordinate;
        Resident = resident;
        Dirty = dirty;
        _bytes = encodedChunk.ToArray();
    }

    public ChunkCoord Coordinate { get; }
    public bool Resident { get; }
    public bool Dirty { get; }
    public int ByteLength => _bytes.Length;

    public byte[] CopyEncodedChunk() => (byte[])_bytes.Clone();

    internal ReadOnlySpan<byte> EncodedChunk => _bytes;
}

/// <summary>
/// Ordered records for all materialized chunks of one Sphere. Unmaterialized
/// chunks must never appear in this snapshot.
/// </summary>
public sealed class VoxelWorldSaveSnapshot
{
    public const int MaximumChunks = 131072;
    private readonly IReadOnlyList<SavedWorldChunk> _chunks;

    public VoxelWorldSaveSnapshot(IEnumerable<SavedWorldChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var collected = chunks.Take(MaximumChunks + 1).ToArray();
        if (collected.Length > MaximumChunks)
            throw new InvalidDataException("Too many chunks in world snapshot.");
        if (collected.Any(chunk => chunk is null))
            throw new ArgumentException("Saved chunk records cannot be null.", nameof(chunks));

        Array.Sort(collected, static (a, b) =>
        {
            var y = a.Coordinate.Y.CompareTo(b.Coordinate.Y);
            if (y != 0) return y;
            var z = a.Coordinate.Z.CompareTo(b.Coordinate.Z);
            return z != 0 ? z : a.Coordinate.X.CompareTo(b.Coordinate.X);
        });
        for (var i = 1; i < collected.Length; i++)
        {
            if (collected[i - 1].Coordinate == collected[i].Coordinate)
                throw new InvalidDataException(
                    $"Duplicate saved chunk coordinate: {collected[i].Coordinate}");
        }

        _chunks = Array.AsReadOnly(collected);
    }

    public IReadOnlyList<SavedWorldChunk> Chunks => _chunks;
}

/// <summary>
/// Captures every materialized chunk, including empty/pristine archives.
/// Decodes into a new VoxelWorld; failed validation never touches the live one.
/// Caller must pause/quiesce world mutation while capturing.
/// </summary>
public static class VoxelWorldSaveCodec
{
    public static VoxelWorldSaveSnapshot Capture(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(world);
        var entries = new List<SavedWorldChunk>();
        foreach (var source in world.CaptureChunkSources())
        {
            if (entries.Count == VoxelWorldSaveSnapshot.MaximumChunks)
                throw new InvalidDataException("World snapshot chunk limit exceeded.");

            var payload = ChunkSaveCodec.Encode(source.Chunk, blocks, fluids, dyes, layers);
            entries.Add(new SavedWorldChunk(
                source.Coord, source.Resident, source.Dirty, payload));
        }

        return new VoxelWorldSaveSnapshot(entries);
    }

    public static VoxelWorld Restore(
        VoxelWorldSaveSnapshot snapshot, BlockRegistry blocks,
        FluidRegistry fluids, DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var restored = new VoxelWorld();

        // The caller never receives this world unless every chunk passed the
        // namespaced-ID, checksum, occupancy and geometry validations.
        foreach (var entry in snapshot.Chunks)
        {
            var chunk = ChunkSaveCodec.Decode(
                entry.EncodedChunk, blocks, fluids, dyes, layers);
            restored.RestoreSavedChunk(
                entry.Coordinate, chunk, entry.Resident, entry.Dirty);
        }

        return restored;
    }
}
