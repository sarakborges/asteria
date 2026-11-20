namespace Asteria.Core.World;

/// <summary>
/// Resolves the shared background-work capacity from available processors,
/// following Minecraft Java's CPU-minus-one policy with a configurable ceiling.
/// </summary>
public static class WorldWorkerCapacity
{
    public const int DefaultMaximumBackgroundThreads = 255;

    public static int Resolve(
        int processorCount,
        int maximumBackgroundThreads = DefaultMaximumBackgroundThreads)
    {
        if (processorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(processorCount));
        if (maximumBackgroundThreads <= 0 ||
            maximumBackgroundThreads > DefaultMaximumBackgroundThreads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBackgroundThreads));
        }

        return Math.Min(
            Math.Max(1, processorCount - 1),
            maximumBackgroundThreads);
    }
}
