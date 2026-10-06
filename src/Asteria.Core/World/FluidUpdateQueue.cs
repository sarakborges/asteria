namespace Asteria.Core.World;

public sealed class FluidUpdateQueue
{
    private static readonly (int X, int Y, int Z)[] Neighborhood =
    [
        (0, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    private readonly HashSet<WorldVoxelCoord> _pending = [];

    public int Count => _pending.Count;

    public bool HasWork => _pending.Count > 0;

    public void Enqueue(WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            _pending.Add(position);
        }
    }

    public void EnqueueNeighborhood(
        WorldVoxelCoord position)
    {
        foreach (var offset in Neighborhood)
        {
            Enqueue(position + offset);
        }
    }

    public FluidUpdateBatch Drain()
    {
        var positions = _pending
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X)
            .ToArray();

        _pending.Clear();
        return new FluidUpdateBatch(positions);
    }

    public void Requeue(
        IEnumerable<WorldVoxelCoord> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        foreach (var position in positions)
        {
            Enqueue(position);
        }
    }
}

public sealed record FluidUpdateBatch(
    IReadOnlyList<WorldVoxelCoord> Positions)
{
    public bool IsEmpty => Positions.Count == 0;
}
