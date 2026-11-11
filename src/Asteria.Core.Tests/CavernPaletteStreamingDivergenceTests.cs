using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernPaletteStreamingDivergenceTests
{
    private const ulong Seed = 181960897289965UL;
    private const string EnchantedForestBiomeId = "asteria:overworld/enchanted_forest";

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
    public void StreamingBenchmarkSeedRestoresGeologicGravelWhenNeighborArrivalRemovesDirectionalExposure()
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

        var materials = new BiomeSurfaceMaterialField(
            dimensionSeed,
            dimension.SurfaceBiomes
                .Concat(dimension.VolumeBiomes)
                .Distinct(StringComparer.Ordinal)
                .Select(biomes.Get),
            blocks,
            generator.Terrain);

        var sourceCoord = new ChunkCoord(9, 3, -3);
        var sourceChunk = generator.Materialize(sourceCoord);
        var world = new VoxelWorld();
        world.InsertChunk(sourceCoord, sourceChunk);

        // The visible block is the initial directional palette result;
        // BaseBlock retains the underlying geologic material.
        var geologicBaseBlocks = new Dictionary<WorldVoxelCoord, BlockRuntimeId>();
        var gravel = blocks.GetId("asteria:gravel");
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var linearIndex = local.X +
                Chunk.Size * (local.Z + Chunk.Size * local.Y);
            var current = sourceChunk.GetBlock(local.X, local.Y, local.Z);
            var faceData = sourceChunk.DirectionalFaceDataAtLinearIndex(linearIndex);
            var storedBase = faceData.BaseBlock.IsAir
                ? "<air>"
                : blocks.GetDefinition(faceData.BaseBlock).Id;
            var metadata =
                $"voxel={position.X},{position.Y},{position.Z}; " +
                $"current={blocks.GetDefinition(current).Id}; storedBase={storedBase}; " +
                $"terrain={faceData.IsTerrainVoxel}; hasCandidates={faceData.HasCandidates}";
            Assert.Equal(blocks.GetId(baselineBlock), current);
            Assert.True(faceData.IsTerrainVoxel, metadata);
            Assert.True(faceData.HasCandidates, metadata);
            var surfaceBiome = generator.Biomes.Sample(
                position.X, position.Z);
            Assert.Equal(EnchantedForestBiomeId, surfaceBiome.Primary);
            Assert.Equal(
                EnchantedForestBiomeId,
                generator.EffectiveBiomeAt(position.X, position.Y, position.Z));

            var surfaceOwner = new BiomeRegionOwner(
                surfaceBiome, BiomeRegionOwnerKind.Surface);
            var surfacePalette = materials.DirectionalPaletteFor(surfaceOwner);
            Assert.False(surfacePalette.Has(BiomePaletteFace.Walls));
            Assert.Equal(
                gravel,
                materials.VolumeBlockAt(
                    surfaceBiome,
                    position.X, position.Y, position.Z,
                    BiomePaletteFace.Walls,
                    depth: 0));
            Assert.Equal(gravel, faceData.BaseBlock);
            geologicBaseBlocks.Add(position, faceData.BaseBlock);
        }

        var arrivingNeighbor = new ChunkCoord(9, 3, -2);
        world.InsertChunk(
            arrivingNeighbor,
            generator.Materialize(arrivingNeighbor));

        var expectedPositions = DivergentVoxels
            .Select(voxel => voxel.Position)
            .ToHashSet();
        var diagnostics =
            new Dictionary<WorldVoxelCoord, DirectionalFacePaletteReapplicationDiagnostic>();
        var changedPositions = generator.ReconcileChunkArrivalWithDiagnostics(
            world,
            arrivingNeighbor,
            diagnostic =>
            {
                if (expectedPositions.Contains(diagnostic.Position))
                {
                    Assert.False(
                        diagnostics.ContainsKey(diagnostic.Position),
                        $"Duplicate reapplication diagnostic for {diagnostic.Position}.");
                    diagnostics.Add(diagnostic.Position, diagnostic);
                }
            }).ToHashSet();

        Assert.Equal(DivergentVoxels.Length, changedPositions.Count);
        Assert.True(
            expectedPositions.SetEquals(changedPositions),
            "Neighbor arrival must change exactly the twelve voxels identified by the streaming A/B artifact.");

        Assert.Equal(DivergentVoxels.Length, diagnostics.Count);
        var transitions = new Dictionary<string, int>(StringComparer.Ordinal);
        var paletteApplications = 0;
        var geologicRestorations = 0;
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var current = sourceChunk.GetBlock(local.X, local.Y, local.Z);
            var diagnostic = diagnostics[position];
            Assert.Equal(gravel, current);
            Assert.Equal(geologicBaseBlocks[position], diagnostic.RestoredBlock);
            Assert.True(
                diagnostic.WasTerrainVoxel,
                $"Reapplication did not start from a terrain voxel at {position}.");
            if (diagnostic.SolidOwnerBiome is null)
            {
                Assert.False(diagnostic.HadCandidates);
                Assert.False(diagnostic.Applied);
            }
            else
            {
                Assert.Equal(EnchantedForestBiomeId, diagnostic.SolidOwnerBiome);
                Assert.Equal(BiomeRegionOwnerKind.Surface, diagnostic.SolidOwnerKind);
            }

            if (diagnostic.Applied)
            {
                paletteApplications++;
                Assert.Equal("asteria:overworld/caverns", diagnostic.AppliedOwnerBiome);
                Assert.Equal(BiomeRegionOwnerKind.Volume, diagnostic.AppliedOwnerKind);
                Assert.Equal(BiomePaletteFace.Walls, diagnostic.Face);
                Assert.NotNull(diagnostic.Depth);
                Assert.NotNull(diagnostic.EmptyNeighbor);
                Assert.Equal(gravel, diagnostic.Replacement);
                Assert.True(
                    generator.IsCaveVoidAt(
                        diagnostic.EmptyNeighbor.Value.X,
                        diagnostic.EmptyNeighbor.Value.Y,
                        diagnostic.EmptyNeighbor.Value.Z),
                    $"Selected palette owner was not adjacent to a cave void at {position}.");
            }
            else
            {
                geologicRestorations++;
                Assert.Null(diagnostic.AppliedOwnerBiome);
                Assert.Null(diagnostic.Face);
                Assert.Null(diagnostic.Replacement);
                Assert.Equal(gravel, diagnostic.RestoredBlock);
            }

            transitions[baselineBlock] =
                transitions.TryGetValue(baselineBlock, out var count)
                    ? count + 1
                    : 1;
        }

        Assert.Equal(
            0,
            paletteApplications);
        Assert.Equal(
            DivergentVoxels.Length,
            geologicRestorations);

        Assert.Equal(5, transitions["asteria:stone"]);
        Assert.Equal(7, transitions["asteria:cobble_stone"]);
    }
}
