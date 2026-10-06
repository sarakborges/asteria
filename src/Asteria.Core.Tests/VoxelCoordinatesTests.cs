using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelCoordinatesTests
{
    [Fact]
    public void VoxelHaloVisitsDeterministicNeighborRange()
    {
        var visited =
            new List<ChunkCoord>();

        VoxelCoordinates
            .VisitChunkCoordsWhoseVoxelHaloContains(
                new WorldVoxelCoord(
                    15,
                    31,
                    15),
                visited.Add);

        Assert.Equal(
            new[]
            {
                new ChunkCoord(0, 1, 0),
                new ChunkCoord(1, 1, 0),
                new ChunkCoord(0, 1, 1),
                new ChunkCoord(1, 1, 1),
                new ChunkCoord(0, 2, 0),
                new ChunkCoord(1, 2, 0),
                new ChunkCoord(0, 2, 1),
                new ChunkCoord(1, 2, 1),
            },
            visited);
    }

    [Fact]
    public void InteriorVoxelHaloVisitsOnlyContainingChunk()
    {
        var visited =
            new List<ChunkCoord>();

        VoxelCoordinates
            .VisitChunkCoordsWhoseVoxelHaloContains(
                new WorldVoxelCoord(
                    4,
                    20,
                    6),
                visited.Add);

        Assert.Equal(
            new[]
            {
                new ChunkCoord(0, 1, 0),
            },
            visited);
    }

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
