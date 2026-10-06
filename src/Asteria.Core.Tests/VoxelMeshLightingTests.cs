using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelMeshLightingTests
{
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
}
