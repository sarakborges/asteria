using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceMaterialBandDiagnosticsTests
{
    [Fact]
    public void UniformSurfacePaletteStaysUniformAcrossUnevenTerrain()
    {
        var stone = new BlockDefinition("asteria:stone");
        var blocks = new BlockRegistry([stone]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(0, 96, 255),
                0.7f),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/uneven_surface",
            new BiomeSurfaceLayoutDefinition(
                weight: 1f,
                regionMin: 256,
                regionMax: 512),
            new BiomeTerrainDefinition(
                offset: 0f,
                macroAmplitude: 12f,
                macroScale: 28f,
                detailAmplitude: 5f,
                detailScale: 8f),
            [
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ]);
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
        var generator = new BiomeWorldGenerator(
            0xBA_DD_1EUL,
            dimension,
            blocks,
            fluids,
            new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                spawnCaves: false,
                spawnOceans: false));
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
                    if (!materialized.TryGetValue(
                            address.Chunk,
                            out var chunk))
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
}
