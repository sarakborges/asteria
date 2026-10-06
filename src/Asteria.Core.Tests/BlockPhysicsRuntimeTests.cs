using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockPhysicsRuntimeTests
{
    [Fact]
    public void VoxelEditWakesChangedVoxelAndBlockAboveOnce()
    {
        var queue =
            new BlockPhysicsUpdateQueue();
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
    public void ResidentChunkSeedsExistingGravityBlocks()
    {
        var world = new VoxelWorld();
        var chunk = new Chunk();
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockPhysicsCapabilities.Gravity,
                    ]),
            ]);
        var sand =
            blocks.GetId(
                "asteria:sand");
        chunk.SetBlock(
            3,
            4,
            3,
            sand);
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates);

        Assert.Equal(
            1,
            physics.EnqueueResidentChunk(
                ChunkCoord.Zero));
        Assert.Equal(
            1,
            physicsUpdates.Count);
        Assert.Equal(
            1,
            physics.ProcessWakeups());
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
            fixture.Physics.ProcessWakeups();

        Assert.Equal(1, started);
        Assert.True(
            fixture.World
                .GetCellOrEmpty(position)
                .IsEmpty);

        var falling =
            Assert.Single(
                fixture.Physics.ActiveBlocks);

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

        fixture.Physics.ProcessWakeups();

        var previousY =
            Assert.Single(
                fixture.Physics.ActiveBlocks)
                .CenterY;

        fixture.Physics.Advance(
            1.0 / 60.0,
            18.0);

        var movedY =
            Assert.Single(
                fixture.Physics.ActiveBlocks)
                .CenterY;

        Assert.True(movedY < previousY);
        Assert.True(movedY > 1.5);

        for (var frame = 0;
             frame < 240 &&
             fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(
                1.0 / 60.0,
                18.0);
        }

        Assert.Equal(
            0,
            fixture.Physics.ActiveCount);
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
            fixture.Physics.ProcessWakeups());
        Assert.Equal(
            0,
            fixture.Physics.ActiveCount);
        Assert.Equal(
            stone,
            fixture.World
                .GetCellOrEmpty(position)
                .Block);
    }

    [Fact]
    public void UnsupportedSupportBelowBlockIsRemovedAndReported()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:snow_layer",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:snow")),
                new BlockDefinition("asteria:snow"),
            ]);
        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates);
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            mutations.SetBlockAt(
                position,
                blocks.GetId("asteria:snow_layer"),
                out _));

        var wake =
            physics.ProcessWakeups();

        var removal =
            Assert.Single(
                wake.UnsupportedRemovals);
        Assert.Equal(position, removal.Position);
        Assert.Equal(
            blocks.GetId("asteria:snow_layer"),
            removal.Cell.Block);
        Assert.True(
            world.GetCellOrEmpty(position).IsEmpty);
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
                        BlockPhysicsCapabilities.Gravity,
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
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
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
                physicsUpdates,
                terrainRevisions,
                fluidRevisions);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates);

        return new Fixture(
            world,
            blocks,
            mutations,
            physics);
    }

    private sealed record Fixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        BlockPhysicsRuntime Physics);
}
