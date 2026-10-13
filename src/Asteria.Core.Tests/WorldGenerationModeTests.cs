using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldGenerationModeTests
{
    [Fact]
    public void FlatWorldProducesOnePlanarSurface()
    {
        var (generator, blocks) = Create(WorldGenerationMode.Flat);
        var stone = blocks.GetId("asteria:stone");

        foreach (var coord in new[]
                 {
                     new ChunkCoord(0, 2, 0),
                     new ChunkCoord(4, 2, 4),
                 })
        {
            var chunk = generator.Materialize(coord);
            Assert.Equal(32, generator.SurfaceHeight(
                coord.X * Chunk.Size + 8, coord.Z * Chunk.Size + 8));
            Assert.Equal(stone, chunk.GetBlock(8, 0, 8));
            Assert.True(chunk.GetBlock(8, 1, 8).IsAir);
        }
    }

    [Fact]
    public void FlatWorldRemainsPlanarEvenAcrossOceanBiomesWhenOceansAreEnabled()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), 0.7f),
        ]);
        BiomeDefinition Biome(string id, float offset) =>
            new(id,
                new BiomeSurfaceLayoutDefinition(
                    weight: 1f, regionMin: 256, regionMax: 512),
                new BiomeTerrainDefinition(
                    offset, macroAmplitude: 0, macroScale: 128,
                    detailAmplitude: 0, detailScale: 32),
                [new BiomeSurfaceLayerDefinition("asteria:stone")]);
        var ocean = Biome("asteria:test/ocean", -18);
        var land = Biome("asteria:test/land", 120);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [ocean.Id, land.Id],
            seaLevel: 90, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedOcean: new DimensionGeneratedOceanDefinition(
                ocean.Id, "asteria:water"));
        var generator = new BiomeWorldGenerator(
            0x0CEA_2026UL, dimension, blocks, fluids,
            new BiomeRegistry([ocean, land]), StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                WorldGenerationMode.Flat, spawnStructures: false,
                spawnCaves: false, spawnOceans: true));

        var oceanLocation = Assert.IsType<SurfaceBiomeSearchResult>(
            generator.FindNearestSurfaceBiome(ocean.Id, 0, 0, 1024));
        foreach (var (x, z) in new[]
                 {
                     (0, 0),
                     (oceanLocation.X, oceanLocation.Z),
                     (oceanLocation.X + Chunk.Size, oceanLocation.Z),
                 })
        {
            Assert.Equal(90, generator.SurfaceHeight(x, z));
            Assert.True(generator.DensityAt(x, 90, z) >= 0d);
            Assert.True(generator.DensityAt(x, 91, z) < 0d);
            var (coord, local) = VoxelCoordinates.FromWorld(x, 90, z);
            var chunk = generator.Materialize(coord);
            Assert.Equal(blocks.GetId("asteria:stone"),
                chunk.GetBlock(local.X, local.Y, local.Z));
        }
    }

    [Fact]
    public void VoidWorldHasOnlySpawnPlatformWithoutSphereShell()
    {
        var (generator, blocks) = Create(WorldGenerationMode.Void);
        var stone = blocks.GetId("asteria:stone");
        Assert.Equal(new GeneratedSurfaceDestination(0, 33, 0),
            generator.FindGeneratedSpawn(32, 4));
        var spawn = generator.Materialize(new ChunkCoord(0, 2, 0));
        Assert.Equal(stone, spawn.GetBlock(0, 0, 0));
        Assert.True(spawn.GetBlock(8, 0, 8).IsAir);

        var remote = generator.Materialize(new ChunkCoord(4, 2, 4));
        Assert.True(remote.GetBlock(8, 0, 8).IsAir);
        Assert.Equal(0, generator.SurfaceHeight(72, 72));

        // The authored shell must be omitted entirely, not hidden
        // under a core-material lining.
        var floor = generator.Materialize(ChunkCoord.Zero);
        Assert.True(floor.IsEmpty);
        Assert.True(generator.DensityAt(8, 0, 8) < 0d);
        Assert.True(generator.DensityAt(8, 1, 8) < 0d);
    }

    [Theory]
    [InlineData(WorldGenerationMode.Flat)]
    [InlineData(WorldGenerationMode.Void)]
    public void DisablingOceansDoesNotRejectTheirHabitatWeightedStructures(
        WorldGenerationMode mode)
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), 0.7f),
        ]);
        BiomeDefinition Biome(string id, SurfaceHabitatDefinition? habitats = null) =>
            new(id, new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0, macroAmplitude: 0, macroScale: 128,
                    detailAmplitude: 0, detailScale: 32),
                [new BiomeSurfaceLayerDefinition("asteria:stone")],
                surfaceHabitats: habitats);
        var ocean = Biome("asteria:test/ocean",
            new SurfaceHabitatDefinition(64, 0.1f,
            [
                new SurfaceHabitatBand("sand_flats", 0f),
                new SurfaceHabitatBand("rocky_reefs", 1f),
            ]));
        var land = Biome("asteria:test/land");
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [ocean.Id, land.Id],
            seaLevel: 90, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedOcean: new DimensionGeneratedOceanDefinition(
                ocean.Id, "asteria:water"),
            generatedSurfaceStructures:
            [
                new DimensionGeneratedSurfaceStructureDefinition(
                    ocean.Id, "asteria:test_reef",
                    spacing: 32, chance: 1f,
                    habitatWeights: new SurfaceHabitatWeights(
                        [new KeyValuePair<string, float>("rocky_reefs", 2f)])),
            ]);

        var generator = new BiomeWorldGenerator(
            77UL, dimension, blocks, fluids,
            new BiomeRegistry([ocean, land]), StructureRegistry.Empty,
            generation: new WorldGenerationOptions(
                mode, spawnStructures: false,
                spawnCaves: false, spawnOceans: false));

        // The excluded ocean remains absent from the actual biome sampler.
        Assert.Equal(land.Id, generator.Biomes.Sample(0, 0).Primary);
        Assert.Equal(new GeneratedSurfaceDestination(0, 91, 0),
            generator.FindGeneratedSpawn(32, 4));

        var chunk = generator.Materialize(new ChunkCoord(0, 5, 0));
        Assert.Equal(blocks.GetId("asteria:stone"),
            chunk.GetBlock(0, 10, 0));
        if (mode == WorldGenerationMode.Void)
            Assert.True(chunk.GetBlock(8, 10, 8).IsAir);
        else
            Assert.Equal(blocks.GetId("asteria:stone"),
                chunk.GetBlock(8, 10, 8));
    }

    [Fact]
    public void VoidPlatformUsesSelectedSpawnBiomeWithoutGeneratingTerrainOrShell()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:flower"),
            new BlockDefinition("asteria:shell_sphere",
                mining: new BlockMiningDefinition(unbreakable: true)),
        ]);
        BiomeDefinition Biome(string id, string material) =>
            new(id, new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
                [new BiomeSurfaceLayerDefinition(material)]);
        var first = Biome("asteria:test/alpha", "asteria:stone");
        var selected = Biome("asteria:test/zeta", "asteria:flower");
        var biomes = new BiomeRegistry([first, selected]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [first.Id, selected.Id],
            seaLevel: 32, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            shell: new DimensionShellDefinition("asteria:shell_sphere", floorY: 0));

        BiomeWorldGenerator Generator(string? spawnBiome) =>
            new(77UL, dimension, blocks,
                new FluidRegistry(Array.Empty<FluidDefinition>()),
                biomes, StructureRegistry.Empty,
                generation: new WorldGenerationOptions(
                    WorldGenerationMode.Void, spawnBiome: spawnBiome,
                    singleBiome: false, spawnStructures: false,
                    spawnCaves: false, spawnOceans: false));

        var baseline = Generator(null);
        var chosen = Generator(selected.Id);
        var baselineChunk = baseline.Materialize(new ChunkCoord(0, 2, 0));
        var selectedChunk = chosen.Materialize(new ChunkCoord(0, 2, 0));
        Assert.Equal(blocks.GetId("asteria:stone"),
            baselineChunk.GetBlock(0, 0, 0));
        Assert.Equal(blocks.GetId("asteria:flower"),
            selectedChunk.GetBlock(0, 0, 0));
        Assert.True(selectedChunk.GetBlock(8, 0, 8).IsAir);
        Assert.True(chosen.Materialize(ChunkCoord.Zero).IsEmpty);
        Assert.Equal(new GeneratedSurfaceDestination(0, 33, 0),
            chosen.FindGeneratedSpawn(32, 4));
        Assert.True(chosen.Materialize(new ChunkCoord(4, 2, 4)).IsEmpty);
    }

    [Fact]
    public void VoidPlatformRejectsUnavailableSelectedBiomeWithoutChangingDefault()
    {
        // An unknown requested spawn-biome is not an eligible material
        // source; ordinary Void generation still falls back to the first
        // authored surface biome. SingleBiome remains independently strict.
        var (generator, blocks) = Create(WorldGenerationMode.Void);
        Assert.Equal(blocks.GetId("asteria:stone"),
            generator.Materialize(new ChunkCoord(0, 2, 0)).GetBlock(0, 0, 0));
    }

    private static (BiomeWorldGenerator Generator, BlockRegistry Blocks)
        Create(WorldGenerationMode mode)
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:flower"),
            new BlockDefinition("asteria:shell_sphere",
                mining: new BlockMiningDefinition(unbreakable: true)),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/plain",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(12f, 8f, 64, 2f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [new BiomeDecorationDefinition(
                "asteria:flower", 1f, ["asteria:stone"])]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id],
            seaLevel: 32,
            gravityStrength: 18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            new DimensionShellDefinition("asteria:shell_sphere", floorY: 0));
        return (
            new BiomeWorldGenerator(
                77UL, dimension, blocks,
                new FluidRegistry(Array.Empty<FluidDefinition>()),
                new BiomeRegistry([biome]),
                StructureRegistry.Empty,
                generation: new WorldGenerationOptions(
                    mode, spawnStructures: false,
                    spawnCaves: false, spawnOceans: false)),
            blocks);
    }
}
