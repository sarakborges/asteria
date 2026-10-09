using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SubmergedDecorationTests
{
    [Fact]
    public void FluidPlacementParsesAndRejectsUnknownValues()
    {
        const string json = """
        {
          "id": "asteria:test/ocean",
          "surfaceLayout": {},
          "surfaceTerrain": {
            "type": "rolling", "baseHeight": -18,
            "amplitude": 0, "scale": 0.01,
            "detailAmplitude": 0, "detailScale": 0.03
          },
          "palette": { "default": [{ "block": "asteria:stone" }] },
          "decorations": [
            { "block": "asteria:dry", "chance": 1, "surfaceBlocks": ["asteria:stone"] },
            { "block": "asteria:reef", "chance": 1, "surfaceBlocks": ["asteria:stone"], "fluidPlacement": "submerged" },
            { "block": "asteria:any", "chance": 1, "surfaceBlocks": ["asteria:stone"], "fluidPlacement": "any" }
          ]
        }
        """;
        var decorations = BiomeDefinitionJson.Parse(json).Decorations;
        Assert.Equal(DecorationFluidPlacement.Dry, decorations[0].FluidPlacement);
        Assert.Equal(DecorationFluidPlacement.Submerged, decorations[1].FluidPlacement);
        Assert.Equal(DecorationFluidPlacement.Any, decorations[2].FluidPlacement);
        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse(
            json.Replace("\"submerged\"", "\"wet\"", StringComparison.Ordinal)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BiomeDecorationDefinition(
            "asteria:reef", 1f, ["asteria:stone"],
            fluidPlacement: (DecorationFluidPlacement)123));
    }

    [Fact]
    public void DecoratorsChooseWetOrDryCellWithoutBiomeSpecificRules()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dry"),
            new BlockDefinition("asteria:reef"),
        ]);
        var biome = OceanBiome();
        var field = new SurfaceDecorationField(17, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");

        Assert.Equal(blocks.GetId("asteria:dry"),
            field.BlockAt(sample, stone, 2, 3));
        Assert.Equal(blocks.GetId("asteria:reef"),
            field.BlockAt(sample, stone, 2, 3, submerged: true));
    }

    [Fact]
    public void OceanFloorDecoratorIsSubmergedAndLeavesNeighboringWaterIntact()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dry"),
            new BlockDefinition("asteria:reef"),
        ]);
        var fluids = new FluidRegistry(
        [
            new FluidDefinition("asteria:water",
                new FluidColor(0, 96, 255), 0.7f),
        ]);
        var biome = OceanBiome();
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id],
            seaLevel: 32,
            gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f),
            generatedOcean: new DimensionGeneratedOceanDefinition(
                biome.Id, "asteria:water"));
        var generator = new BiomeWorldGenerator(
            17, dimension, blocks, fluids,
            new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(spawnCaves: false));

        var y = generator.SurfaceHeight(8, 8) + 1;
        Assert.True(y < dimension.SeaLevel);
        var coord = new ChunkCoord(0, y / Chunk.Size, 0);
        var chunk = generator.Materialize(coord);
        var localY = y % Chunk.Size;
        Assert.Equal(blocks.GetId("asteria:reef"),
            chunk.GetBlock(8, localY, 8));
        Assert.True(chunk.GetFluid(8, localY, 8).IsEmpty);
        // Even a fully decorated seabed has water directly above it.
        var aboveY = y + 1;
        var above = generator.Materialize(
            new ChunkCoord(0, aboveY / Chunk.Size, 0));
        Assert.Equal(fluids.GetId("asteria:water"),
            above.GetFluid(8, aboveY % Chunk.Size, 8).Fluid);
        Assert.Equal(blocks.GetId("asteria:reef"),
            generator.Materialize(coord).GetBlock(8, localY, 8));
    }

    private static BiomeDefinition OceanBiome() => new(
        "asteria:test/ocean",
        new BiomeSurfaceLayoutDefinition(),
        new BiomeTerrainDefinition(-18f, 0f, 64, 0f, 32),
        [new BiomeSurfaceLayerDefinition("asteria:stone")],
        decorations:
        [
            new BiomeDecorationDefinition(
                "asteria:dry", 1f, ["asteria:stone"]),
            new BiomeDecorationDefinition(
                "asteria:reef", 1f, ["asteria:stone"],
                fluidPlacement: DecorationFluidPlacement.Submerged),
        ]);
}
