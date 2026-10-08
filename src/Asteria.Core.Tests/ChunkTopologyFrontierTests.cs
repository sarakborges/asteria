using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkTopologyFrontierTests
{
    [Fact]
    public void LightingFrontierOnlySeedsResidentNeighborFaces()
    {
        var source = new ChunkCoord(0, 2, 0);
        var neighbor = new ChunkCoord(1, 2, 0);
        var lone = ChunkTopologyFrontier.LightingSeeds(
            source,
            _ => false).ToArray();
        Assert.Empty(lone);

        var frontier = ChunkTopologyFrontier.LightingSeeds(
            source,
            coord => coord == neighbor).ToArray();
        Assert.Equal(Chunk.Area * 2, frontier.Length);
        Assert.Contains(
            new WorldVoxelCoord(Chunk.Size - 1, 32, 0),
            frontier);
        Assert.Contains(
            new WorldVoxelCoord(Chunk.Size, 32, 0),
            frontier);
        Assert.DoesNotContain(
            new WorldVoxelCoord(0, 32, 0),
            frontier);

        // Both sides of the face remain available after the source
        // chunk has been unloaded; residency filtering occurs at enqueue.
        Assert.Equal(
            Chunk.Area * 2,
            ChunkTopologyFrontier.LightingSeeds(
                source,
                coord => coord == neighbor).Count());
    }

    [Fact]
    public void LightingFrontierDoesNotSeedNegativeWorldY()
    {
        var bottom = ChunkTopologyFrontier.LightingSeeds(
            ChunkCoord.Zero,
            _ => true);
        Assert.DoesNotContain(bottom, position => position.Y < 0);
    }

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
