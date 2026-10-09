using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanCoastGenerationTests
{
    [Theory]
    [InlineData(6f)]
    [InlineData(120f)]
    public void OceanShoreStaysLowAndUphillSlopeStartsInsideLand(float landOffset)
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
        ]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(0, 96, 255),
                0.7f),
        ]);
        BiomeDefinition Biome(string id, float offset) =>
            new(
                id,
                new BiomeSurfaceLayoutDefinition(
                    weight: 1f,
                    regionMin: 256,
                    regionMax: 512),
                new BiomeTerrainDefinition(
                    offset,
                    macroAmplitude: 0f,
                    macroScale: 128,
                    detailAmplitude: 0f,
                    detailScale: 32),
                [
                    new BiomeSurfaceLayerDefinition("asteria:stone"),
                ]);

        var ocean = Biome("asteria:test/ocean", -18f);
        var land = Biome("asteria:test/land", landOffset);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [ocean.Id, land.Id],
            seaLevel: 90,
            gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f),
            generatedOcean: new DimensionGeneratedOceanDefinition(
                ocean.Id, "asteria:water"));

        var generator = new BiomeWorldGenerator(
            0x0CEA_2026UL,
            dimension,
            blocks,
            fluids,
            new BiomeRegistry([ocean, land]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(spawnCaves: false));

        // Find an actual one-voxel ownership boundary, not an artificial
        // BiomeSample: this also guards the transition across chunk seams.
        var foundBoundary = false;
        foreach (var z in new[] { 0, 64, -64, 192 })
        {
            for (var x = -512; x < 512; x++)
            {
                var left = generator.Biomes.Sample(x, z);
                var right = generator.Biomes.Sample(x + 1, z);
                if (left.Primary == right.Primary)
                {
                    continue;
                }

                var landX = left.Primary == land.Id ? x : x + 1;
                var oceanX = left.Primary == ocean.Id ? x : x + 1;
                var landHeight = generator.SurfaceHeight(landX, z);
                var oceanHeight = generator.SurfaceHeight(oceanX, z);

                // The ocean must not climb to match even a 120-block
                // neighboring mountain. Its authored shore is at +2.
                Assert.InRange(oceanHeight, 91, 93);
                Assert.InRange(landHeight, 91, (int)(90 + landOffset));
                Assert.InRange(
                    Math.Abs(oceanHeight - landHeight),
                    0,
                    3);
                foundBoundary = true;
                break;
            }

            if (foundBoundary)
            {
                break;
            }
        }

        Assert.True(foundBoundary, "The test seed must expose a land/ocean boundary.");
    }
}
