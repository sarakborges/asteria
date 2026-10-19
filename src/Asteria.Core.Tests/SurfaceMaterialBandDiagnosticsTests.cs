using System.Text;
using Asteria.Core.World;
using Xunit.Abstractions;

namespace Asteria.Core.Tests;

public sealed class SurfaceMaterialBandDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public SurfaceMaterialBandDiagnosticsTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void UniformSurfacePaletteStaysUniformAcrossUnevenTerrain()
    {
        var stone = new BlockDefinition("asteria:stone");
        var blocks = new BlockRegistry([stone]);
        var fluids = CreateFluids();
        var biome = CreateBiome(
            "asteria:test/uneven_surface",
            new BiomeTerrainDefinition(
                baseHeightOffset: 0f,
                macroAmplitude: 12f,
                macroScale: 28u,
                detailAmplitude: 5f,
                detailScale: 8u),
            [new BiomeSurfaceLayerDefinition("asteria:stone")]);
        var generator = CreateGenerator(blocks, fluids, biome);
        var materialized = new Dictionary<ChunkCoord, Chunk>();
        var heights = new HashSet<int>();
        var stoneId = blocks.GetId("asteria:stone");

        for (var z = 0; z < 64; z += 2)
        {
            for (var x = 0; x < 64; x += 2)
            {
                var surfaceY = generator.SurfaceHeight(x, z);
                heights.Add(surfaceY);

                for (var depth = 0; depth < 4; depth++)
                {
                    var address = VoxelCoordinates.FromWorld(
                        x,
                        surfaceY - depth,
                        z);
                    if (!materialized.TryGetValue(address.Chunk, out var chunk))
                    {
                        chunk = generator.Materialize(address.Chunk);
                        materialized.Add(address.Chunk, chunk);
                    }

                    Assert.Equal(
                        stoneId,
                        chunk.GetBlock(
                            address.Local.X,
                            address.Local.Y,
                            address.Local.Z));
                }
            }
        }

        Assert.True(
            heights.Count >= 4,
            $"The diagnostic terrain should have varied heights; observed {heights.Count}.");
    }

    [Fact]
    public void LayeredSurfaceEmitsReproducibleVoxelCrossSection()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:soil"),
            new BlockDefinition("asteria:gravel"),
            new BlockDefinition("asteria:stone"),
        ]);
        var biome = CreateBiome(
            "asteria:test/layered_uneven_surface",
            new BiomeTerrainDefinition(
                baseHeightOffset: 0f,
                macroAmplitude: 12f,
                macroScale: 28u,
                detailAmplitude: 5f,
                detailScale: 8u),
            [
                new BiomeSurfaceLayerDefinition("asteria:soil", depth: 3),
                new BiomeSurfaceLayerDefinition("asteria:gravel", depth: 2),
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ]);
        var generator = CreateGenerator(blocks, CreateFluids(), biome);
        const int z = 32;
        const int step = 2;
        var samples = Enumerable.Range(0, 64 / step)
            .Select(index =>
            {
                var x = index * step;
                var surfaceY = generator.SurfaceHeight(x, z);
                var address = VoxelCoordinates.FromWorld(x, surfaceY, z);
                return (X: x, SurfaceY: surfaceY, Address: address);
            })
            .ToArray();
        var materialized = new Dictionary<ChunkCoord, Chunk>();

        Chunk GetChunk(ChunkCoord coord)
        {
            if (!materialized.TryGetValue(coord, out var chunk))
            {
                chunk = generator.Materialize(coord);
                materialized.Add(coord, chunk);
            }

            return chunk;
        }

        var minimumY = samples.Min(sample => sample.SurfaceY) - 2;
        var maximumY = samples.Max(sample => sample.SurfaceY);
        var soilId = blocks.GetId("asteria:soil");
        var gravelId = blocks.GetId("asteria:gravel");
        var stoneId = blocks.GetId("asteria:stone");
        var report = new StringBuilder();

        report.AppendLine(
            $"Surface material cross-section: seed=0xBA_DD_1E, z={z}, x step={step}");
        report.AppendLine("Legend: space=air, S=soil, G=gravel, #=stone, ?=unexpected");
        report.Append("Y\\X ");
        foreach (var sample in samples)
            report.Append($"{sample.X / step % 10}");
        report.AppendLine();

        for (var y = maximumY; y >= minimumY; y--)
        {
            report.Append($"{y,4} ");
            foreach (var sample in samples)
            {
                if (y > sample.SurfaceY)
                {
                    report.Append(' ');
                    continue;
                }

                var address = VoxelCoordinates.FromWorld(sample.X, y, z);
                var block = GetChunk(address.Chunk).GetBlock(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z);
                report.Append(block == soilId ? 'S' :
                    block == gravelId ? 'G' :
                    block == stoneId ? '#' : '?');
            }

            report.AppendLine();
        }

        _output.WriteLine(report.ToString());

        Assert.All(samples, sample =>
        {
            var surface = GetChunk(sample.Address.Chunk).GetBlock(
                sample.Address.Local.X,
                sample.Address.Local.Y,
                sample.Address.Local.Z);
            Assert.Equal(soilId, surface);
        });
        Assert.True(
            samples.Select(sample => sample.SurfaceY).Distinct().Count() >= 4,
            "The diagnostic terrain must expose enough height variation to inspect banding.");
        Assert.DoesNotContain("?", report.ToString());
    }

    private static FluidRegistry CreateFluids() =>
        new(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(0, 96, 255),
                0.7f),
        ]);

    private static BiomeDefinition CreateBiome(
        string id,
        BiomeTerrainDefinition terrain,
        IEnumerable<BiomeSurfaceLayerDefinition> layers) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                weight: 1f,
                regionMin: 256,
                regionMax: 512),
            terrain,
            layers);

    private static BiomeWorldGenerator CreateGenerator(
        BlockRegistry blocks,
        FluidRegistry fluids,
        BiomeDefinition biome)
    {
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id],
            seaLevel: 80,
            gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f));
        return new BiomeWorldGenerator(
            0xBA_DD_1EUL,
            dimension,
            blocks,
            fluids,
            new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                spawnCaves: false,
                spawnOceans: false));
    }
}
