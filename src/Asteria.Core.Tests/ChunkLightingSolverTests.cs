using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkLightingSolverTests
{
    [Fact]
    public void EmptyChunkInitializesToFullDirectSkylight()
    {
        var chunk =
            new Chunk();

        ChunkLightingSolver.Initialize(
            chunk,
            new BlockRegistry([]),
            new FluidRegistry([]));

        Assert.Equal(
            VoxelLight.MaxLevel,
            chunk.GetLight(
                    0,
                    0,
                    0)
                .Sky);
        Assert.Equal(
            VoxelLight.MaxLevel,
            chunk.GetLight(
                    Chunk.Size - 1,
                    Chunk.Size - 1,
                    Chunk.Size - 1)
                .Sky);
    }

    [Fact]
    public void FullyOpaqueChunkKeepsDirectLightWithoutPropagation()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone", lightDampening: 15),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var chunk = new Chunk();

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                for (var y = 0; y < Chunk.Size; y++)
                    chunk.SetBlock(x, y, z, stone);
            }
        }

        ChunkLightingSolver.Initialize(
            chunk, blocks, new FluidRegistry([]));

        Assert.Equal(Chunk.Volume, chunk.NonEmptyVoxelCount);
        Assert.Equal((byte)0, chunk.GetLight(0, 0, 0).Sky);
        Assert.Equal((byte)0, chunk.GetLight(8, 15, 8).Sky);
        Assert.Equal((byte)0, chunk.GetLight(15, 7, 15).Sky);

        // Reopening a skylight path still uses the original flood fill.
        chunk.SetBlock(8, 15, 8, BlockRuntimeId.Air);
        ChunkLightingSolver.Initialize(
            chunk, blocks, new FluidRegistry([]));
        Assert.Equal(VoxelLight.MaxLevel, chunk.GetLight(8, 15, 8).Sky);
    }

    [Fact]
    public void OpaqueBlockStopsDirectSkylightButAllowsLateralBounceAroundIt()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone", lightDampening: 15),
        ]);
        var stone = blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        chunk.SetBlock(4, 10, 4, stone);

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));

        Assert.Equal((byte)15, chunk.GetLight(4, 11, 4).Sky);
        Assert.Equal((byte)0, chunk.GetLight(4, 10, 4).Sky);
        Assert.InRange(chunk.GetLight(4, 9, 4).Sky, (byte)1, (byte)14);
        Assert.True(chunk.GetLight(5, 9, 4).Sky > 0);
    }

    [Fact]
    public void ColoredBlockEmissionPropagatesWithoutWhitening()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:red_lamp",
                lightDampening: 15,
                lightEmission: new BlockLightEmission(15, 2, 0)),
        ]);
        var lamp = blocks.GetId("asteria:red_lamp");
        var chunk = new Chunk();

        chunk.SetBlock(7, 8, 7, lamp);

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));

        var source = chunk.GetLight(7, 8, 7);
        var neighbor = chunk.GetLight(8, 8, 7);

        Assert.Equal((byte)15, source.Red);
        Assert.Equal((byte)2, source.Green);
        Assert.Equal((byte)0, source.Blue);
        Assert.Equal((byte)14, neighbor.Red);
        Assert.Equal((byte)1, neighbor.Green);
        Assert.Equal((byte)0, neighbor.Blue);
    }

    [Fact]
    public void PartialLayerScalesLightDampeningByOccupiedVolume()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:sand", lightDampening: 15),
            new BlockDefinition(
                "asteria:sand_layer",
                shape: BlockShapeDefinition.SurfaceLayer(1f / 8f, "asteria:sand"),
                lightDampening: 15),
        ]);
        var layer = blocks.GetId("asteria:sand_layer");
        var chunk = new Chunk();
        chunk.SetBlock(2, 12, 2, layer);

        Assert.Equal(
            (byte)2,
            ChunkLightingSolver.MediumDampening(
                chunk,
                blocks,
                new FluidRegistry([]),
                2,
                12,
                2));
    }
}
