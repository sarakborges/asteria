using System.Numerics;
using System.Text.Json;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureNaturalSpawnTests
{
    private static PackContentRegistry<CreatureDefinition> Creatures() =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id":"asteria:test_slime",
              "model":"models/creatures/slime_blob/slime_blob.glb",
              "health":10,
              "maxPerType":2,
              "collider":{"size":[0.78,0.84,0.78],"centerOffset":[0,0.42,0]},
              "jumpSpeed":5,
              "moveSpeed":1.5,
              "jumpInterval":2,
              "anticipationSeconds":0.24,
              "landingSeconds":0.34,
              "animations":{"idle":"Idle"},
              "textures":{"SlimeFace":"textures/creatures/slime/face.png"},
              "naturalSpawn":{"surfaceBiomes":["asteria:test/flat"],"weight":1}
            }
            """],
            CreatureDefinition.Parse, definition => definition.Id);

    private static (
        BiomeWorldGenerator Generator,
        BiomeRegistry Biomes,
        VoxelWorld World,
        BlockRegistry Blocks) FlatWorld()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var biome = new BiomeDefinition(
            "asteria:test/flat",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")]);
        var biomes = new BiomeRegistry([biome]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id],
            32,
            18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f));
        var generator = new BiomeWorldGenerator(
            77, dimension, blocks,
            new FluidRegistry(Array.Empty<FluidDefinition>()),
            biomes);
        return (generator, biomes, new VoxelWorld(), blocks);
    }

    [Fact]
    public void AuthoredNaturalSpawnDefaultsOffAndRejectsBadBiomes()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "creatures");
        var slime = CreatureDefinition.Parse(
            File.ReadAllText(Path.Combine(root, "slime.json")));
        Assert.Contains("asteria:overworld/plains",
            slime.NaturalSpawn!.SurfaceBiomes);
        Assert.Contains("asteria:overworld/swamp",
            slime.NaturalSpawn.SurfaceBiomes);
        var aqua = CreatureDefinition.Parse(
            File.ReadAllText(Path.Combine(root, "slime_aqua.json")));
        Assert.Null(aqua.NaturalSpawn);

        using var invalid = JsonDocument.Parse(
            """{"naturalSpawn":{"surfaceBiomes":["asteria:test/flat","asteria:test/flat"]}}""");
        Assert.Throws<FormatException>(() =>
            CreatureNaturalSpawnDefinition.Parse(invalid.RootElement));
    }

    [Fact]
    public void CandidateLocationIsDeterministicAndBounded()
    {
        var observer = new Vector3(7.5f, 33f, -4.5f);
        var first = CreatureNaturalSpawnRuntime.SampleColumn(77, 400, observer);
        Assert.Equal(first,
            CreatureNaturalSpawnRuntime.SampleColumn(77, 400, observer));
        Assert.NotNull(first);
        Assert.InRange(Math.Abs(first!.Value.X - 7), 0, 33);
        Assert.InRange(Math.Abs(first.Value.Z + 5), 0, 33);
        Assert.True(Math.Abs(first.Value.X - 7) >= 18 ||
            Math.Abs(first.Value.Z + 5) >= 18);
        Assert.Null(CreatureNaturalSpawnRuntime.SampleColumn(
            77, 400, new Vector3(float.NaN, 33, 1)));
    }

    [Fact]
    public void SpawnWaitsForIntervalGameRuleAndLoadedGround()
    {
        var (generator, biomes, world, blocks) = FlatWorld();
        var creatures = Creatures();
        var runtime = new CreatureRuntime(creatures);
        var scheduler = new CreatureNaturalSpawnRuntime(creatures, biomes);
        var observer = new Vector3(0, 33, 0);
        Assert.False(scheduler.TryAdvance(
            0, 40, 77, observer, true, generator, world, blocks, runtime));
        Assert.Equal(400UL, scheduler.NextAttemptTick);

        Assert.False(scheduler.TryAdvance(
            400, 40, 77, observer, false, generator, world, blocks, runtime));
        Assert.Equal(800UL, scheduler.NextAttemptTick);

        var site = CreatureNaturalSpawnRuntime.SampleColumn(77, 800, observer)!.Value;
        var destination = generator.FindGeneratedSurfaceDestination(
            site.X, site.Z, 0)!.Value;
        var support = new WorldVoxelCoord(
            destination.X, destination.Y - 1, destination.Z);
        var coord = VoxelCoordinates.FromWorld(
            support.X, support.Y, support.Z).Chunk;
        world.InsertChunk(coord, new Chunk());
        Assert.True(world.SetBlockAt(support,
            blocks.GetId("asteria:stone"), out _));

        // Retiring a Sphere preserves its scheduled due tick.
        var restoredScheduler = new CreatureNaturalSpawnRuntime(
            creatures, biomes, scheduler.NextAttemptTick);
        Assert.True(restoredScheduler.TryAdvance(
            800, 40, 77, observer, true, generator, world, blocks, runtime));
        var spawned = Assert.Single(runtime.ActiveCreatures);
        Assert.Equal("asteria:test_slime", spawned.DefinitionId);
        Assert.Equal(destination.X + 0.5f, spawned.Position.X);
        Assert.Equal(destination.Z + 0.5f, spawned.Position.Z);
        Assert.Equal(1200UL, restoredScheduler.NextAttemptTick);
        Assert.False(restoredScheduler.TryAdvance(
            801, 40, 77, observer, true, generator, world, blocks, runtime));
    }

    [Fact]
    public void WrongBiomeReferencesFailDuringSchedulerComposition()
    {
        var (_, biomes, _, _) = FlatWorld();
        var registry = PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id":"asteria:slime",
              "model":"slime.glb","health":10,"maxPerType":1,
              "collider":{"size":[1,1,1],"centerOffset":[0,0.5,0]},
              "jumpSpeed":1,"moveSpeed":1,"jumpInterval":1,
              "anticipationSeconds":0,"landingSeconds":0,
              "animations":{},"textures":{},
              "naturalSpawn":{"surfaceBiomes":["asteria:missing/biome"]}
            }
            """], CreatureDefinition.Parse, creature => creature.Id);
        Assert.Throws<KeyNotFoundException>(() =>
            new CreatureNaturalSpawnRuntime(registry, biomes));
    }
}
