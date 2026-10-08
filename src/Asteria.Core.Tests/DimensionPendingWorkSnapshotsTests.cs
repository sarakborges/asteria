using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DimensionPendingWorkSnapshotsTests
{
    [Fact]
    public void FluidScheduledTopologyAndDormantWorkSurviveQueueReplacement()
    {
        var queue = new FluidUpdateQueue();
        var fluid = new FluidRuntimeId(1);
        var topology = new WorldVoxelCoord(3, 18, -2);
        var duePosition = new WorldVoxelCoord(2, 10, -1);
        var dormantPosition = new WorldVoxelCoord(4, 20, 6);

        queue.EnqueueTopology(topology);
        queue.ScheduleAt(new FluidTickKey(fluid, duePosition), 145);
        queue.DeferUnloaded(new FluidTickKey(fluid, dormantPosition));
        var state = queue.CaptureState();

        Assert.Single(state.Topology);
        Assert.Single(state.Scheduled);
        Assert.Single(state.Dormant);

        var restored = new FluidUpdateQueue(state);
        Assert.Equal(state.Topology, restored.CaptureState().Topology);
        Assert.Equal(state.Scheduled, restored.CaptureState().Scheduled);
        Assert.Equal(state.Dormant, restored.CaptureState().Dormant);
        Assert.Equal(145UL, restored.NextDueTick);

        var first = restored.DrainReady(144, 10);
        Assert.Equal(topology, Assert.Single(first.TopologyPositions));
        Assert.Empty(first.DueTicks);
        restored.ReactivateLoadedChunk(
            VoxelCoordinates.FromWorld(
                dormantPosition.X, dormantPosition.Y, dormantPosition.Z).Chunk,
            144);
        var second = restored.DrainReady(144, 10);
        Assert.Equal(dormantPosition, Assert.Single(second.DueTicks).Position);
        var third = restored.DrainReady(145, 10);
        Assert.Equal(duePosition, Assert.Single(third.DueTicks).Position);
    }

    [Fact]
    public void BlockPhysicsWakeupsPreserveDeterministicFifoOrdering()
    {
        var queue = new BlockPhysicsUpdateQueue();
        var a = new WorldVoxelCoord(2, 6, 1);
        var b = new WorldVoxelCoord(-4, 7, 3);
        queue.Enqueue(a);
        queue.Enqueue(b);
        queue.Enqueue(a);
        var snapshot = queue.CaptureState();
        Assert.Equal(new[] { a, b }, snapshot.Positions);
        Assert.Equal(new[] { a, b },
            new BlockPhysicsUpdateQueue(snapshot).DrainBatch());
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void InvalidOrRepeatedWorkIsRejected()
    {
        var invalid = new WorldVoxelCoord(0, -1, 0);
        Assert.Throws<InvalidDataException>(() =>
            new BlockPhysicsUpdateQueueSnapshot([invalid]));
        var position = new WorldVoxelCoord(0, 5, 0);
        Assert.Throws<InvalidDataException>(() =>
            new BlockPhysicsUpdateQueueSnapshot([position, position]));

        var water = new FluidTickKey(new FluidRuntimeId(1), position);
        Assert.Throws<InvalidDataException>(() =>
            new FluidUpdateQueueSnapshot([], [
                new ScheduledFluidTick(water, 10),
                new ScheduledFluidTick(water, 12)
            ], []));
        Assert.Throws<InvalidDataException>(() =>
            new FluidUpdateQueueSnapshot([], [
                new ScheduledFluidTick(water, 10)
            ], [water]));
        Assert.Throws<InvalidDataException>(() =>
            new FluidUpdateQueueSnapshot([invalid], [], []));
    }

    [Fact]
    public void SphereSessionCaptureAndRestoreCarriesBothPendingQueues()
    {
        var definition = new DimensionDefinition(
            DimensionId.Overworld, ["asteria:overworld/plains"],
            90, 18f, new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f));
        var dimensions = new DimensionRegistry([definition]);
        var creation = new WorldCreationOptions("World", 105UL);
        var store = new DimensionSessionStateStore(creation, dimensions);
        var state = store.GetOrCreate(DimensionId.Overworld);
        var fluid = new FluidUpdateQueue();
        fluid.EnqueueTopology(new WorldVoxelCoord(1, 8, 1));
        fluid.ScheduleAt(new FluidTickKey(
            new FluidRuntimeId(1), new WorldVoxelCoord(1, 9, 1)), 9);
        state.PendingFluidWork = fluid.CaptureState();
        var physics = new BlockPhysicsUpdateQueue();
        physics.Enqueue(new WorldVoxelCoord(2, 10, 2));
        state.PendingPhysicsWork = physics.CaptureState();

        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([]);
        var dyes = new DyeRegistry([]);
        var layers = new AttachedLayerRegistry([]);
        var snapshot = GameplaySessionSaveCodec.Capture(
            store, blocks, fluids, dyes, layers);
        var restored = GameplaySessionSaveCodec.Restore(
            creation, dimensions, snapshot, blocks, fluids, dyes, layers);
        var other = restored.GetOrCreate(DimensionId.Overworld);
        Assert.NotNull(other.PendingFluidWork);
        Assert.NotNull(other.PendingPhysicsWork);
        Assert.Single(new FluidUpdateQueue(
            other.PendingFluidWork).CaptureState().Scheduled);
        Assert.Equal(1, new BlockPhysicsUpdateQueue(
            other.PendingPhysicsWork).Count);
    }
}
