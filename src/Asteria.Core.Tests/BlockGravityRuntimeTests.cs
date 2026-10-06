using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockGravityRuntimeTests
{
    [Fact]
    public void VoxelEditWakesChangedVoxelAndBlockAboveOnce()
    {
        var queue =
            new BlockGravityUpdateQueue();
        var position =
            new WorldVoxelCoord(3, 7, -2);

        queue.EnqueueVoxelEdit(position);
        queue.EnqueueVoxelEdit(position);

        Assert.Equal(
            new[]
            {
                position,
                position + (0, 1, 0),
            },
            queue.DrainBatch());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void UnsupportedGravityBlockBecomesContinuousFallingState()
    {
        var fixture = CreateFixture();
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                sand,
                out _));

        var started =
            fixture.Gravity.ProcessWakeups();

        Assert.Equal(1, started);
        Assert.True(
            fixture.World
                .GetCellOrEmpty(position)
                .IsEmpty);

        var falling =
            Assert.Single(
                fixture.Gravity.ActiveBlocks);

        Assert.Equal(
            sand,
            falling.Cell.Block);
        Assert.Equal(
            4.5,
            falling.CenterY);
    }

    [Fact]
    public void FallingBlockMovesContinuouslyAndSettlesAboveSupport()
    {
        var fixture = CreateFixture();
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var support =
            new WorldVoxelCoord(3, 0, 3);
        var start =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                support,
                stone,
                out _));
        Assert.True(
            fixture.Mutations.SetBlockAt(
                start,
                sand,
                out _));

        fixture.Gravity.ProcessWakeups();

        var previousY =
            Assert.Single(
                fixture.Gravity.ActiveBlocks)
                .CenterY;

        fixture.Gravity.Advance(
            1.0 / 60.0,
            18.0);

        var movedY =
            Assert.Single(
                fixture.Gravity.ActiveBlocks)
                .CenterY;

        Assert.True(movedY < previousY);
        Assert.True(movedY > 1.5);

        for (var frame = 0;
             frame < 240 &&
             fixture.Gravity.ActiveCount > 0;
             frame++)
        {
            fixture.Gravity.Advance(
                1.0 / 60.0,
                18.0);
        }

        Assert.Equal(
            0,
            fixture.Gravity.ActiveCount);
        Assert.Equal(
            sand,
            fixture.World
                .GetCellOrEmpty(
                    new WorldVoxelCoord(
                        3,
                        1,
                        3))
                .Block);
    }

    [Fact]
    public void NonGravityBlockDoesNotStartFalling()
    {
        var fixture = CreateFixture();
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                stone,
                out _));

        Assert.Equal(
            0,
            fixture.Gravity.ProcessWakeups());
        Assert.Equal(
            0,
            fixture.Gravity.ActiveCount);
        Assert.Equal(
            stone,
            fixture.World
                .GetCellOrEmpty(position)
                .Block);
    }

    private static Fixture CreateFixture()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockGravityRuntime.GravityTag,
                    ]),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var gravityUpdates =
            new BlockGravityUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                gravityUpdates,
                terrainRevisions,
                fluidRevisions);
        var gravity =
            new BlockGravityRuntime(
                world,
                blocks,
                mutations,
                gravityUpdates);

        return new Fixture(
            world,
            blocks,
            mutations,
            gravity);
    }

    private sealed record Fixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        BlockGravityRuntime Gravity);
}
