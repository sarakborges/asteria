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
    public void ArrivalSkipsMatchingHorizontalLightButKeepsVerticalAndRetirementSeeds()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        var previous = new Chunk();
        var arriving = new Chunk();
        ChunkLightingSolver.Initialize(previous, blocks, fluids);
        ChunkLightingSolver.Initialize(arriving, blocks, fluids);
        world.InsertChunk(ChunkCoord.Zero, previous);
        var neighborCoord = new ChunkCoord(1, 0, 0);
        world.InsertChunk(neighborCoord, arriving);

        var horizontal = new WorldVoxelCoord(16, 5, 5);
        Assert.Equal(world.GetLightOrDark(horizontal),
            world.GetLightOrDark(horizontal + (-1, 0, 0)));
        Assert.True(ChunkTopologyFrontier.NeedsSeamLightSeed(
            world, blocks, fluids, horizontal));
        Assert.False(ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
            world, blocks, fluids, neighborCoord, horizontal));

        // Direct-sky reconciliation is not optimized out at vertical edges.
        Assert.True(ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
            world, blocks, fluids, neighborCoord,
            new WorldVoxelCoord(16, 0, 5)));

        world.ArchiveChunk(neighborCoord);
        // On retirement the source is gone, so neighboring survivors
        // must be checked without assuming matching light.
        Assert.True(ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
            world, blocks, fluids, neighborCoord,
            new WorldVoxelCoord(15, 5, 5)));
    }

    [Fact]
    public void ArrivalReconcilesEmissiveLightAcrossHorizontalSeam()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        var previous = new Chunk();
        ChunkLightingSolver.Initialize(previous, blocks, fluids);
        world.InsertChunk(ChunkCoord.Zero, previous);

        var arrivingCoord = new ChunkCoord(1, 0, 0);
        var arriving = new Chunk();
        arriving.SetBlock(0, 5, 5, blocks.GetId("asteria:lamp"));
        ChunkLightingSolver.Initialize(arriving, blocks, fluids);
        world.InsertChunk(arrivingCoord, arriving);

        var filtered = ChunkTopologyFrontier.LightingSeeds(
                arrivingCoord, world.ContainsChunk)
            .Where(position =>
                ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
                    world, blocks, fluids, arrivingCoord, position))
            .ToArray();
        Assert.Contains(new WorldVoxelCoord(16, 5, 5), filtered);
        Assert.DoesNotContain(new WorldVoxelCoord(16, 14, 15), filtered);

        VoxelWorldLightingSolver.RelightAfterEdits(
            world, blocks, fluids, filtered);
        Assert.Equal((byte)14,
            world.GetLightOrDark(new WorldVoxelCoord(15, 5, 5)).Red);
    }

    [Fact]
    public void VerticalArrivalSkipsUnchangedFullSkylight()
    {
        var blocks = new BlockRegistry([]);
        var fluids = new FluidRegistry([]);
        var world = new VoxelWorld();
        var bottom = new Chunk();
        var top = new Chunk();
        ChunkLightingSolver.Initialize(bottom, blocks, fluids);
        ChunkLightingSolver.Initialize(top, blocks, fluids);
        world.InsertChunk(ChunkCoord.Zero, bottom);
        var arriving = new ChunkCoord(0, 1, 0);
        world.InsertChunk(arriving, top);

        Assert.Empty(ChunkTopologyFrontier.LightingSeeds(
                arriving, world.ContainsChunk)
            .Where(position => ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
                world, blocks, fluids, arriving, position)));
    }

    [Fact]
    public void VerticalArrivalSkipsTwoDarkOpaqueFaces()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([]);
        var stone = blocks.GetId("asteria:stone");
        var bottom = new Chunk();
        var top = new Chunk();
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
        {
            bottom.SetBlock(x, Chunk.Size - 1, z, stone);
            top.SetBlock(x, 0, z, stone);
        }
        ChunkLightingSolver.Initialize(bottom, blocks, fluids);
        ChunkLightingSolver.Initialize(top, blocks, fluids);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, bottom);
        var arriving = new ChunkCoord(0, 1, 0);
        world.InsertChunk(arriving, top);

        Assert.Empty(ChunkTopologyFrontier.LightingSeeds(
                arriving, world.ContainsChunk)
            .Where(position => ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
                world, blocks, fluids, arriving, position)));
    }

    [Fact]
    public void VerticalArrivalKeepsShadowCastingRoofSeeds()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([]);
        var bottom = new Chunk();
        var top = new Chunk();
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
            top.SetBlock(x, 0, z, blocks.GetId("asteria:stone"));
        ChunkLightingSolver.Initialize(bottom, blocks, fluids);
        ChunkLightingSolver.Initialize(top, blocks, fluids);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, bottom);
        var arriving = new ChunkCoord(0, 1, 0);
        world.InsertChunk(arriving, top);

        Assert.Equal(Chunk.Area, ChunkTopologyFrontier.LightingSeeds(
                arriving, world.ContainsChunk)
            .Count(position => ChunkTopologyFrontier.NeedsArrivalSeamLightSeed(
                world, blocks, fluids, arriving, position)));
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
    public void RetirementSkipsDarkHorizontalSeamsButKeepsEmittedLight()
    {
        var world = new VoxelWorld();
        var chunk = new Chunk();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var retired = new ChunkCoord(1, 0, 0);
        var position = new WorldVoxelCoord(Chunk.Size - 1, 5, 5);

        Assert.False(ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
            world, retired, position));

        chunk.SetLight(Chunk.Size - 1, 5, 5, new VoxelLight(0, 12, 0, 0));
        Assert.True(ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
            world, retired, position));
    }

    [Fact]
    public void RetirementRetainsPotentialSkylightOpeningBelowMissingRoof()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var top = new WorldVoxelCoord(5, Chunk.Size - 1, 5);

        // A dark lower column may brighten after removing its upper chunk.
        Assert.True(ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
            world, new ChunkCoord(0, 1, 0), top));

        // A fully sky-lit voxel without emissive RGB cannot change.
        world.GetChunk(ChunkCoord.Zero).SetLight(
            5, Chunk.Size - 1, 5, new VoxelLight(15, 0, 0, 0));
        Assert.False(ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
            world, new ChunkCoord(0, 1, 0), top));
    }

    [Fact]
    public void RetirementOfLowerChunkSkipsAlreadyDarkUpperSeams()
    {
        var world = new VoxelWorld();
        var upper = new ChunkCoord(0, 1, 0);
        world.InsertChunk(upper, new Chunk());
        var bottom = new WorldVoxelCoord(5, Chunk.Size, 5);

        Assert.False(ChunkTopologyFrontier.NeedsRetirementSeamLightSeed(
            world, ChunkCoord.Zero, bottom));
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
