using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldLightingSolverTests
{
    [Fact]
    public void BlockLightPropagatesAcrossChunkBoundary()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:red_lamp",
                lightDampening: 0,
                lightEmission: new BlockLightEmission(15, 0, 0)),
        ]);
        var lamp = blocks.GetId("asteria:red_lamp");
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        world.SetBlockAt(
            new WorldVoxelCoord(15, 5, 5),
            lamp,
            out _);

        VoxelWorldLightingSolver.Initialize(world, blocks);

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                new WorldVoxelCoord(16, 5, 5)).Red);
    }
}
