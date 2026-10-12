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
        var grass =
            blocks.GetId(
                TestChunkFactory.GrassPlantId);
        var grassBlock =
            blocks.GetId(
                TestChunkFactory.GrassId);
        var mud =
            blocks.GetId(
                TestChunkFactory.MudId);
        var firstPlants = CollectPlants(first, grass);
        var secondPlants = CollectPlants(second, grass);
        var firstMushrooms = CollectMushrooms(first);
        var secondMushrooms = CollectMushrooms(second);

        Assert.Equal(
            firstPlants,
            secondPlants);
        Assert.Contains(
            firstPlants,
            entry =>
                entry.Block == grass);
        Assert.Equal(firstMushrooms, secondMushrooms);
        Assert.NotEmpty(firstMushrooms);

        foreach (var plant in firstPlants)
        {
            Assert.True(
                plant.Y > 0);

            var support =
                first.GetCell(
                    plant.X,
                    plant.Y - 1,
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

    private static (
        int X,
        int Y,
        int Z,
        BlockRuntimeId Block)[]
        CollectPlants(
            Chunk chunk,
            BlockRuntimeId grass)
    {
        var result =
            new List<(
                int X,
                int Y,
                int Z,
                BlockRuntimeId Block)>();

        chunk.VisitBlockCells(
            (x, y, z, cell) =>
            {
                if (cell.Block == grass)
                {
                    result.Add(
                        (
                            x,
                            y,
                            z,
                            cell.Block));
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
