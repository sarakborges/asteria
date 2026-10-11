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
        Assert.Equal(Chunk.Area, frontier.Length);
        Assert.DoesNotContain(
            new WorldVoxelCoord(Chunk.Size - 1, 32, 0),
            frontier);
        Assert.Contains(
            new WorldVoxelCoord(Chunk.Size, 32, 0),
            frontier);
        Assert.DoesNotContain(
            new WorldVoxelCoord(0, 32, 0),
            frontier);

        // Once the source is resident, only its own face is seeded.
        var loaded = ChunkTopologyFrontier.LightingSeeds(
            source,
            coord => coord == source || coord == neighbor).ToArray();
        Assert.Equal(Chunk.Area, loaded.Length);
        Assert.Contains(
            new WorldVoxelCoord(Chunk.Size - 1, 32, 0), loaded);
        Assert.DoesNotContain(
            new WorldVoxelCoord(Chunk.Size, 32, 0), loaded);
    }

    [Fact]
    public void DarkOpaqueSeedsAreSkippedWithoutSkippingEmittingOrVerticalSeeds()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:lamp",
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        var chunk = new Chunk();
        chunk.SetBlock(15, 5, 6, blocks.GetId("asteria:stone"));
        chunk.SetBlock(15, 6, 6, blocks.GetId("asteria:lamp"));
        chunk.SetBlock(15, 15, 6, blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, chunk);

        Assert.False(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, new WorldVoxelCoord(15, 5, 6)));
        Assert.True(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, new WorldVoxelCoord(15, 6, 6)));
        Assert.True(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, new WorldVoxelCoord(15, 15, 6)));
        Assert.True(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, new WorldVoxelCoord(16, 5, 6)));
        chunk.SetBlock(15, 5, 6, BlockRuntimeId.Air);
        Assert.True(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, new WorldVoxelCoord(15, 5, 6)));
    }

    [Fact]
    public void LightingFrontierReconcilesEmissionOnArrivalAndRetirement()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:red_lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        var source = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        var lampPosition = new WorldVoxelCoord(15, 5, 5);
        var neighborPosition = new WorldVoxelCoord(16, 5, 5);
        var first = new Chunk();
        first.SetBlock(15, 5, 5, blocks.GetId("asteria:red_lamp"));
        ChunkLightingSolver.Initialize(first, blocks, fluids);
        world.InsertChunk(source, first);

        var second = new Chunk();
        ChunkLightingSolver.Initialize(second, blocks, fluids);
        world.InsertChunk(neighbor, second);
        var arrivalSeeds = ChunkTopologyFrontier.LightingSeeds(
            neighbor, world.ContainsChunk).ToArray();
        Assert.Equal(Chunk.Area, arrivalSeeds.Length);

        VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, arrivalSeeds);
        Assert.Equal((byte)14, world.GetLightOrDark(neighborPosition).Red);
        Assert.Equal((byte)15, world.GetLightOrDark(lampPosition).Red);

        world.ArchiveChunk(source);
        var retirementSeeds = ChunkTopologyFrontier.LightingSeeds(
            source, world.ContainsChunk).ToArray();
        Assert.Equal(Chunk.Area, retirementSeeds.Length);
        VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, retirementSeeds);
        Assert.Equal((byte)0, world.GetLightOrDark(neighborPosition).Red);
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
