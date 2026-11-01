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
    private const int BackloggedLightingSeeds = 8192;
    private const int BackloggedPhysicsWakeups = 8192;
    private const double SlowFrameSeconds = 1.0 / 55.0;

    public static WorldStreamingWorkPressure Evaluate(
        double frameSeconds,
        int dirtyTerrainChunks,
        bool loadingWorld,
        int pendingLightingSeeds = 0,
        int pendingPhysicsWakeups = 0)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        if (dirtyTerrainChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(dirtyTerrainChunks));
        if (pendingLightingSeeds < 0)
            throw new ArgumentOutOfRangeException(nameof(pendingLightingSeeds));
        if (pendingPhysicsWakeups < 0)
            throw new ArgumentOutOfRangeException(nameof(pendingPhysicsWakeups));

        return new WorldStreamingWorkPressure(
            !loadingWorld &&
            (dirtyTerrainChunks >= BackloggedTerrainChunks ||
             pendingLightingSeeds >= BackloggedLightingSeeds ||
             pendingPhysicsWakeups >= BackloggedPhysicsWakeups ||
             (frameSeconds > SlowFrameSeconds &&
              dirtyTerrainChunks >= SlowFrameTerrainChunks)));
    }
}
