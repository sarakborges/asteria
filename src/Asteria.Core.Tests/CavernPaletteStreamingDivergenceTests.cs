using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernPaletteStreamingDivergenceTests
{
    private const ulong Seed = 181960897289965UL;
    private const string CavernBiomeId = "asteria:overworld/caverns";

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
    public void StreamingBenchmarkDivergenceIsAuthoredByTheCavernWallPalette()
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
        var chunkCoord = new ChunkCoord(9, 3, -3);
        var chunk = generator.Materialize(chunkCoord);
        var world = new VoxelWorld();
        world.InsertChunk(chunkCoord, chunk);

        // The baseline materials are present when this chunk first arrives.
        // The divergence is introduced only when its +Z neighbor becomes
        // resident and the arrival-time face reconciliation runs.
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            Assert.Equal(
                blocks.GetId(baselineBlock),
                chunk.GetBlock(local.X, local.Y, local.Z));
        }

        var arrivingNeighbor = new ChunkCoord(9, 3, -2);
        world.InsertChunk(
            arrivingNeighbor,
            generator.Materialize(arrivingNeighbor));
        var tracePositions = DivergentVoxels
            .Select(voxel => voxel.Position)
            .ToHashSet();
        var allDecisions =
            new List<DirectionalFacePaletteApplicationDiagnostic>();
        var changedPositions = generator.ReconcileChunkArrivalWithDiagnostics(
            world,
            arrivingNeighbor,
            allDecisions.Add).ToHashSet();
        Assert.All(
            DivergentVoxels,
            voxel => Assert.Contains(voxel.Position, changedPositions));
        var decisions = allDecisions
            .Where(decision => tracePositions.Contains(decision.SolidPosition))
            .ToDictionary(decision => decision.SolidPosition);

        var gravel = blocks.GetId("asteria:gravel");
        Assert.Equal(DivergentVoxels.Length, decisions.Count);

        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var currentBlock = chunk.GetBlock(local.X, local.Y, local.Z);
            var decision = decisions[position];
            var description =
                $"voxel={position.X},{position.Y},{position.Z}; " +
                $"baseline={baselineBlock}; current={blocks.GetDefinition(currentBlock).Id}; " +
                $"owner={decision.OwnerBiome} ({decision.OwnerKind}); " +
                $"face={decision.Face}; depth={decision.Depth}; " +
                $"air-neighbor={decision.EmptyNeighbor.X},{decision.EmptyNeighbor.Y},{decision.EmptyNeighbor.Z}; " +
                $"replacement={blocks.GetDefinition(decision.Replacement).Id}";

            Assert.Contains(position, changedPositions);
            Assert.Equal(gravel, currentBlock);
            Assert.Equal(CavernBiomeId, decision.OwnerBiome);
            Assert.Equal(BiomeRegionOwnerKind.Volume, decision.OwnerKind);
            Assert.Equal(BiomePaletteFace.Walls, decision.Face);
            Assert.InRange(decision.Depth, 0u, 2u);
            Assert.Equal("asteria:gravel", blocks.GetDefinition(decision.Replacement).Id);
            Assert.True(
                generator.IsCaveVoidAt(
                    decision.EmptyNeighbor.X,
                    decision.EmptyNeighbor.Y,
                    decision.EmptyNeighbor.Z),
                description);
            Assert.Equal(
                arrivingNeighbor,
                VoxelCoordinates.FromWorld(
                    decision.EmptyNeighbor.X,
                    decision.EmptyNeighbor.Y,
                    decision.EmptyNeighbor.Z).Chunk);
        }
    }
}
