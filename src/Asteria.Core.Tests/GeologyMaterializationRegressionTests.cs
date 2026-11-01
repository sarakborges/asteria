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
    public void GeneratedGravityGeologyNeverMaterializesUnsupportedVoxelsAcrossChunkSeams()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var caves = new DimensionCaveDefinition(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 8,
                maxDepth: 80,
                channels:
                [
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                ],
                noiseHalfWidth: 1f,
                densityScale: 24f,
                boundaryFade: 6),
        ]);
        var dimension = CreateDimension(biome.Id, caves);
        var firstGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, new BiomeRegistry([biome]));
        var secondGenerator = new BiomeWorldGenerator(
            Seed, dimension, blocks, new BiomeRegistry([biome]));
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var gravityBlocks = new HashSet<BlockRuntimeId>
        {
            blocks.GetId("asteria:sand"),
            blocks.GetId("asteria:gravel"),
        };
        WorldVoxelCoord? unsupportedAtSeam = null;
        WorldVoxelCoord? supportedAtSeam = null;

        // The flat surface is Y=40. With caves beginning at depth eight,
        // the roof crosses the Y=32 vertical chunk seam.
        for (var z = -128; z <= 128 &&
             (!unsupportedAtSeam.HasValue || !supportedAtSeam.HasValue); z += 2)
        for (var x = -128; x <= 128 &&
             (!unsupportedAtSeam.HasValue || !supportedAtSeam.HasValue); x += 2)
        {
            const int worldY = Chunk.Size * 2;
            if (firstGenerator.DensityAt(x, worldY, z) < 0d ||
                !materials.TryGeologyBlockAt(
                    sample, x, worldY, z, out var material) ||
                !gravityBlocks.Contains(material))
                continue;

            var position = new WorldVoxelCoord(x, worldY, z);
            if (firstGenerator.DensityAt(x, worldY - 1, z) < 0d)
                unsupportedAtSeam ??= position;
            else
                supportedAtSeam ??= position;
        }

        Assert.True(unsupportedAtSeam.HasValue,
            "The cave fixture did not produce an unsupported gravity deposit at the vertical seam.");
        Assert.True(supportedAtSeam.HasValue,
            "The cave fixture did not produce a supported gravity deposit at the vertical seam.");

        var unsupported = unsupportedAtSeam.Value;
        var (upperCoord, upperLocal) = VoxelCoordinates.FromWorld(
            unsupported.X, unsupported.Y, unsupported.Z);
        var lowerCoord = new ChunkCoord(
            upperCoord.X, upperCoord.Y - 1, upperCoord.Z);
        var firstUpper = firstGenerator.Materialize(upperCoord);
        var firstLower = firstGenerator.Materialize(lowerCoord);
        var secondLower = secondGenerator.Materialize(lowerCoord);
        var secondUpper = secondGenerator.Materialize(upperCoord);

        Assert.Equal(
            blocks.GetId("asteria:stone"),
            firstUpper.GetBlock(
                upperLocal.X, upperLocal.Y, upperLocal.Z));
        AssertChunksEqual(firstUpper, secondUpper);
        AssertChunksEqual(firstLower, secondLower);

        var supported = supportedAtSeam.Value;
        var (supportedCoord, supportedLocal) = VoxelCoordinates.FromWorld(
            supported.X, supported.Y, supported.Z);
        var supportedChunk = firstGenerator.Materialize(supportedCoord);
        Assert.True(gravityBlocks.Contains(
            supportedChunk.GetBlock(
                supportedLocal.X, supportedLocal.Y, supportedLocal.Z)),
            "A supported sand/gravel deposit must retain its geological material.");

        foreach (var (coord, chunk) in new[]
                 {
                     (upperCoord, firstUpper),
                     (lowerCoord, firstLower),
                 })
        {
            for (var z = 0; z < Chunk.Size; z++)
            for (var y = 0; y < Chunk.Size; y++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var cell = chunk.GetCell(x, y, z);
                if (cell.IsEmpty || !gravityBlocks.Contains(cell.Block))
                    continue;

                var worldY = coord.Y * Chunk.Size + y;
                if (worldY == 0)
                    continue;

                var belowOccupied = y > 0
                    ? !chunk.GetCell(x, y - 1, z).IsEmpty
                    : coord == upperCoord &&
                      !firstLower.GetCell(x, Chunk.Size - 1, z).IsEmpty;
                Assert.True(belowOccupied,
                    $"Unsupported generated gravity block remains at " +
                    $"{coord.X * Chunk.Size + x},{worldY},{coord.Z * Chunk.Size + z}.");
            }
        }

        static void AssertChunksEqual(Chunk expected, Chunk actual)
        {
            for (var z = 0; z < Chunk.Size; z++)
            for (var y = 0; y < Chunk.Size; y++)
            for (var x = 0; x < Chunk.Size; x++)
                Assert.Equal(
                    expected.GetCell(x, y, z),
                    actual.GetCell(x, y, z));
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
    public void MaterializedVerticalWallBreaksLongMaterialColumns()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome();
        var dimension = CreateDimension(biome.Id);
        var generator = new BiomeWorldGenerator(
            Seed, dimension, blocks, new BiomeRegistry([biome]));
        var chunks = new Dictionary<ChunkCoord, Chunk>();
        var materials = new HashSet<BlockRuntimeId>();
        var columnsWithVariation = 0;
        var horizontalTransitions = 0;

        // Read the actual materialized voxel wall, not direct noise-field
        // queries. The terrain is flat at Y=40, so every sampled voxel below
        // it is part of one continuous exposed solid section.
        for (var x = -32; x < 32; x++)
        {
            var previous = BlockRuntimeId.Air;
            var column = new HashSet<BlockRuntimeId>();
            for (var y = 8; y < 40; y++)
            {
                var (coord, local) = VoxelCoordinates.FromWorld(x, y, 0);
                if (!chunks.TryGetValue(coord, out var chunk))
                {
                    chunk = generator.Materialize(coord);
                    chunks.Add(coord, chunk);
                }

                var block = chunk.GetBlock(local.X, local.Y, local.Z);
                materials.Add(block);
                column.Add(block);
                if (!previous.IsAir && previous != block)
                    horizontalTransitions++;
                previous = block;
            }

            if (column.Count > 1)
                columnsWithVariation++;
        }

        Assert.True(materials.Count >= 4,
            $"Expected host rock and multiple materials on a materialized wall; found {materials.Count}.");
        Assert.True(columnsWithVariation >= 48,
            $"Long single-material columns remain in the materialized wall: {columnsWithVariation}/64 varied.");
        Assert.True(horizontalTransitions >= 100,
            $"Material boundaries are still too continuous on the wall: {horizontalTransitions} transitions.");
    }

    [Fact]
    public void FlatExposedSurfaceRetainsCoverAndShowsGeologicalVariation()
    {
        var blocks = CreateBlocks();
        var biome = CreateBiome(surfaceCoverage: 0.4f);
        var dimension = CreateDimension(biome.Id);
        var biomes = new BiomeRegistry([biome]);
        var generator = new BiomeWorldGenerator(Seed, dimension, blocks, biomes);
        var surfaceMaterials = new HashSet<BlockRuntimeId>();

        // Sample a 64x64 flat surface so low authored exposure coverage remains
        // meaningful while checking actual materialized top voxels.
        var chunksWithSurfaceVariation = 0;
        for (var chunkZ = -2; chunkZ < 2; chunkZ++)
        for (var chunkX = -2; chunkX < 2; chunkX++)
        {
            var chunk = generator.Materialize(new ChunkCoord(chunkX, 2, chunkZ));
            var localSurface = new HashSet<BlockRuntimeId>();
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
            {
                var block = chunk.GetBlock(x, 40 % Chunk.Size, z);
                surfaceMaterials.Add(block);
                localSurface.Add(block);
            }

            if (localSurface.Count > 1)
                chunksWithSurfaceVariation++;
        }

        Assert.True(chunksWithSurfaceVariation >= 12,
            $"Flat exposed surface remains uniform within most chunks: {chunksWithSurfaceVariation}/16 varied.");
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
        new BlockDefinition("asteria:sand",
            tags: [BlockPhysicsCapabilities.Gravity]),
        new BlockDefinition("asteria:gravel",
            tags: [BlockPhysicsCapabilities.Gravity]),
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
            ["asteria:sand", "asteria:gravel", "asteria:clay", "asteria:dirt"],
            scale: 24,
            verticalScale: 6,
            detailScale: 5,
            selectionScale: 12,
            coverage: 0.78f,
            roughness: 0.32f,
            surfaceCoverDepth: 3,
            surfaceCoverage: surfaceCoverage));

    private static DimensionDefinition CreateDimension(
        string biomeId,
        DimensionCaveDefinition? caves = null) => new(
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
            0f),
        caves: caves);
}
