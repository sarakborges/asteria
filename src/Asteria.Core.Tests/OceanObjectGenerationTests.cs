using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanObjectGenerationTests
{
    private static readonly PackContentRegistry<ObjectDefinition> Objects =
        PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:seagrass","category":"foliage",
             "icon":"textures/objects/seagrass.png",
             "visual":{"type":"crossedSprite","texture":"textures/objects/seagrass.png",
                       "width":0.8,"height":0.78,"planes":4},
             "target":{"size":[0.8,0.78,0.8],"centerOffset":[0,0.39,0]},
             "placementFaces":["top"],"dropSelf":true}
            """,
            """
            {"id":"asteria:sea_anemone","category":"foliage",
             "icon":"textures/objects/sea_anemone.png",
             "visual":{"type":"crossedSprite","texture":"textures/objects/sea_anemone.png",
                       "width":0.8,"height":0.88,"planes":4,"alphaCutoff":0.5},
             "target":{"size":[0.8,0.88,0.8],"centerOffset":[0,0.44,0]},
             "placementFaces":["top"],"dropSelf":false}
            """
        ], ObjectDefinition.Parse, obj => obj.Id);

    private static (BiomeWorldGenerator Generator, FluidRegistry Fluids) Generator(
        float baseHeight, bool ocean, string objectId = "asteria:seagrass")
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), .7f)
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/ocean",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(baseHeight, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition(objectId, 1f,
                    ["asteria:stone"], fluidPlacement: DecorationFluidPlacement.Submerged,
                    isObject: true)
            ]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id],
            seaLevel: 32, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f),
            generatedOcean: ocean
                ? new DimensionGeneratedOceanDefinition(biome.Id, "asteria:water")
                : null);
        return (new BiomeWorldGenerator(
            17, dimension, blocks, fluids, new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(spawnCaves: false),
            objects: Objects), fluids);
    }

    [Theory]
    [InlineData(-18f, "asteria:seagrass")]
    [InlineData(-17f, "asteria:seagrass")]
    [InlineData(-18f, "asteria:sea_anemone")]
    [InlineData(-17f, "asteria:sea_anemone")]
    public void GeneratedObjectIsAnchoredToSeabedAndPreservesItsWaterSource(
        float baseHeight, string objectId)
    {
        var (generator, fluids) = Generator(baseHeight, ocean: true, objectId);
        var surfaceY = generator.SurfaceHeight(8, 8);
        var supportCoord = new ChunkCoord(0, surfaceY / Chunk.Size, 0);
        var supportChunk = generator.Materialize(supportCoord);
        var localY = surfaceY % Chunk.Size;
        var obj = Assert.Single(supportChunk.ObjectsAt(8, localY, 8));
        Assert.Equal(objectId, obj.Id);
        Assert.Equal(BlockFace.Top, obj.Face);
        Assert.False(supportChunk.GetCell(8, localY, 8).IsEmpty);
        var waterY = surfaceY + 1;
        var waterChunk = generator.Materialize(
            new ChunkCoord(0, waterY / Chunk.Size, 0));
        Assert.True(waterChunk.GetCell(8, waterY % Chunk.Size, 8).IsEmpty);
        Assert.Equal(FluidCell.Source(fluids.GetId("asteria:water")),
            waterChunk.GetFluid(8, waterY % Chunk.Size, 8));
        Assert.Equal(obj,
            Assert.Single(generator.Materialize(supportCoord).ObjectsAt(8, localY, 8)));
    }

    [Theory]
    [InlineData("asteria:seagrass")]
    [InlineData("asteria:sea_anemone")]
    public void SubmergedRuleDoesNotCreateObjectsOnDryTerrain(string objectId)
    {
        var (generator, _) = Generator(-18f, ocean: false, objectId);
        var surfaceY = generator.SurfaceHeight(8, 8);
        var chunk = generator.Materialize(
            new ChunkCoord(0, surfaceY / Chunk.Size, 0));
        Assert.Empty(chunk.ObjectsAt(8, surfaceY % Chunk.Size, 8));
    }

    [Fact]
    public void ObjectRuleWithoutMatchingRegistryIdentityFailsValidation()
    {
        var biome = new BiomeDefinition(
            "asteria:test/ocean", new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(-18f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition("asteria:missing", 1f,
                    ["asteria:stone"], isObject: true)
            ]);
        var biomes = new BiomeRegistry([biome]);
        Assert.Throws<ArgumentException>(() => biomes.ValidateObjects(Objects));
    }
}
