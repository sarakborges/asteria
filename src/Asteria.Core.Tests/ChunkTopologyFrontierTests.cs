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
    [Fact]
    public void RetirementDoesNotRemeshAnEmptyPresentedNeighbor()
    {
        var world = new VoxelWorld();
        var neighbor = new ChunkCoord(1, 0, 0);
        world.InsertChunk(neighbor, new Chunk());

        Assert.Empty(
            ChunkTopologyFrontier.PresentedNeighborMeshInvalidationsForRetirement(
                ChunkCoord.Zero, world, coord => coord == neighbor));
    }

    [Fact]
    public void RetirementInvalidatesOnlyNeighborTerrainAtBoundary()
    {
        var world = new VoxelWorld();
        var neighbor = new ChunkCoord(1, 0, 0);
        var chunk = new Chunk();
        chunk.SetBlock(0, 5, 5, new BlockRuntimeId(1));
        world.InsertChunk(neighbor, chunk);

        var invalidation = Assert.Single(
            ChunkTopologyFrontier.PresentedNeighborMeshInvalidationsForRetirement(
                ChunkCoord.Zero, world, coord => coord == neighbor));

        Assert.Equal(neighbor, invalidation.Neighbor);
        Assert.False(invalidation.Terrain.IsEmpty);
        Assert.True(invalidation.Fluid.IsEmpty);

        chunk.SetBlock(0, 5, 5, BlockRuntimeId.Air);
        chunk.SetBlock(5, 5, 5, new BlockRuntimeId(1));
        Assert.Empty(
            ChunkTopologyFrontier.PresentedNeighborMeshInvalidationsForRetirement(
                ChunkCoord.Zero, world, coord => coord == neighbor));
    }

    [Fact]
    public void RetirementInvalidatesOnlyNeighborFluidAtBoundary()
    {
        var world = new VoxelWorld();
        var neighbor = new ChunkCoord(1, 0, 0);
        var chunk = new Chunk();
        chunk.SetFluid(0, 5, 5,
            FluidCell.Source(new FluidRuntimeId(1)));
        world.InsertChunk(neighbor, chunk);

        var invalidation = Assert.Single(
            ChunkTopologyFrontier.PresentedNeighborMeshInvalidationsForRetirement(
                ChunkCoord.Zero, world, coord => coord == neighbor));

        Assert.Equal(neighbor, invalidation.Neighbor);
        Assert.True(invalidation.Terrain.IsEmpty);
        Assert.False(invalidation.Fluid.IsEmpty);
    }

}
