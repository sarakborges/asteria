using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidSupportedDecorationTests
{
    [Fact]
    public void DefaultSwampAuthorsNearWaterAndAboveWaterPlacement()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateFluids(fluids);
        var swamp = biomes.Get("asteria:overworld/swamp");
        var cattail = Assert.Single(swamp.Decorations, d => d.Block == "asteria:cattail");
        var lily = Assert.Single(swamp.Decorations, d => d.Block == "asteria:lily_pad");
        Assert.Equal(DecorationFluidRelation.Nearby, cattail.FluidRequirement!.Relation);
        Assert.Equal(2, cattail.FluidRequirement.MaxDistance);
        Assert.Equal("asteria:water", cattail.FluidRequirement.Fluid);
        Assert.Equal(DecorationFluidRelation.Below, lily.FluidRequirement!.Relation);
        Assert.Equal(0, lily.FluidRequirement.MaxDistance);
        Assert.Equal("asteria:water", lily.FluidRequirement.Fluid);
        Assert.NotNull(cattail.HabitatWeights);
        Assert.NotNull(lily.HabitatWeights);
        Assert.Equal(BlockVisualKind.CrossedSprite,
            blocks.GetDefinition(blocks.GetId(cattail.Block)).Visual.Kind);
        Assert.Equal(BlockVisualKind.GroundSprite,
            blocks.GetDefinition(blocks.GetId(lily.Block)).Visual.Kind);
    }

    [Fact]
    public void GenericFluidRequirementsRejectInvalidRadiusAndMissingFluid()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDecorationFluidRequirement(
                "asteria:water", DecorationFluidRelation.Nearby, 0));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDecorationFluidRequirement(
                "asteria:water", DecorationFluidRelation.Below, 1));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDecorationFluidRequirement(
                "asteria:water", DecorationFluidRelation.Nearby, 5));
    }

    [Fact]
    public void NearbyRuleRequiresMatchingFluidAndNeverSpawnsOnWater()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:mud"),
            new BlockDefinition("asteria:cattail"),
            new BlockDefinition("asteria:lily_pad")
        ]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), 0.7f)
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/swamp",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:mud")],
            decorations: [
                new BiomeDecorationDefinition("asteria:cattail", 1f,
                    ["asteria:mud"], fluidRequirement:
                    new BiomeDecorationFluidRequirement(
                        "asteria:water", DecorationFluidRelation.Nearby, 2)),
                new BiomeDecorationDefinition("asteria:lily_pad", 1f,
                    ["asteria:mud"], fluidRequirement:
                    new BiomeDecorationFluidRequirement(
                        "asteria:water", DecorationFluidRelation.Below)),
            ]);
        var field = new SurfaceDecorationField(42, [biome], blocks, fluids: fluids);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var mud = blocks.GetId("asteria:mud");
        var water = fluids.GetId("asteria:water");
        Assert.True(field.BlockAt(sample, mud, 3, 4, supportY: 90,
            nearbyFluid: (f, radius, x, y, z) => false).IsAir);
        Assert.Equal(blocks.GetId("asteria:cattail"),
            field.BlockAt(sample, mud, 3, 4, supportY: 90,
                nearbyFluid: (f, radius, x, y, z) =>
                    f == water && radius == 2 && x == 3 && y == 90 && z == 4));
        Assert.True(field.BlockAt(sample, mud, 3, 4,
            onFluidSurface: true).IsAir);
        Assert.Equal(blocks.GetId("asteria:lily_pad"),
            field.BlockAt(sample, mud, 3, 4,
                onFluidSurface: true, fluidBelow: water));
    }

    [Fact]
    public void LilyPadIsMaterializedAboveSwampWaterRatherThanDisplacingIt()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:mud"),
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:lily_pad")
        ]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water", new FluidColor(0, 96, 255), 0.7f)
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/swamp",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0f, 0f, 64, 0f, 32),
            decorations: [
                new BiomeDecorationDefinition("asteria:lily_pad", 1f,
                    ["asteria:mud"], fluidRequirement:
                    new BiomeDecorationFluidRequirement(
                        "asteria:water", DecorationFluidRelation.Below))
            ],
            palette: new BiomePaletteDefinition(
                [new BiomeSurfaceLayerDefinition("asteria:mud", 2),
                 new BiomeSurfaceLayerDefinition("asteria:stone")],
                surfaceMosaic: new BiomeSurfaceMosaicDefinition(
                    24, 8, 0,
                    [new BiomeSurfaceMosaicEntryDefinition("asteria:mud", null, 0.0001f),
                     new BiomeSurfaceMosaicEntryDefinition(null, "asteria:water", 1000f)])));
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"), [biome.Id],
            seaLevel: 90, gravityStrength: 18f,
            spawn: new DimensionSpawnDefinition(0, 0),
            environment: new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255), 1f,
                new DimensionColor(0, 0, 0), 0f));
        var generator = new BiomeWorldGenerator(
            42, dimension, blocks, fluids, new BiomeRegistry([biome]),
            StructureRegistry.Empty,
            generation: new WorldGenerationOptions(spawnCaves: false));
        var chunk = generator.Materialize(new ChunkCoord(0, 5, 0));
        var water = fluids.GetId("asteria:water");
        var lily = blocks.GetId("asteria:lily_pad");
        var observed = 0;
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            if (chunk.GetFluid(x, 10, z).Fluid != water)
                continue;
            Assert.Equal(lily, chunk.GetBlock(x, 11, z));
            Assert.True(chunk.GetBlock(x, 10, z).IsAir);
            observed++;
        }
        Assert.True(observed > 0);
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal).Select(File.ReadAllText);
    }
}
