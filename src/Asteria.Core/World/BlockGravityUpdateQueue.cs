namespace Asteria.Core.World;

public sealed class BlockGravityUpdateQueue
{
    private readonly Queue<WorldVoxelCoord> _queue = new();
    private readonly HashSet<WorldVoxelCoord> _queued = [];

    public int Count => _queue.Count;

    public void EnqueueVoxelEdit(
        WorldVoxelCoord position)
    {
        Enqueue(position);
        Enqueue(position + (0, 1, 0));
    }

    public void Enqueue(
        WorldVoxelCoord position)
    {
        if (position.Y < 0 ||
            !_queued.Add(position))
        {
            return;
        }

        _queue.Enqueue(position);
    }

    public IReadOnlyList<WorldVoxelCoord> DrainBatch()
    {
        var count = _queue.Count;
        var result =
            new List<WorldVoxelCoord>(count);

        for (var index = 0;
             index < count;
             index++)
        {
            var position = _queue.Dequeue();
            _queued.Remove(position);
            result.Add(position);
        }

        return result;
    }
}
