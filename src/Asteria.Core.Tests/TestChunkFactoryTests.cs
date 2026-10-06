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
        var mushroom =
            blocks.GetId(
                TestChunkFactory.BrownMushroomId);
        var grassBlock =
            blocks.GetId(
                TestChunkFactory.GrassId);
        var mud =
            blocks.GetId(
                TestChunkFactory.MudId);
        var firstPlants =
            CollectPlants(
                first,
                grass,
                mushroom);
        var secondPlants =
            CollectPlants(
                second,
                grass,
                mushroom);

        Assert.Equal(
            firstPlants,
            secondPlants);
        Assert.Contains(
            firstPlants,
            entry =>
                entry.Block == grass);
        Assert.Contains(
            firstPlants,
            entry =>
                entry.Block == mushroom);

        foreach (var plant in firstPlants)
        {
            Assert.True(
                plant.Y > 0);

            var support =
                first.GetCell(
                    plant.X,
                    plant.Y - 1,
                    plant.Z).Block;

            Assert.Equal(
                plant.Block == grass
                    ? grassBlock
                    : mud,
                support);
        }
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
            BlockRuntimeId grass,
            BlockRuntimeId mushroom)
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
                if (cell.Block == grass ||
                    cell.Block == mushroom)
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
}
