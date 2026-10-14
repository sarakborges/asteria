namespace Asteria.Core.World;

/// <summary>
/// Derived, frame-local load shedding policy. The streaming and terrain
/// schedulers retain ownership of their queues and worker lifecycles.
/// Initial world loading is never throttled.
/// </summary>
public readonly record struct WorldStreamingWorkPressure(bool IsPressured)
{
    private const int BackloggedTerrainChunks = 64;
    private const int SlowFrameTerrainChunks = 16;
    private const double SlowFrameSeconds = 1.0 / 55.0;

    public static WorldStreamingWorkPressure Evaluate(
        double frameSeconds, int dirtyTerrainChunks, bool loadingWorld)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        if (dirtyTerrainChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(dirtyTerrainChunks));

        return new WorldStreamingWorkPressure(
            !loadingWorld &&
            (dirtyTerrainChunks >= BackloggedTerrainChunks ||
             (frameSeconds > SlowFrameSeconds &&
              dirtyTerrainChunks >= SlowFrameTerrainChunks)));
    }
}
