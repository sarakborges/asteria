using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanSeabedMaterialTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void OceanSeabedUsesOrganicThreeDimensionalGeologyWithoutHorizontalBands()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var geology = Assert.IsType<BiomeGeologyDefinition>(biome.Geology);

        Assert.Equal(1u, geology.SurfaceCoverDepth);
        Assert.Equal(0.5f, geology.SurfaceCoverage);
        Assert.True(geology.SurfaceSubmergedOnly);
        Assert.Equal("asteria:sand", biome.SurfaceLayers[0].Block);
        Assert.Equal(1u, biome.SurfaceLayers[0].Depth);
        Assert.Null(biome.SurfaceLayers[0].Patch);
        Assert.InRange(geology.VerticalScale / (double)geology.Scale, 0.5d, 0.75d);

        var blocks = BlockRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(PackData, "blocks"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var materials = new BiomeSurfaceMaterialField(
            7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        const int sampleCount = 97;
        var grid = new BlockRuntimeId[sampleCount, sampleCount];
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount; x++)
        {
            Assert.True(materials.TryGeologyBlockAt(
                sample, 355 + (x - 48) * 4, 82, -230 + (z - 48) * 4, out var block));
            grid[x, z] = block;
            seen.Add(block);
        }

        var xTransitions = 0;
        var zTransitions = 0;
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount - 1; x++)
            if (grid[x, z] != grid[x + 1, z])
                xTransitions++;
        for (var z = 0; z < sampleCount - 1; z++)
        for (var x = 0; x < sampleCount; x++)
            if (grid[x, z] != grid[x, z + 1])
                zTransitions++;

        Assert.True(seen.Count >= 3,
            "The ocean floor should contain the host rock and multiple geological deposits.");
        Assert.True(xTransitions > 0 && zTransitions > 0,
            "The volumetric geology should vary along both horizontal axes.");
        var larger = Math.Max(xTransitions, zTransitions);
        var smaller = Math.Min(xTransitions, zTransitions);
        Assert.True(smaller >= larger * 0.45,
            $"Geology variation is strongly directional: X={xTransitions}, Z={zTransitions}.");

        var vertical = new HashSet<BlockRuntimeId>();
        for (var y = 8; y <= 168; y += 4)
        {
            Assert.True(materials.TryGeologyBlockAt(sample, 355, y, -230, out var block));
            vertical.Add(block);
        }
        Assert.True(vertical.Count > 1,
            "The material distribution must change within a vertical world-space column.");

        var surfaceDeposits = new HashSet<BlockRuntimeId>();
        for (var z = 0; z < sampleCount; z++)
        for (var x = 0; x < sampleCount; x++)
            if (materials.TrySurfaceGeologyBlockAt(
                sample,
                355 + (x - 48) * 4,
                82,
                -230 + (z - 48) * 4,
                out var block))
                surfaceDeposits.Add(block);

        Assert.True(surfaceDeposits.Count >= 3,
            $"Exposed submerged terrain should reveal multiple deposits, found {surfaceDeposits.Count}.");
    }

    [Fact]
    public void MaterializedOceanSurfaceVariesBelowWaterAndKeepsDryBeachSand()
    {
        const ulong seed = 7808508665006554090UL;
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJsonDirectory("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJsonDirectory("biomes"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var structureSets = StructureSetRegistry.FromJson(ReadJsonDirectory("structure_sets"));
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var generator = new BiomeWorldGenerator(
            DimensionSeed.Derive(seed, dimension.Id),
            dimension, blocks, fluids, biomes, structures, structureSets,
            new WorldGenerationOptions(
                spawnStructures: false, spawnCaves: false, spawnOceans: true));
        var ocean = biomes.Get("asteria:overworld/ocean");
        var sand = blocks.GetId("asteria:sand");
        var chunkCache = new Dictionary<ChunkCoord, Chunk>();
        var submergedSurfaceMaterials = new HashSet<BlockRuntimeId>();
        var foundExposedDeposit = false;

        for (var z = -486; z <= 486; z += 81)
        for (var x = -131; x <= 841; x += 81)
        {
            if (generator.Biomes.Sample(x, z).Primary != ocean.Id)
                continue;

            var surfaceY = generator.SurfaceHeight(x, z);
            if (surfaceY < 0)
                continue;
            var (coord, local) = VoxelCoordinates.FromWorld(x, surfaceY, z);
            if (!chunkCache.TryGetValue(coord, out var chunk))
            {
                chunk = generator.Materialize(coord);
                chunkCache.Add(coord, chunk);
            }

            var actual = chunk.GetBlock(local.X, local.Y, local.Z);
            if (surfaceY >= dimension.SeaLevel)
            {
                // Exposed beach voxels stay with the authored Sand cover.
                Assert.Equal(sand, actual);
                continue;
            }

            submergedSurfaceMaterials.Add(actual);
            foundExposedDeposit |= actual != sand;
        }

        Assert.True(submergedSurfaceMaterials.Count >= 2,
            "The materialized submerged surface should not remain entirely Sand.");
        Assert.True(foundExposedDeposit,
            "No geological deposit was exposed on the materialized submerged surface.");
    }

    [Fact]
    public void OceanGeologyTransitionsAlongAllThreeWorldAxes()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var blocks = BlockRegistry.FromJson(
            Directory.EnumerateFiles(Path.Combine(PackData, "blocks"), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var materials = new BiomeSurfaceMaterialField(
            7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);

        const int width = 17;
        const int step = 8;
        var volume = new BlockRuntimeId[width, width, width];
        for (var y = 0; y < width; y++)
        for (var z = 0; z < width; z++)
        for (var x = 0; x < width; x++)
        {
            Assert.True(materials.TryGeologyBlockAt(
                sample,
                355 + (x - width / 2) * step,
                18 + y * step,
                -230 + (z - width / 2) * step,
                out var block));
            volume[x, y, z] = block;
        }

        var xTransitions = 0;
        var yTransitions = 0;
        var zTransitions = 0;
        for (var y = 0; y < width; y++)
        for (var z = 0; z < width; z++)
        for (var x = 0; x < width; x++)
        {
            var current = volume[x, y, z];
            if (x + 1 < width && current != volume[x + 1, y, z])
                xTransitions++;
            if (y + 1 < width && current != volume[x, y + 1, z])
                yTransitions++;
            if (z + 1 < width && current != volume[x, y, z + 1])
                zTransitions++;
        }

        var minimum = Math.Min(xTransitions, Math.Min(yTransitions, zTransitions));
        var maximum = Math.Max(xTransitions, Math.Max(yTransitions, zTransitions));
        Assert.True(minimum >= maximum * 0.35,
            $"Geology is still strongly directional: X={xTransitions}, Y={yTransitions}, Z={zTransitions}.");
    }

    private static IEnumerable<string> ReadJsonDirectory(string directory) =>
        Directory.EnumerateFiles(Path.Combine(PackData, directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

}
