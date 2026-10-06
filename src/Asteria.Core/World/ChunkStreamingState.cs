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
    private readonly RetiredChunkQueue _retired = new();

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

        var retired =
            new List<ChunkCoord>();

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
                retired.Add(coord);
            }
        }

        retired.Sort(
            (left, right) =>
                CompareRetirementPriority(
                    left,
                    right,
                    center));

        foreach (var coord in retired)
        {
            _retired.Enqueue(coord);
        }

        foreach (var coord in _desired)
        {
            _retired.Remove(coord);
        }

        foreach (var coord in _retained)
        {
            _retired.Remove(coord);
        }

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

        ChunkCoord? selected = null;
        var selectedPriority =
            default(ChunkLoadPriority);

        foreach (var coord in _pending)
        {
            var priority =
                Priority(
                    coord,
                    center,
                    MovementDirection);

            if (selected is null ||
                priority.CompareTo(
                    selectedPriority) < 0)
            {
                selected = coord;
                selectedPriority = priority;
            }
        }

        if (selected is not { } result)
        {
            return null;
        }

        _pending.Remove(result);
        return result;
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

        ChunkCoord? selected = null;
        var selectedPriority =
            default(ChunkLoadPriority);

        foreach (var coord in
                 _presentationPending)
        {
            if (!selection.RetainsRenderMesh(
                    coord))
            {
                continue;
            }

            var priority =
                Priority(
                    coord,
                    center,
                    MovementDirection);

            if (selected is null ||
                priority.CompareTo(
                    selectedPriority) < 0)
            {
                selected = coord;
                selectedPriority = priority;
            }
        }

        if (selected is not { } result)
        {
            return null;
        }

        _presentationPending.Remove(result);
        return result;
    }

    public ChunkCoord? PopRetiredOutsideHorizontalRadius(
        ChunkCoord center,
        int retentionRadius) =>
        _retired.PopOutsideHorizontalRadius(
            center,
            retentionRadius,
            _desired,
            _retained);

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

    private static int CompareRetirementPriority(
        ChunkCoord left,
        ChunkCoord right,
        ChunkCoord center)
    {
        var comparison =
            HorizontalDistanceSquared(
                right,
                center)
                .CompareTo(
                    HorizontalDistanceSquared(
                        left,
                        center));

        if (comparison != 0)
        {
            return comparison;
        }

        comparison =
            left.Y.CompareTo(right.Y);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison =
            left.Z.CompareTo(right.Z);
        return comparison != 0
            ? comparison
            : left.X.CompareTo(right.X);
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

    private sealed class RetiredChunkQueue
    {
        private const int MaximumScanStepsPerPoll = 64;

        private readonly DeduplicatedQueue<ChunkCoord>
            _queue = new();

        private ChunkCoord? _scanCenter;
        private int _scanRadius;
        private ulong _scanRevision;
        private int _remaining;

        public void Enqueue(
            ChunkCoord coord) =>
            _queue.Enqueue(coord);

        public void Remove(
            ChunkCoord coord) =>
            _queue.Remove(coord);

        public ChunkCoord? PopOutsideHorizontalRadius(
            ChunkCoord center,
            int retentionRadius,
            IReadOnlySet<ChunkCoord> desired,
            IReadOnlySet<ChunkCoord> retained)
        {
            ArgumentNullException.ThrowIfNull(desired);
            ArgumentNullException.ThrowIfNull(retained);

            var radius =
                Math.Max(
                    0,
                    retentionRadius);
            var revision =
                _queue.Revision;

            if (_scanCenter != center ||
                _scanRadius != radius ||
                _scanRevision != revision)
            {
                _scanCenter = center;
                _scanRadius = radius;
                _scanRevision = revision;
                _remaining = _queue.Count;
            }

            var steps =
                Math.Min(
                    _remaining,
                    MaximumScanStepsPerPoll);

            for (var index = 0;
                 index < steps;
                 index++)
            {
                if (!_queue.TryDequeue(
                        out var coord))
                {
                    _remaining = 0;
                    _scanRevision =
                        _queue.Revision;
                    return null;
                }

                _remaining--;

                if (desired.Contains(coord) ||
                    retained.Contains(coord))
                {
                    _scanRevision =
                        _queue.Revision;
                    continue;
                }

                if (!InsideHorizontalRadius(
                        coord,
                        center,
                        radius))
                {
                    _scanRevision =
                        _queue.Revision;
                    return coord;
                }

                _queue.Enqueue(coord);
                _scanRevision =
                    _queue.Revision;
            }

            return null;
        }
    }
}
