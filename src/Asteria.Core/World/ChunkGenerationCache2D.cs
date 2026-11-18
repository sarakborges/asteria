namespace Asteria.Core.World;

/// <summary>
/// Retained two-dimensional view of the GenerationChunkHolder neighborhood
/// required by one Minecraft-style ChunkStep. The 16-cubed Asteria storage
/// sections are deliberately absent from this lifecycle cache.
/// </summary>
public sealed class ChunkGenerationCache2D : IDisposable
{
    private readonly IReadOnlyDictionary<ChunkColumnCoord, ChunkHolder> _holders;
    private ChunkDependencyClaim? _claim;

    internal ChunkGenerationCache2D(
        ChunkColumnCoord center,
        int radius,
        ChunkStep step,
        IReadOnlyDictionary<ChunkColumnCoord, ChunkHolder> holders,
        ChunkDependencyClaim claim)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Step = step ?? throw new ArgumentNullException(nameof(step));
        _holders = holders ?? throw new ArgumentNullException(nameof(holders));
        _claim = claim ?? throw new ArgumentNullException(nameof(claim));
        Center = center;
        Radius = radius;

        var width = checked(radius * 2 + 1);
        if (_holders.Count != checked(width * width))
            throw new ArgumentException(
                "A two-dimensional generation cache must contain its complete square neighborhood.",
                nameof(holders));
    }

    public ChunkColumnCoord Center { get; }

    public int Radius { get; }

    public int Count => _holders.Count;

    public ChunkStep Step { get; }

    public IReadOnlyCollection<ChunkColumnCoord> Columns => _holders.Keys;

    /// <summary>Gets a retained holder by absolute horizontal chunk position.</summary>
    public ChunkHolder GetHolder(ChunkColumnCoord column)
    {
        EnsureActive();
        EnsureInsideCache(column);
        return _holders[column];
    }

    /// <summary>Gets a retained holder by absolute X/Z chunk coordinates.</summary>
    public ChunkHolder GetHolder(int x, int z) =>
        GetHolder(new ChunkColumnCoord(x, z));

    /// <summary>
    /// Returns the minimum status required at this position by the step's
    /// accumulated dependency pyramid. Null means the target position itself
    /// or a position outside the dependency table has no prerequisite entry.
    /// </summary>
    public ChunkStatus? GetRequiredStatus(ChunkColumnCoord column)
    {
        EnsureActive();
        var distance = EnsureInsideCache(column);
        return Step.AccumulatedDependencies.GetRequiredStatus(distance);
    }

    public ChunkStatus? GetRequiredStatus(int x, int z) =>
        GetRequiredStatus(new ChunkColumnCoord(x, z));

    public void Dispose()
    {
        Interlocked.Exchange(ref _claim, null)?.Dispose();
    }

    private int EnsureInsideCache(ChunkColumnCoord column)
    {
        var distance = Math.Max(
            Math.Abs((long)column.X - Center.X),
            Math.Abs((long)column.Z - Center.Z));
        if (distance > Radius)
            throw new ArgumentOutOfRangeException(
                nameof(column),
                $"Chunk column {column} is outside the radius-{Radius} cache centered at {Center}.");

        if (!_holders.ContainsKey(column))
            throw new InvalidOperationException(
                $"The generation cache is missing retained holder {column}.");
        return (int)distance;
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_claim is null, this);
    }
}
