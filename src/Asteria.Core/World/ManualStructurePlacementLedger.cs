namespace Asteria.Core.World;

/// <summary>
/// World-space reservation for one successfully committed manual /place.
/// Bounds are inclusive; a multi-piece StructureSet owns one reservation.
/// </summary>
public sealed record ManualStructurePlacementFootprint
{
    public ManualStructurePlacementFootprint(
        string reference, int anchorX, int anchorZ,
        int minimumX, int maximumX, int minimumY, int maximumY,
        int minimumZ, int maximumZ)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (minimumX > maximumX || minimumY < 0 || minimumY > maximumY ||
            minimumZ > maximumZ)
            throw new ArgumentException("Manual placement bounds are invalid.");

        Reference = reference;
        AnchorX = anchorX;
        AnchorZ = anchorZ;
        MinimumX = minimumX;
        MaximumX = maximumX;
        MinimumY = minimumY;
        MaximumY = maximumY;
        MinimumZ = minimumZ;
        MaximumZ = maximumZ;
    }

    public string Reference { get; }
    public int AnchorX { get; }
    public int AnchorZ { get; }
    public int MinimumX { get; }
    public int MaximumX { get; }
    public int MinimumY { get; }
    public int MaximumY { get; }
    public int MinimumZ { get; }
    public int MaximumZ { get; }

    public bool Intersects(ManualStructurePlacementFootprint other) =>
        MinimumX <= other.MaximumX && MaximumX >= other.MinimumX &&
        MinimumY <= other.MaximumY && MaximumY >= other.MinimumY &&
        MinimumZ <= other.MaximumZ && MaximumZ >= other.MinimumZ;
}

/// <summary>
/// Authoritative per-Sphere committed-manual-placement journal. Unlike the
/// generated field, placement history must survive chunk eviction and session
/// retirement. A sparse X/Z chunk index bounds query cost to local footprints.
/// Records are retained for the lifetime of the parent world/session state:
/// registration happens only after a complete successful voxel transaction.
/// </summary>
public sealed class ManualStructurePlacementLedger
{
    private readonly List<ManualStructurePlacementFootprint> _entries = [];
    private readonly Dictionary<(int X, int Z), List<int>> _byColumn = [];

    public int Count => _entries.Count;

    public IReadOnlyList<ManualStructurePlacementFootprint> Snapshot() =>
        Array.AsReadOnly(_entries.ToArray());

    public bool Overlaps(ManualStructurePlacementFootprint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var seen = new HashSet<int>();
        foreach (var column in Columns(candidate))
        {
            if (!_byColumn.TryGetValue(column, out var indices))
                continue;

            foreach (var index in indices)
            {
                if (seen.Add(index) && _entries[index].Intersects(candidate))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Call only after canonical mutation publication succeeds.
    /// The gameplay thread serializes preflight, publication and registration.</summary>
    public void RecordCommitted(ManualStructurePlacementFootprint placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (Overlaps(placement))
            throw new InvalidOperationException(
                "A committed manual structure conflicts with an earlier placement.");

        var index = _entries.Count;
        _entries.Add(placement);
        foreach (var column in Columns(placement))
        {
            if (!_byColumn.TryGetValue(column, out var indices))
            {
                indices = [];
                _byColumn.Add(column, indices);
            }
            indices.Add(index);
        }
    }

    private static IEnumerable<(int X, int Z)> Columns(
        ManualStructurePlacementFootprint placement)
    {
        var min = VoxelCoordinates.FromWorld(
            placement.MinimumX, 0, placement.MinimumZ).Chunk;
        var max = VoxelCoordinates.FromWorld(
            placement.MaximumX, 0, placement.MaximumZ).Chunk;
        for (var x = min.X; x <= max.X; x++)
        for (var z = min.Z; z <= max.Z; z++)
            yield return (x, z);
    }
}
