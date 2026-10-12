using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TestChunkFactoryTests
{
    [Fact]
    public void FixtureSeedsSupportedGroundPlantsDeterministically()
    {
        var blocks =
            LoadBaseBlocks();
        var first =
            TestChunkFactory.Create(
                blocks).Chunk;
        var second =
            TestChunkFactory.Create(
                blocks).Chunk;
        var grassBlock =
            blocks.GetId(
                TestChunkFactory.GrassId);
        var mud =
            blocks.GetId(
                TestChunkFactory.MudId);
        var firstPlants = CollectPlants(first);
        var secondPlants = CollectPlants(second);
        var firstMushrooms = CollectMushrooms(first);
        var secondMushrooms = CollectMushrooms(second);

        Assert.Equal(
            firstPlants,
            secondPlants);
        Assert.NotEmpty(firstPlants);
        Assert.Equal(firstMushrooms, secondMushrooms);
        Assert.NotEmpty(firstMushrooms);

        foreach (var plant in firstPlants)
        {
            Assert.True(
                plant.Y > 0);

            var support =
                first.GetCell(
                    plant.X,
                    plant.Y,
                    plant.Z).Block;

            Assert.Equal(grassBlock, support);
        }
        foreach (var mushroom in firstMushrooms)
            Assert.Equal(mud, first.GetBlock(mushroom.X, mushroom.Y, mushroom.Z));
    }

    private static BlockRegistry LoadBaseBlocks()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "blocks");
        var documents =
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText);

        return BlockRegistry.FromJson(
            documents);
    }

    private static (int X, int Y, int Z)[] CollectPlants(Chunk chunk)
    {
        var result = new List<(int X, int Y, int Z)>();
        chunk.VisitObjectCells((x, y, z, obj) =>
        {
            if (obj.Id == TestChunkFactory.GrassPlantId)
            {
                Assert.Equal(BlockFace.Top, obj.Face);
                result.Add((x, y, z));
            }
        });
        return result.ToArray();
    }

    private static (int X, int Y, int Z)[] CollectMushrooms(Chunk chunk)
    {
        var result = new List<(int X, int Y, int Z)>();
        chunk.VisitObjectCells((x, y, z, obj) =>
        {
            if (obj.Id == TestChunkFactory.BrownMushroomId)
            {
                Assert.Equal(BlockFace.Top, obj.Face);
                result.Add((x, y, z));
            }
        });
        return result.ToArray();
    }
}
