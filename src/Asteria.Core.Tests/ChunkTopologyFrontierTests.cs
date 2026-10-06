using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkTopologyFrontierTests
{
    [Fact]
    public void AddedChunkInvalidatesOnlyContentRelevantNeighborMeshes()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var sourceCoord =
            ChunkCoord.Zero;
        var neighborCoord =
            new ChunkCoord(
                1,
                0,
                0);
        var world =
            new VoxelWorld();
        var source =
            new Chunk();
        var neighbor =
            new Chunk();

        source.SetBlock(
            Chunk.Size - 1,
            3,
            3,
            blocks.GetId(
                "asteria:stone"));
        neighbor.SetBlock(
            0,
            3,
            3,
            blocks.GetId(
                "asteria:stone"));
        world.InsertChunk(
            sourceCoord,
            source);
        world.InsertChunk(
            neighborCoord,
            neighbor);

        var invalidation =
            Assert.Single(
                ChunkTopologyFrontier
                    .PresentedNeighborMeshInvalidationsForAddition(
                        sourceCoord,
                        world,
                        coord =>
                            coord ==
                            neighborCoord));

        Assert.Equal(
            neighborCoord,
            invalidation.Neighbor);
        Assert.False(
            invalidation.Terrain.IsEmpty);
        Assert.True(
            invalidation.Fluid.IsEmpty);
    }

    [Fact]
    public void AddedEmptyChunkDoesNotInvalidatePresentedNeighbor()
    {
        var sourceCoord =
            ChunkCoord.Zero;
        var neighborCoord =
            new ChunkCoord(
                1,
                0,
                0);
        var world =
            new VoxelWorld();

        world.InsertChunk(
            sourceCoord,
            new Chunk());
        world.InsertChunk(
            neighborCoord,
            new Chunk());

        Assert.Empty(
            ChunkTopologyFrontier
                .PresentedNeighborMeshInvalidationsForAddition(
                    sourceCoord,
                    world,
                    coord =>
                        coord ==
                        neighborCoord));
    }
}
