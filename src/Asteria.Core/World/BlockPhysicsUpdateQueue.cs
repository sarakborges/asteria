namespace Asteria.Core.World;

public sealed class BlockPhysicsUpdateQueue
{
    private readonly DeduplicatedQueue<WorldVoxelCoord>
        _queue = new();

    public int Count => _queue.Count;

    public void EnqueueVoxelEdit(WorldVoxelCoord position)
    {
        Enqueue(position);
        Enqueue(position + (0, 1, 0));
    }

    public void Enqueue(WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            _queue.Enqueue(position);
        }
    }

    public IReadOnlyList<WorldVoxelCoord> DrainBatch() =>
        _queue.Drain();
}
