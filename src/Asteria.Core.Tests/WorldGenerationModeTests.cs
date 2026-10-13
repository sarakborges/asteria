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
    public void VoidWorldHasOnlySpawnPlatformAndConcealedSphereShell()
    {
        var (generator, blocks) = Create(WorldGenerationMode.Void);
        var stone = blocks.GetId("asteria:stone");
        var shell = blocks.GetId("asteria:sphere_shell");

        Assert.Equal(new GeneratedSurfaceDestination(0, 33, 0),
            generator.FindGeneratedSpawn(32, 4));
        var spawn = generator.Materialize(new ChunkCoord(0, 2, 0));
        Assert.Equal(stone, spawn.GetBlock(0, 0, 0));
        Assert.True(spawn.GetBlock(8, 0, 8).IsAir);

        var remote = generator.Materialize(new ChunkCoord(4, 2, 4));
        Assert.True(remote.GetBlock(8, 0, 8).IsAir);
        Assert.Equal(0, generator.SurfaceHeight(72, 72));

        var floor = generator.Materialize(ChunkCoord.Zero);
        Assert.Equal(shell, floor.GetBlock(8, 0, 8));
        Assert.Equal(stone, floor.GetBlock(8, 1, 8));
        Assert.True(floor.GetBlock(8, 2, 8).IsAir);
    }

    private static (BiomeWorldGenerator Generator, BlockRegistry Blocks)
        Create(WorldGenerationMode mode)
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:sphere_shell",
                mining: new BlockMiningDefinition(unbreakable: true)),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/plain",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(12f, 8f, 64, 2f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")]);
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
            new DimensionShellDefinition("asteria:sphere_shell", floorY: 0));
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
