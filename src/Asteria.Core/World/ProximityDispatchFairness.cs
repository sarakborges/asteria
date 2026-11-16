namespace Asteria.Core.World;

/// <summary>
/// Shared policy for proximity-first worker dispatch. After a fixed number
/// of successful nearby starts, one ordinary FIFO/coordinate-ordered start
/// is required so continuous local changes do not starve older work.
/// Queries never change state; only actual worker starts advance fairness.
/// </summary>
public sealed class ProximityDispatchFairness
{
    private const int MaximumConsecutiveNearbyStarts = 7;
    private int _consecutiveNearbyStarts;

    public bool ShouldPrioritizeNearby(bool hasStreamingCenter) =>
        hasStreamingCenter &&
        _consecutiveNearbyStarts < MaximumConsecutiveNearbyStarts;

    public void RecordStarted(bool prioritizedNearby)
    {
        _consecutiveNearbyStarts = prioritizedNearby
            ? Math.Min(_consecutiveNearbyStarts + 1, MaximumConsecutiveNearbyStarts)
            : 0;
    }
}
