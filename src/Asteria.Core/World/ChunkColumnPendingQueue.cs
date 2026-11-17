namespace Asteria.Core.World;

/// <summary>
/// One pending column admission, containing the exact vertical sections that
/// were desired when the column was removed from the pending queue.
/// </summary>
internal readonly record struct ChunkColumnPendingDispatch(
    ChunkColumnCoord Column,
    IReadOnlyList<ChunkPendingDispatch> Sections,
    ChunkGenerationTaskPriority Priority,
    long EnqueuedTimestamp,
    ulong EnqueueSequence);

/// <summary>
/// Authoritative pending admission index grouped by horizontal chunk column.
/// The shared ChunkGenerationTaskQueue provides deterministic ordering,
/// deduplication, stable enqueue age and in-place priority updates. Section
/// timestamps remain independent so a requeued section does not lose its age.
/// </summary>
internal sealed class ChunkColumnPendingQueue
{
    private readonly ChunkGenerationTaskQueue _columns = new();
    private readonly Dictionary<
        ChunkColumnCoord,
        SortedDictionary<int, ChunkPendingDispatch>> _sectionsByColumn = [];

    private ChunkCoord _center;
    private Func<ChunkCoord, int>? _ticketLevelProvider;
    private Func<ChunkColumnCoord, ChunkGenerationTaskPriority?>?
        _holderPriorityProvider;
    private bool _hasPriorityContext;

    public int Count { get; private set; }

    public int ColumnCount => _sectionsByColumn.Count;

    public bool IsEmpty => Count == 0;

    public bool Contains(ChunkCoord coord) =>
        _sectionsByColumn.TryGetValue(
            ChunkColumnCoord.FromChunk(coord), out var sections) &&
        sections.ContainsKey(coord.Y);

    /// <summary>
    /// Recomputes every queued column's key after the player center changes.
    /// ChunkGenerationTaskQueue preserves the original enqueue timestamp and
    /// FIFO sequence while reordering the columns.
    /// </summary>
    public void UpdatePriorityContext(
        ChunkCoord center,
        Func<ChunkCoord, int> ticketLevelProvider,
        Func<ChunkColumnCoord, ChunkGenerationTaskPriority?> holderPriorityProvider)
    {
        _center = center;
        _ticketLevelProvider = ticketLevelProvider ??
            throw new ArgumentNullException(nameof(ticketLevelProvider));
        _holderPriorityProvider = holderPriorityProvider ??
            throw new ArgumentNullException(nameof(holderPriorityProvider));
        _hasPriorityContext = true;
        ReprioritizeColumns();
    }

    /// <summary>
    /// Applies the holder's canonical queue key to a pending column. Active
    /// dispatch and pending admission now consume the same priority revision.
    /// </summary>
    public void UpdateHolderPriority(
        ChunkColumnCoord column,
        ChunkGenerationTaskPriority priority)
    {
        if (!_sectionsByColumn.ContainsKey(column))
            return;

        if (!_columns.UpdatePriority(Key(column), priority))
        {
            throw new InvalidOperationException(
                $"Pending column is missing from its priority index: {column}");
        }
    }

    private void ReprioritizeColumns()
    {
        foreach (var column in _sectionsByColumn.Keys
                     .OrderBy(column => column.X)
                     .ThenBy(column => column.Z))
        {
            if (!_columns.UpdatePriority(
                    Key(column),
                    CalculatePriority(column)))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }
        }
    }

    /// <summary>
    /// Adds one section to its horizontal column. Duplicate requests preserve
    /// the first timestamp and do not perturb queue ordering.
    /// </summary>
    public bool Enqueue(ChunkCoord coord, long enqueuedTimestamp)
    {
        EnsurePriorityContext();
        var column = ChunkColumnCoord.FromChunk(coord);
        if (_sectionsByColumn.TryGetValue(column, out var existing))
        {
            if (existing.ContainsKey(coord.Y))
                return false;

            existing.Add(coord.Y, new ChunkPendingDispatch(
                coord, enqueuedTimestamp));
            Count++;
            if (!_columns.UpdatePriority(
                    Key(column),
                    CalculatePriority(column, existing.Values)))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }

            return true;
        }

        var sections = new SortedDictionary<int, ChunkPendingDispatch>
        {
            [coord.Y] = new ChunkPendingDispatch(coord, enqueuedTimestamp),
        };
        if (!_columns.Enqueue(
                Key(column),
                CalculatePriority(column),
                enqueuedTimestamp))
        {
            throw new InvalidOperationException(
                $"Pending column already exists in the priority index: {column}");
        }

        _sectionsByColumn.Add(column, sections);
        Count++;
        return true;
    }

    public bool Remove(ChunkCoord coord)
    {
        var column = ChunkColumnCoord.FromChunk(coord);
        if (!_sectionsByColumn.TryGetValue(column, out var sections) ||
            !sections.Remove(coord.Y))
        {
            return false;
        }

        Count--;
        if (sections.Count == 0)
        {
            _sectionsByColumn.Remove(column);
            if (!_columns.Remove(Key(column)))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }
        }
        else if (!_columns.UpdatePriority(
                     Key(column),
                     CalculatePriority(column)))
        {
            throw new InvalidOperationException(
                $"Pending column is missing from its priority index: {column}");
        }

        return true;
    }

    public int RemoveWhere(Predicate<ChunkCoord> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var removed = 0;
        var coords = _sectionsByColumn.Values
            .SelectMany(sections => sections.Values)
            .Select(item => item.Coord)
            .Where(coord => predicate(coord))
            .ToArray();
        foreach (var coord in coords)
        {
            if (Remove(coord))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Removes the best section while retaining the remaining sections under
    /// one column key. This keeps the legacy one-section consumer compatible
    /// during migration to PopPendingColumnEntryByPriority.
    /// </summary>
    public bool TryDequeueSection(
        long? maximumHorizontalDistanceSquared,
        out ChunkPendingDispatch dispatch)
    {
        ValidateMaximumDistance(maximumHorizontalDistanceSquared);
        if (!TryFindBestColumn(
                maximumHorizontalDistanceSquared, out var queued))
        {
            dispatch = default;
            return false;
        }

        var column = queued.Key.Column;
        if (!_sectionsByColumn.TryGetValue(column, out var sections) ||
            sections.Count == 0)
        {
            throw new InvalidOperationException(
                $"Pending column has no section entries: {column}");
        }

        var selected = sections.Values
            .OrderBy(item => StreamingPriority(item.Coord))
            .ThenBy(item => item.EnqueuedTimestamp)
            .First();
        sections.Remove(selected.Coord.Y);
        Count--;

        if (sections.Count == 0)
        {
            _sectionsByColumn.Remove(column);
            if (!_columns.Remove(queued.Key))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }
        }
        else if (!_columns.UpdatePriority(
                     queued.Key,
                     CalculatePriority(column)))
        {
            throw new InvalidOperationException(
                $"Pending column is missing from its priority index: {column}");
        }

        dispatch = selected;
        return true;
    }

    /// <summary>
    /// Removes all currently pending sections for the best eligible X/Z
    /// column. Sections are returned in the same deterministic streaming
    /// priority order used by the legacy section-at-a-time admission path.
    /// </summary>
    public bool TryDequeueColumn(
        long? maximumHorizontalDistanceSquared,
        out ChunkColumnPendingDispatch dispatch) =>
        TryDequeueColumn(
            maximumHorizontalDistanceSquared,
            int.MaxValue,
            out dispatch);

    /// <summary>
    /// Removes at most <paramref name="maximumSections"/> sections from the
    /// best horizontal column. Any remaining vertical sections stay indexed
    /// under the same column admission key and retain their individual ages.
    /// </summary>
    public bool TryDequeueColumn(
        long? maximumHorizontalDistanceSquared,
        int maximumSections,
        out ChunkColumnPendingDispatch dispatch)
    {
        ValidateMaximumDistance(maximumHorizontalDistanceSquared);
        if (maximumSections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSections));
        if (!TryFindBestColumn(
                maximumHorizontalDistanceSquared, out var queued))
        {
            dispatch = default;
            return false;
        }

        var column = queued.Key.Column;
        if (!_sectionsByColumn.TryGetValue(column, out var sections) ||
            sections.Count == 0)
        {
            throw new InvalidOperationException(
                $"Pending column has no section entries: {column}");
        }

        var orderedSections = sections.Values
            .OrderBy(item => StreamingPriority(item.Coord))
            .ThenBy(item => item.EnqueuedTimestamp)
            .Take(maximumSections)
            .ToArray();
        foreach (var section in orderedSections)
            sections.Remove(section.Coord.Y);

        Count -= orderedSections.Length;
        if (sections.Count == 0)
        {
            _sectionsByColumn.Remove(column);
            if (!_columns.Remove(queued.Key))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }
        }
        else if (!_columns.UpdatePriority(
                     queued.Key,
                     CalculatePriority(column)))
        {
            throw new InvalidOperationException(
                $"Pending column is missing from its priority index: {column}");
        }

        dispatch = new ChunkColumnPendingDispatch(
            column,
            orderedSections,
            queued.Priority,
            queued.EnqueuedTimestamp,
            queued.EnqueueSequence);
        return true;
    }

    private bool TryFindBestColumn(
        long? maximumHorizontalDistanceSquared,
        out ChunkGenerationQueuedTask selected)
    {
        if (Count == 0)
        {
            selected = default;
            return false;
        }

        if (maximumHorizontalDistanceSquared is null)
        {
            if (_columns.TryPeek(out selected))
                return true;

            throw new InvalidOperationException(
                "Pending sections exist without a queued column.");
        }

        var hasSelected = false;
        selected = default;
        foreach (var column in _sectionsByColumn.Keys)
        {
            if (!_columns.TryGet(Key(column), out var candidate))
            {
                throw new InvalidOperationException(
                    $"Pending column is missing from its priority index: {column}");
            }

            if (candidate.Priority.HorizontalDistanceSquared >
                maximumHorizontalDistanceSquared.Value)
                continue;

            if (!hasSelected || Compare(candidate, selected) < 0)
            {
                selected = candidate;
                hasSelected = true;
            }
        }

        return hasSelected;
    }

    private ChunkGenerationTaskPriority CalculatePriority(
        ChunkColumnCoord column)
    {
        if (_holderPriorityProvider?.Invoke(column) is { } holderPriority)
            return holderPriority;

        // Until a holder exists, use the same horizontal key the holder will
        // publish once the loading graph admits this column.
        return StreamingPriority(new ChunkCoord(
            column.X, _center.Y, column.Z));
    }

    private ChunkGenerationTaskPriority StreamingPriority(ChunkCoord coord) =>
        ChunkStreamingState.StreamingPriority(
            coord,
            _center,
            _ticketLevelProvider?.Invoke(coord));

    private static int Compare(
        ChunkGenerationQueuedTask left,
        ChunkGenerationQueuedTask right)
    {
        var result = left.Priority.CompareTo(right.Priority);
        if (result != 0)
            return result;

        result = left.EnqueueSequence.CompareTo(right.EnqueueSequence);
        return result != 0
            ? result
            : left.Key.Column.CompareTo(right.Key.Column);
    }

    private static ChunkGenerationTaskKey Key(ChunkColumnCoord column) =>
        new(column, ChunkGenerationStepId.SectionReady);

    private void EnsurePriorityContext()
    {
        if (!_hasPriorityContext)
        {
            throw new InvalidOperationException(
                "Pending columns cannot be enqueued before streaming selection is initialized.");
        }
    }

    private static void ValidateMaximumDistance(
        long? maximumHorizontalDistanceSquared)
    {
        if (maximumHorizontalDistanceSquared is < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumHorizontalDistanceSquared));
    }
}
