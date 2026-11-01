using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldStreamingWorkPressureTests
{
    [Theory]
    [InlineData(1.0 / 60.0, 63, false)]
    [InlineData(1.0 / 40.0, 15, false)]
    [InlineData(1.0 / 60.0, 64, true)]
    [InlineData(1.0 / 40.0, 16, true)]
    public void PressureRequiresAMeshBacklogAndUsesFrameTime(
        double frameSeconds, int terrainChunks, bool expected)
    {
        Assert.Equal(expected,
            WorldStreamingWorkPressure.Evaluate(
                frameSeconds, terrainChunks, loadingWorld: false).IsPressured);
    }

    [Theory]
    [InlineData(8191, false)]
    [InlineData(8192, true)]
    public void LightingBacklogDefersDistantStreaming(
        int pendingSeeds, bool expected)
    {
        Assert.Equal(expected, WorldStreamingWorkPressure.Evaluate(
            1.0 / 60.0, dirtyTerrainChunks: 0, loadingWorld: false,
            pendingLightingSeeds: pendingSeeds).IsPressured);
    }

    [Theory]
    [InlineData(8191, false)]
    [InlineData(8192, true)]
    public void PhysicsBacklogDefersDistantStreaming(
        int pendingWakeups, bool expected)
    {
        Assert.Equal(expected, WorldStreamingWorkPressure.Evaluate(
            1.0 / 60.0, dirtyTerrainChunks: 0, loadingWorld: false,
            pendingPhysicsWakeups: pendingWakeups).IsPressured);
    }

    [Fact]
    public void InitialLoadingNeverThrottlesWorldMaterialization()
    {
        Assert.False(WorldStreamingWorkPressure.Evaluate(
            0.1, 1000, loadingWorld: true,
            pendingLightingSeeds: 100_000,
            pendingPhysicsWakeups: 100_000).IsPressured);
    }

    [Fact]
    public void InvalidPressureInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldStreamingWorkPressure.Evaluate(double.NaN, 16, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldStreamingWorkPressure.Evaluate(0.016, -1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldStreamingWorkPressure.Evaluate(
                0.016, 0, false, pendingLightingSeeds: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldStreamingWorkPressure.Evaluate(
                0.016, 0, false, pendingPhysicsWakeups: -1));
    }
}
