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

public sealed class ChunkColumnCoordTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 12, 0, 0)]
    [InlineData(-3, 0, 7, 8)]
    [InlineData(-3, 31, 7, -16)]
    public void SectionYDoesNotAffectColumnIdentity(
        int chunkX,
        int firstSectionY,
        int chunkZ,
        int secondSectionY)
    {
        var first = ChunkColumnCoord.FromSection(
            new ChunkCoord(chunkX, firstSectionY, chunkZ));
        var second = ChunkColumnCoord.FromSection(
            new ChunkCoord(chunkX, secondSectionY, chunkZ));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ColumnConvertsBackToTheRequestedVerticalSection()
    {
        var column = new ChunkColumnCoord(-8, 13);

        Assert.Equal(
            new ChunkCoord(-8, 0, 13),
            column.ToSection(0));
        Assert.Equal(
            new ChunkCoord(-8, 27, 13),
            column.ToSection(27));
    }

    [Fact]
    public void ColumnOrderingIsDeterministicByXThenZ()
    {
        var columns = new[]
        {
            new ChunkColumnCoord(1, 2),
            new ChunkColumnCoord(-1, 9),
            new ChunkColumnCoord(0, 3),
            new ChunkColumnCoord(0, -4),
            new ChunkColumnCoord(1, -2),
        };

        Assert.Equal(
            new[]
            {
                new ChunkColumnCoord(-1, 9),
                new ChunkColumnCoord(0, -4),
                new ChunkColumnCoord(0, 3),
                new ChunkColumnCoord(1, -2),
                new ChunkColumnCoord(1, 2),
            },
            columns.OrderBy(column => column).ToArray());
    }
}
