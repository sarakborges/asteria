using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldWorkerCapacityTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(8, 7)]
    [InlineData(256, 255)]
    public void CapacityUsesAvailableProcessorsMinusOne(
        int processorCount,
        int expected)
    {
        Assert.Equal(expected, WorldWorkerCapacity.Resolve(processorCount));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(8, 3, 3)]
    [InlineData(4, 255, 3)]
    [InlineData(512, 12, 12)]
    public void ConfiguredMaximumCapsProcessorDerivedCapacity(
        int processorCount,
        int maximumBackgroundThreads,
        int expected)
    {
        Assert.Equal(
            expected,
            WorldWorkerCapacity.Resolve(
                processorCount, maximumBackgroundThreads));
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(-1, 255)]
    [InlineData(4, 0)]
    [InlineData(4, -1)]
    [InlineData(4, 256)]
    public void InvalidCapacityInputsAreRejected(
        int processorCount,
        int maximumBackgroundThreads)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldWorkerCapacity.Resolve(
                processorCount, maximumBackgroundThreads));
    }
}
