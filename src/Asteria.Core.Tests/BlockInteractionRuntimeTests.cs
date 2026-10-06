using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockInteractionRuntimeTests
{
    [Fact]
    public void BreakingBlockSpawnsPortableSelfDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:stone"));
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var position =
            new WorldVoxelCoord(2, 2, 2);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                stone,
                out _));

        var decision =
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.True(decision.Accepted);
        Assert.True(
            fixture.World
                .GetCellOrEmpty(position)
                .IsEmpty);

        var drop =
            Assert.Single(
                fixture.Dropped.ActiveBlocks);

        Assert.Equal(
            stone,
            drop.Block.Cell.Block);
        Assert.Equal(
            new System.Numerics.Vector3(
                2.5f,
                2.5f,
                2.5f),
            drop.Position);
    }

    [Fact]
    public void DropsSelfFalseBreaksWithoutSpawningDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:fixture",
                    dropsSelf: false));
        var block =
            fixture.Blocks.GetId(
                "asteria:fixture");
        var position =
            new WorldVoxelCoord(2, 2, 2);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                block,
                out _));

        var decision =
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.True(decision.Accepted);
        Assert.Equal(
            0,
            fixture.Dropped.ActiveCount);
    }

    [Fact]
    public void BreakingSculptedBlockCopiesMaskIntoDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:sculpted",
                    tags: ["fragmentable"]));
        var block =
            fixture.Blocks.GetId(
                "asteria:sculpted");
        var position =
            new WorldVoxelCoord(2, 2, 2);
        var mask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thin,
                occupied: false);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                block,
                out _));

        var address =
            VoxelCoordinates.FromWorld(
                position.X,
                position.Y,
                position.Z);
        Assert.True(
            fixture.World
                .GetChunk(address.Chunk)
                .SetMicroblockMask(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z,
                    mask));

        Assert.True(
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0))
                .Accepted);

        Assert.Equal(
            mask,
            Assert.Single(
                    fixture.Dropped.ActiveBlocks)
                .Block
                .MicroblockMask);
    }

    private static Fixture CreateFixture(
        BlockDefinition definition)
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var blocks =
            new BlockRegistry(
            [
                definition,
            ]);
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
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var interactions =
            new BlockInteractionRuntime(
                world,
                blocks,
                mutations,
                dropped);

        return new Fixture(
            world,
            blocks,
            mutations,
            dropped,
            interactions);
    }

    private sealed record Fixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Dropped,
        BlockInteractionRuntime Interactions);
}
