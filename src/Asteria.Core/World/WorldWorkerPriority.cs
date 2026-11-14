namespace Asteria.Core.World;

/// <summary>
/// Global executor ordering. Chunk-backed work preserves the complete ticket
/// tuple (band, horizontal distance, total distance and coordinates); other
/// world domains use semantic priority classes with stable FIFO ties.
/// </summary>
public readonly record struct WorldWorkerPriority(
    int PriorityBand,
    long HorizontalDistanceSquared,
    long TotalDistanceSquared,
    int CoordinateY,
    int CoordinateZ,
    int CoordinateX) : IComparable<WorldWorkerPriority>
{
    public static WorldWorkerPriority FromPriorityClass(int priorityClass) =>
        new(priorityClass, 0, 0, 0, 0, 0);

    public static WorldWorkerPriority FromStreaming(
        ChunkStreamingPriority priority) =>
        new(
            PriorityBandForTicketLevel(priority.StreamingBand),
            priority.HorizontalDistanceSquared,
            priority.TotalDistanceSquared,
            priority.Y,
            priority.Z,
            priority.X);

    public static WorldWorkerPriority FromGenerationTicket(
        ChunkGenerationTaskPriority priority) =>
        new(
            PriorityBandForTicketLevel(priority.TicketLevel),
            priority.HorizontalDistanceSquared,
            priority.TotalDistanceSquared,
            priority.CoordinateY,
            priority.CoordinateZ,
            priority.CoordinateX);

    private static int PriorityBandForTicketLevel(int ticketLevel) =>
        ticketLevel switch
        {
            0 => WorldWorkerPool.PlayerChunkPriority,
            1 => WorldWorkerPool.ForwardPrefetchPriority,
            2 => WorldWorkerPool.BackgroundChunkPriority,
            3 => WorldWorkerPool.RearChunkPriority,
            _ => WorldWorkerPool.BackgroundWorldPriority,
        };

    public int CompareTo(WorldWorkerPriority other)
    {
        var result = PriorityBand.CompareTo(other.PriorityBand);
        if (result != 0)
            return result;

        result = HorizontalDistanceSquared.CompareTo(
            other.HorizontalDistanceSquared);
        if (result != 0)
            return result;

        result = TotalDistanceSquared.CompareTo(
            other.TotalDistanceSquared);
        if (result != 0)
            return result;

        result = CoordinateY.CompareTo(other.CoordinateY);
        if (result != 0)
            return result;

        result = CoordinateZ.CompareTo(other.CoordinateZ);
        return result != 0
            ? result
            : CoordinateX.CompareTo(other.CoordinateX);
    }
}
