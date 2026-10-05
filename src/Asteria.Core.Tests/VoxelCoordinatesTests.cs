using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelCoordinatesTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(15, 0, 15)]
    [InlineData(16, 1, 0)]
    [InlineData(-1, -1, 15)]
    [InlineData(-16, -1, 0)]
    [InlineData(-17, -2, 15)]
    public void WorldAxisUsesEuclideanChunkCoordinates(int worldX, int expectedChunkX, int expectedLocalX)
    {
        var address = VoxelCoordinates.FromWorld(worldX, 0, 0);

        Assert.Equal(expectedChunkX, address.Chunk.X);
        Assert.Equal(expectedLocalX, address.Local.X);

        var roundTrip = VoxelCoordinates.ToWorld(address.Chunk, address.Local);
        Assert.Equal(worldX, roundTrip.X);
    }
}
