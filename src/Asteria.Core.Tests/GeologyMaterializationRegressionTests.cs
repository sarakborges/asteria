using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GeologyMaterializationRegressionTests
{
    private const ulong Seed = 0xA57E_3D10_2026UL;

    [Fact]
    public void MaterializedChunksMatchWorldSpaceGeologyAcrossChunkSeamsAndGenerationOrder()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var firstGenerator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var secondGenerator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);

        // Materialize in opposite orders so cache and generation order cannot
        // influence voxels at either the horizontal or vertical chunk seam.
        var firstUpperRight = firstGenerator.Materialize(new ChunkCoord(1, 2, 0));
        var firstUpperLeft = firstGenerator.Materialize(new ChunkCoord(0, 2, 0));
        var firstLowerRight = firstGenerator.Materialize(new ChunkCoord(1, 1, 0));
        var firstLowerLeft = firstGenerator.Materialize(new ChunkCoord(0, 1, 0));
        var secondLowerLeft = secondGenerator.Materialize(new ChunkCoord(0, 1, 0));
        var secondLowerRight = secondGenerator.Materialize(new ChunkCoord(1, 1, 0));
        var secondUpperLeft = secondGenerator.Materialize(new ChunkCoord(0, 2, 0));
        var secondUpperRight = secondGenerator.Materialize(new ChunkCoord(1, 2, 0));

        var grass = blocks.GetId("asteria:grass");
        var dirt = blocks.GetId("asteria:dirt");
        var cover = Assert.IsType<BiomeGeologyDefinition>(biome.Geology)
            .SurfaceCoverDepth;

        for (var x = 0; x < Chunk.Size * 2; x++)
        for (var z = 0; z < Chunk.Size; z++)
        for (var worldY = 16; worldY <= 40; worldY++)
        {
            var coord = new ChunkCoord(x / Chunk.Size, worldY / Chunk.Size, 0);
            var localX = x % Chunk.Size;
            var localY = worldY % Chunk.Size;
            var actualFirst = (x / Chunk.Size, coord.Y) switch
            {
                (0, 1) => firstLowerLeft.GetBlock(localX, localY, z),
                (1, 1) => firstLowerRight.GetBlock(localX, localY, z),
                (0, 2) => firstUpperLeft.GetBlock(localX, localY, z),
                (1, 2) => firstUpperRight.GetBlock(localX, localY, z),
                _ => throw new InvalidOperationException("Unexpected test chunk coordinate."),
            };
            var actualSecond = (x / Chunk.Size, coord.Y) switch
            {
                (0, 1) => secondLowerLeft.GetBlock(localX, localY, z),
                (1, 1) => secondLowerRight.GetBlock(localX, localY, z),
                (0, 2) => secondUpperLeft.GetBlock(localX, localY, z),
                (1, 2) => secondUpperRight.GetBlock(localX, localY, z),
                _ => throw new InvalidOperationException("Unexpected test chunk coordinate."),
            };

            Assert.Equal(actualFirst, actualSecond);

            var depth = checked((uint)(40 - worldY));
            if (depth == 0)
            {
                Assert.Equal(grass, actualFirst);
            }
            else if (depth < cover)
            {
                Assert.Equal(dirt, actualFirst);
            }
            else
            {
                Assert.True(materials.TryGeologyBlockAt(
                    sample, x, worldY, z, out var expected));
                Assert.Equal(expected, actualFirst);
            }
        }

        // Explicitly exercise each voxel along both shared faces.
        for (var worldY = 16; worldY <= 40; worldY++)
        for (var z = 0; z < Chunk.Size; z++)
        {
            var chunkY = worldY / Chunk.Size;
            var leftFirst = chunkY == 1
                ? firstLowerLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z)
                : firstUpperLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z);
            var rightFirst = chunkY == 1
                ? firstLowerRight.GetBlock(0, worldY % Chunk.Size, z)
                : firstUpperRight.GetBlock(0, worldY % Chunk.Size, z);
            var leftSecond = chunkY == 1
                ? secondLowerLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z)
                : secondUpperLeft.GetBlock(Chunk.Size - 1, worldY % Chunk.Size, z);
            var rightSecond = chunkY == 1
                ? secondLowerRight.GetBlock(0, worldY % Chunk.Size, z)
                : secondUpperRight.GetBlock(0, worldY % Chunk.Size, z);
            Assert.Equal(leftFirst, leftSecond);
            Assert.Equal(rightFirst, rightSecond);
            Assert.True(materials.TryGeologyBlockAt(
                sample, Chunk.Size - 1, worldY, z, out var expectedLeft));
            Assert.True(materials.TryGeologyBlockAt(
                sample, Chunk.Size, worldY, z, out var expectedRight));
            if (40 - worldY >= cover)
            {
                Assert.Equal(expectedLeft, leftFirst);
                Assert.Equal(expectedRight, rightFirst);
            }
        }
    }

    [Fact]
    public void GeologyVariesAtOneAltitudeAndThroughVerticalMaterializedColumns()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var sameAltitude = new HashSet<BlockRuntimeId>();
        var verticalColumnsWithVariation = 0;

        for (var z = -96; z <= 96; z += 3)
        for (var x = -96; x <= 96; x += 3)
        {
            Assert.True(materials.TryGeologyBlockAt(
                sample, x, 24, z, out var sameHeightBlock));
            sameAltitude.Add(sameHeightBlock);

            var vertical = new HashSet<BlockRuntimeId>();
            for (var y = 3; y <= 40; y++)
            {
                Assert.True(materials.TryGeologyBlockAt(
                    sample, x, y, z, out var block));
                vertical.Add(block);
            }

            if (vertical.Count > 1)
                verticalColumnsWithVariation++;
        }

        Assert.True(sameAltitude.Count >= 3,
            $"Expected host rock and deposits at the same Y; found {sameAltitude.Count} material(s).");
        Assert.True(verticalColumnsWithVariation > 0,
            "No sampled world-space column changed geological material with Y.");
    }

    [Fact]
    public void FlatExposedSurfaceRetainsCoverAndShowsGeologicalVariation()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome(surfaceCoverage: 0.45f);
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var generator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var chunk = generator.Materialize(new ChunkCoord(0, 2, 0));
        var surfaceMaterials = new HashSet<BlockRuntimeId>();

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
            surfaceMaterials.Add(chunk.GetBlock(x, 40 % Chunk.Size, z));

        Assert.Contains(blocks.GetId("asteria:grass"), surfaceMaterials);
        Assert.Contains(blocks.GetId("asteria:dirt"), surfaceMaterials);
        Assert.Contains(blocks.GetId("asteria:gravel"), surfaceMaterials);
        Assert.True(surfaceMaterials.Count >= 4,
            "A flat surface should retain its grass cover while exposing varied geological materials.");
    }

    private static BlockRegistry CreateBlocks() => new(
    [
        new BlockDefinition("asteria:grass"),
        new BlockDefinition("asteria:dirt"),
        new BlockDefinition("asteria:stone"),
        new BlockDefinition("asteria:gravel"),
        new BlockDefinition("asteria:clay"),
    ]);

    private static BiomeDefinition CreateBiome(float surfaceCoverage = 0f) => new(
        "asteria:test/geology",
        new BiomeSurfaceLayoutDefinition(regionMin: 128, regionMax: 128),
        new BiomeTerrainDefinition(
            baseHeightOffset: 40f,
            macroAmplitude: 0f,
            macroScale: 128,
            detailAmplitude: 0f,
            detailScale: 32),
        [
            new BiomeSurfaceLayerDefinition("asteria:grass", depth: 1),
            new BiomeSurfaceLayerDefinition("asteria:dirt", depth: 2),
            new BiomeSurfaceLayerDefinition("asteria:stone"),
        ],
        geology: new BiomeGeologyDefinition(
            "asteria:stone",
            ["asteria:gravel", "asteria:clay", "asteria:dirt"],
            scale: 24,
            verticalScale: 6,
            detailScale: 5,
            selectionScale: 12,
            coverage: 0.78f,
            roughness: 0.32f,
            surfaceCoverDepth: 3,
            surfaceCoverage: surfaceCoverage));

    private static DimensionDefinition CreateDimension(string biomeId) => new(
        new DimensionId("asteria:test"),
        [biomeId],
        seaLevel: 0,
        gravityStrength: 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255),
            1f,
            new DimensionColor(0, 0, 0),
            0f));
}
