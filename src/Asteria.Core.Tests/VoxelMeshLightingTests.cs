using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelMeshLightingTests
{
    [Fact]
    public void DenseLightingGridExactlyMatchesDirectCornerLightAndAo()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone", lightDampening: 15),
        ]);
        var world = new VoxelWorld();
        var chunk = new Chunk();
        var stone = blocks.GetId("asteria:stone");
        // Includes a partial outline, an opaque emitter-free obstruction,
        // and mixed RGB/skylight across chunk and meshlet boundaries.
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
        {
            if ((x + z) % 3 == 0)
                chunk.SetBlock(x, 3, z, stone);
            for (var y = 0; y < Chunk.Size; y++)
                chunk.SetLight(x, y, z,
                    new VoxelLight((byte)(y % 16),
                        (byte)(x % 16), (byte)(z % 16),
                        (byte)((x + y + z) % 16)));
        }
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var grid = VoxelMeshLighting.SamplingGrid.Capture(
            world, blocks, ChunkCoord.Zero, meshletIndex: 0);

        foreach (var y in new[] { 0, 3, 7 })
        foreach (var z in new[] { 0, 2, 7 })
        foreach (var x in new[] { 0, 4, 7 })
        foreach (var face in Enum.GetValues<BlockFace>())
        {
            var position = new WorldVoxelCoord(x, y, z);
            Assert.Equal(
                VoxelMeshLighting.SampleFace(world, blocks, position, face),
                grid.SampleFace(position, face));
        }
    }

    [Fact]
    public void TwoSolidSideNeighborsProduceStrongCornerOcclusion()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var chunk = new Chunk();

        chunk.SetBlock(8, 8, 8, stone);
        chunk.SetBlock(7, 9, 8, stone);
        chunk.SetBlock(8, 9, 9, stone);

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));
        var lighting = VoxelMeshLighting.SampleFace(
            chunk,
            blocks,
            8,
            8,
            8,
            BlockFace.Top);

        Assert.Contains(
            new[]
            {
                lighting.Corner0.AmbientOcclusion,
                lighting.Corner1.AmbientOcclusion,
                lighting.Corner2.AmbientOcclusion,
                lighting.Corner3.AmbientOcclusion,
            },
            value => value <= 0.58f + 0.001f);
    }

    [Fact]
    public void MicroblockOccupancyContributesFractionalAo()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone", tags: ["fragmentable"]),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, stone);
        chunk.SetBlock(2, 2, 1, stone);

        var half = MicroblockMask.Empty;
        for (var z = 0; z < 4; z++)
        {
            for (var y = 0; y < MicroblockMask.Edge; y++)
            {
                for (var x = 0; x < MicroblockMask.Edge; x++)
                {
                    half = half.Edit(
                        x,
                        y,
                        z,
                        MicroblockResolution.ExtraThin,
                        occupied: true);
                }
            }
        }

        chunk.SetMicroblockMask(2, 2, 1, half);

        Assert.Equal(
            0.5f,
            VoxelMeshLighting.OccupancyFraction(chunk, blocks, 2, 2, 1),
            3);
    }
    [Fact]
    public void LayerDampeningScalesWithAuthoredThickness()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:layer",
                    shape:
                        BlockShapeDefinition.CenteredLayer(
                            0.25f),
                    lightDampening: 15),
            ]);
        var layer =
            blocks.GetId("asteria:layer");
        var chunk = new Chunk();

        chunk.SetBlock(
            4,
            4,
            4,
            layer);

        Assert.Equal(
            (byte)4,
            VoxelLightingMedium.Dampening(
                chunk,
                blocks,
                new FluidRegistry([]),
                4,
                4,
                4));
    }

    [Fact]
    public void MicroblockDampeningScalesWithOccupiedVolume()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone",
                    tags: ["fragmentable"],
                    lightDampening: 15),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        var half =
            MicroblockMask.Empty;

        chunk.SetBlock(
            4,
            4,
            4,
            stone);

        for (var z = 0;
             z < MicroblockMask.Edge / 2;
             z++)
        {
            for (var y = 0;
                 y < MicroblockMask.Edge;
                 y++)
            {
                for (var x = 0;
                     x < MicroblockMask.Edge;
                     x++)
                {
                    half =
                        half.Edit(
                            x,
                            y,
                            z,
                            MicroblockResolution.ExtraThin,
                            occupied: true);
                }
            }
        }

        Assert.True(
            chunk.SetMicroblockMask(
                4,
                4,
                4,
                half));

        Assert.Equal(
            (byte)8,
            VoxelLightingMedium.Dampening(
                chunk,
                blocks,
                new FluidRegistry([]),
                4,
                4,
                4));
    }

    [Fact]
    public void TranslucentBlockWithZeroDampeningAllowsColoredLightThrough()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:lamp",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            0,
                            0)),
                new BlockDefinition(
                    "asteria:glass",
                    renderMode:
                        BlockRenderMode.Translucent,
                    lightDampening: 0),
            ]);
        var chunk = new Chunk();

        chunk.SetBlock(
            6,
            8,
            8,
            blocks.GetId("asteria:lamp"));
        chunk.SetBlock(
            7,
            8,
            8,
            blocks.GetId("asteria:glass"));

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));

        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                7,
                8,
                8).Red);
        Assert.Equal(
            (byte)13,
            chunk.GetLight(
                8,
                8,
                8).Red);
    }

}
