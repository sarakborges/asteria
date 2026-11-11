using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernPaletteStreamingDivergenceTests
{
    private const ulong Seed = 181960897289965UL;

    private static readonly (WorldVoxelCoord Position, string BaselineBlock)[] DivergentVoxels =
    [
        (new WorldVoxelCoord(156, 56, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 57, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 58, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 59, -35), "asteria:stone"),
        (new WorldVoxelCoord(159, 59, -34), "asteria:stone"),
        (new WorldVoxelCoord(156, 60, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 60, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -33), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -33), "asteria:cobble_stone"),
    ];

    [Fact]
    public void StreamingBenchmarkSeedReproducesAllTwelveCavernPaletteTransitionsOnNeighborArrival()
    {
        var dataRoot = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data");

        static IEnumerable<string> Documents(string root, string kind) =>
            Directory.EnumerateFiles(Path.Combine(root, kind), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);

        static IEnumerable<string> OptionalDocuments(string root, string kind)
        {
            var directory = Path.Combine(root, kind);
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.json")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(File.ReadAllText)
                : Array.Empty<string>();
        }

        var blocks = BlockRegistry.FromJson(Documents(dataRoot, "blocks"));
        var fluids = FluidRegistry.FromJson(Documents(dataRoot, "fluids"));
        var biomes = new BiomeRegistry(
            Documents(dataRoot, "biomes").Select(BiomeDefinitionJson.Parse));
        var structures = StructureRegistry.FromJson(
            OptionalDocuments(dataRoot, "structures"));
        var structureSets = StructureSetRegistry.FromJson(
            OptionalDocuments(dataRoot, "structure_sets"));
        var dimensions = DimensionRegistry.FromJson(
            Documents(dataRoot, "dimensions"));
        var dimension = dimensions.Get(DimensionId.Overworld);
        var dimensionSeed = DimensionSeed.Derive(Seed, dimension.Id);
        Assert.Equal(14813712133479341624UL, dimensionSeed);

        var generator = new BiomeWorldGenerator(
            dimensionSeed,
            dimension,
            blocks,
            fluids,
            biomes,
            structures,
            structureSets);

        var sourceCoord = new ChunkCoord(9, 3, -3);
        var sourceChunk = generator.Materialize(sourceCoord);
        var world = new VoxelWorld();
        world.InsertChunk(sourceCoord, sourceChunk);

        // Every target has its baseline block before the adjacent +Z chunk
        // arrives. The arrival-time reconciliation introduces the 12 deltas.
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            Assert.Equal(
                blocks.GetId(baselineBlock),
                sourceChunk.GetBlock(local.X, local.Y, local.Z));
        }

        var arrivingNeighbor = new ChunkCoord(9, 3, -2);
        world.InsertChunk(
            arrivingNeighbor,
            generator.Materialize(arrivingNeighbor));

        var changedPositions = generator.ReconcileChunkArrival(
            world, arrivingNeighbor).ToHashSet();
        var expectedPositions = DivergentVoxels
            .Select(voxel => voxel.Position)
            .ToHashSet();

        Assert.Equal(DivergentVoxels.Length, changedPositions.Count);
        Assert.True(
            expectedPositions.SetEquals(changedPositions),
            "Neighbor arrival must change exactly the twelve voxels identified by the streaming A/B artifact.");

        var gravel = blocks.GetId("asteria:gravel");
        var transitions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var current = sourceChunk.GetBlock(local.X, local.Y, local.Z);
            Assert.Equal(gravel, current);
            transitions[baselineBlock] =
                transitions.TryGetValue(baselineBlock, out var count)
                    ? count + 1
                    : 1;
        }

        Assert.Equal(5, transitions["asteria:stone"]);
        Assert.Equal(7, transitions["asteria:cobble_stone"]);
    }
}
