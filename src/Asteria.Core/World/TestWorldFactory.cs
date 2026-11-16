namespace Asteria.Core.World;

public sealed record TestWorldFixture(
    BlockRegistry Blocks,
    VoxelWorld World);

public static class TestWorldFactory
{
    public static TestWorldFixture Create(BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var world = new VoxelWorld();

        for (var chunkY = 0; chunkY <= 1; chunkY++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                for (var chunkX = -1; chunkX <= 1; chunkX++)
                {
                    var coord =
                        new ChunkCoord(
                            chunkX,
                            chunkY,
                            chunkZ);
                    world.InsertChunk(
                        coord,
                        DeterministicChunkProvider.Materialize(
                            blocks,
                            coord));
                }
            }
        }

        return new TestWorldFixture(blocks, world);
    }
}
