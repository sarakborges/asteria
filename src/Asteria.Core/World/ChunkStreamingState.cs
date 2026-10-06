namespace Asteria.Core.World;

public readonly record struct ChunkMovementDirection(int X, int Z)
{
    public static ChunkMovementDirection Zero => default;
}

public readonly record struct ChunkLoadPriority(
    long HorizontalDistanceSquared,
    long TotalDistanceSquared,
    int DirectionalBand,
    int Y,
    int Z,
    int X) : IComparable<ChunkLoadPriority>
{
    public int CompareTo(ChunkLoadPriority other)
    {
        var result = HorizontalDistanceSquared.CompareTo(
            other.HorizontalDistanceSquared);
        if (result != 0) return result;

        result = TotalDistanceSquared.CompareTo(
            other.TotalDistanceSquared);
        if (result != 0) return result;

        result = DirectionalBand.CompareTo(other.DirectionalBand);
        if (result != 0) return result;

        result = Y.CompareTo(other.Y);
        if (result != 0) return result;

        result = Z.CompareTo(other.Z);
        return result != 0 ? result : X.CompareTo(other.X);
    }
}

public sealed class ChunkStreamingState
{
    private readonly HashSet<ChunkCoord> _desired = [];
    private readonly HashSet<ChunkCoord> _retained = [];
    private readonly HashSet<ChunkCoord> _pending = [];
    private readonly HashSet<ChunkCoord> _materializing = [];
    private readonly HashSet<ChunkCoord> _presentationPending = [];
    private readonly HashSet<ChunkCoord> _retired = [];

    public ChunkCoord? Center { get; private set; }

    public int HorizontalRadius { get; private set; }

    public ChunkMovementDirection MovementDirection { get; private set; }

    public int DesiredCount => _desired.Count;

    public int RetainedCount => _retained.Count;

    public int PendingCount => _pending.Count;

    public int MaterializingCount => _materializing.Count;

    public int PresentationPendingCount => _presentationPending.Count;

    public bool HasRenderableBacklog =>
        _pending.Count > 0 ||
        _materializing.Count > 0 ||
        _presentationPending.Count > 0;

    public bool KeepsLoaded(ChunkCoord coord) =>
        _desired.Contains(coord) || _retained.Contains(coord);

    public bool IsDesired(ChunkCoord coord) => _desired.Contains(coord);

    public bool IsMaterializing(ChunkCoord coord) =>
        _materializing.Contains(coord);

    public bool RebuildSelection(
        ChunkCoord center,
        int horizontalRadius,
        int retentionRadius,
        IReadOnlySet<ChunkCoord> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);

        if (Center == center &&
            HorizontalRadius == horizontalRadius &&
            _desired.SetEquals(desired))
        {
            return false;
        }

        UpdateMovementDirection(center);

        var previousInterest = _desired
            .Concat(_retained)
            .ToArray();

        _desired.Clear();
        _desired.UnionWith(desired);
        _retained.Clear();

        foreach (var coord in previousInterest)
        {
            if (_desired.Contains(coord))
            {
                continue;
            }

            if (InsideHorizontalRadius(
                    coord,
                    center,
                    retentionRadius))
            {
                _retained.Add(coord);
            }
            else
            {
                _retired.Add(coord);
            }
        }

        _retired.RemoveWhere(coord =>
            _desired.Contains(coord) ||
            _retained.Contains(coord));

        _pending.RemoveWhere(coord =>
            !_desired.Contains(coord));
        _presentationPending.RemoveWhere(coord =>
            !_desired.Contains(coord));

        Center = center;
        HorizontalRadius = horizontalRadius;
        return true;
    }

    public void SyncResidentState(
        IEnumerable<ChunkCoord> resident,
        IEnumerable<ChunkCoord> presented)
    {
        ArgumentNullException.ThrowIfNull(resident);
        ArgumentNullException.ThrowIfNull(presented);

        var residentSet = resident.ToHashSet();
        var presentedSet = presented.ToHashSet();

        foreach (var coord in _desired)
        {
            if (!residentSet.Contains(coord) &&
                !_materializing.Contains(coord))
            {
                _pending.Add(coord);
                continue;
            }

            if (residentSet.Contains(coord) &&
                !presentedSet.Contains(coord))
            {
                _presentationPending.Add(coord);
            }
        }

        _pending.RemoveWhere(coord =>
            residentSet.Contains(coord) ||
            _materializing.Contains(coord) ||
            !_desired.Contains(coord));

        _presentationPending.RemoveWhere(coord =>
            presentedSet.Contains(coord) ||
            !residentSet.Contains(coord) ||
            !_desired.Contains(coord));
    }

    public void EnqueuePending(ChunkCoord coord)
    {
        if (IsDesired(coord) &&
            !_materializing.Contains(coord))
        {
            _pending.Add(coord);
        }
    }

    public ChunkCoord? PopPendingByPriority()
    {
        if (Center is not { } center ||
            _pending.Count == 0)
        {
            return null;
        }

        var selected = _pending
            .OrderBy(coord =>
                Priority(
                    coord,
                    center,
                    MovementDirection))
            .First();

        _pending.Remove(selected);
        return selected;
    }

    public void MarkMaterializing(ChunkCoord coord)
    {
        if (!_materializing.Add(coord))
        {
            throw new InvalidOperationException(
                $"Chunk is already materializing: {coord}");
        }

        _pending.Remove(coord);
    }

    public void FinishMaterializing(ChunkCoord coord)
    {
        _materializing.Remove(coord);
    }

    public void EnqueuePresentation(ChunkCoord coord)
    {
        if (KeepsLoaded(coord))
        {
            _presentationPending.Add(coord);
        }
    }

    public ChunkCoord? PopPresentationByPriority(
        ChunkPresentationSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (Center is not { } center)
        {
            return null;
        }

        var candidates = _presentationPending
            .Where(selection.RetainsRenderMesh)
            .OrderBy(coord =>
                Priority(
                    coord,
                    center,
                    MovementDirection))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var selected = candidates[0];
        _presentationPending.Remove(selected);
        return selected;
    }

    public ChunkCoord? PopRetiredOutsideHorizontalRadius(
        ChunkCoord center,
        int retentionRadius)
    {
        var candidates = _retired
            .Where(coord =>
                !KeepsLoaded(coord) &&
                !InsideHorizontalRadius(
                    coord,
                    center,
                    retentionRadius))
            .OrderByDescending(coord =>
                HorizontalDistanceSquared(
                    coord,
                    center))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var selected = candidates[0];
        _retired.Remove(selected);
        return selected;
    }

    public void Forget(ChunkCoord coord)
    {
        _desired.Remove(coord);
        _retained.Remove(coord);
        _pending.Remove(coord);
        _materializing.Remove(coord);
        _presentationPending.Remove(coord);
        _retired.Remove(coord);
    }

    public static ChunkLoadPriority Priority(
        ChunkCoord coord,
        ChunkCoord center,
        ChunkMovementDirection movementDirection)
    {
        var dx = (long)coord.X - center.X;
        var dy = (long)coord.Y - center.Y;
        var dz = (long)coord.Z - center.Z;
        var horizontalDistance =
            dx * dx + dz * dz;
        var totalDistance =
            horizontalDistance + dy * dy;
        var forward =
            dx * movementDirection.X +
            dz * movementDirection.Z;

        var directionalBand =
            movementDirection == ChunkMovementDirection.Zero ||
            forward == 0
                ? 1
                : forward > 0
                    ? 0
                    : 2;

        return new ChunkLoadPriority(
            horizontalDistance,
            totalDistance,
            directionalBand,
            coord.Y,
            coord.Z,
            coord.X);
    }

    private void UpdateMovementDirection(ChunkCoord center)
    {
        if (Center is not { } previous)
        {
            MovementDirection =
                ChunkMovementDirection.Zero;
            return;
        }

        var dx = center.X - previous.X;
        var dz = center.Z - previous.Z;

        if (dx != 0 || dz != 0)
        {
            MovementDirection =
                new ChunkMovementDirection(
                    Math.Sign(dx),
                    Math.Sign(dz));
        }
    }

    private static bool InsideHorizontalRadius(
        ChunkCoord coord,
        ChunkCoord center,
        int radius)
    {
        var squared = HorizontalDistanceSquared(
            coord,
            center);
        var clamped = Math.Max(0, radius);
        return squared <= (long)clamped * clamped;
    }

    private static long HorizontalDistanceSquared(
        ChunkCoord coord,
        ChunkCoord center)
    {
        var dx = (long)coord.X - center.X;
        var dz = (long)coord.Z - center.Z;
        return dx * dx + dz * dz;
    }
}
