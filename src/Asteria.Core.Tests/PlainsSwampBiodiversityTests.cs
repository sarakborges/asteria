using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlainsSwampBiodiversityTests
{
    private static readonly string[] PlainsFlowers =
    [
        "daisy", "clover", "aster_purple", "seedheads_meadow"
    ];

    [Fact]
    public void PlainsMeadowObjectsStayInGrassAndPreserveOpenHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var plains = biomes.Get("asteria:overworld/plains");

        Assert.Equal(9, plains.Decorations.Count);
        Assert.Equal("asteria:block_grass", plains.SurfaceLayers[0].Block);
        foreach (var name in PlainsFlowers)
        {
            var rule = Assert.Single(plains.Decorations,
                decoration => decoration.Block == "asteria:" + name);
            Assert.Equal(["asteria:block_grass"], rule.SurfaceBlocks);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.HabitatWeights!.For("open") >
                        rule.HabitatWeights.For("rocky"));
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.True(rule.IsObject);
            Assert.False(blocks.TryGetId(rule.Block, out _));
            var flower = objects.Get(rule.Block);
            Assert.True(flower.Supports(BlockFace.Top));
            Assert.Equal(name == "clover"
                    ? ObjectVisualKind.ExtrudedSprite
                    : ObjectVisualKind.CrossedSprite,
                flower.Visual.Kind);
            Assert.Equal("textures/objects/" + name + ".png",
                flower.Visual.Texture);
        }
    }

    [Fact]
    public void SwampCattailsAndLilyPadsKeepTheirWaterPlacementRules()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var swamp = biomes.Get("asteria:overworld/swamp");

        Assert.Equal(7, swamp.Decorations.Count);
        Assert.Equal("asteria:mud", swamp.SurfaceLayers[0].Block);

        foreach (var (name, relation, maxDistance) in new[]
        {
            ("cattail", DecorationFluidRelation.Nearby, (int?)2),
            ("pad_lily", DecorationFluidRelation.Below, (int?)null)
        })
        {
            var rule = Assert.Single(swamp.Decorations,
                decoration => decoration.Block == "asteria:" + name);
            Assert.True(rule.IsObject);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Equal("asteria:water", rule.FluidRequirement!.Fluid);
            Assert.Equal(relation, rule.FluidRequirement.Relation);
            Assert.Equal(maxDistance, rule.FluidRequirement.MaxDistance);
            Assert.NotEmpty(rule.SurfaceBlocks);
            var definition = objects.Get(rule.Block);
            Assert.True(definition.Supports(BlockFace.Top));
            Assert.Equal(name == "pad_lily"
                    ? ObjectVisualKind.ExtrudedSprite
                    : ObjectVisualKind.CrossedSprite,
                definition.Visual.Kind);
        }
    }

    [Fact]
    public void WillowRemainsButRemovedReedBedsAndMossStumpsHaveNoReferences()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimensions = DimensionRegistry.FromJson(ReadPackJson("dimensions"));
        var swampRules = dimensions.Get(DimensionId.Overworld)
            .GeneratedSurfaceStructures
            .Where(rule => rule.Biome == "asteria:overworld/swamp")
            .ToArray();

        Assert.DoesNotContain(swampRules,
            rule => rule.Structure is "asteria:bed_reed_swamp" or
                "asteria:stump_swamp_moss");
        var willowRule = Assert.Single(swampRules,
            rule => rule.Structure == "asteria:tree_willow");
        Assert.NotNull(willowRule.HabitatWeights);
        Assert.True(willowRule.HabitatWeights!.For("willow_grove") >
                    willowRule.HabitatWeights.For("open_mire"));

        var trees = structures.ResolveReference("asteria:tree_willow");
        Assert.Equal(2, trees.Count);
        Assert.All(trees, tree =>
        {
            Assert.Contains(tree.Voxels,
                voxel => voxel.Block == "asteria:log_willow");
            Assert.Contains(tree.Voxels,
                voxel => voxel.Block == "asteria:leaf_willow");
        });
        Assert.Throws<KeyNotFoundException>(() =>
            structures.ResolveReference("asteria:bed_reed_swamp"));
        Assert.Throws<KeyNotFoundException>(() =>
            structures.ResolveReference("asteria:stump_swamp_moss"));
    }

    [Fact]
    public void RetainedMeadowAndSwampAssetsUseDistinctTexturePaths()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in PlainsFlowers.Concat(["cattail", "pad_lily"]))
        {
            var texture = objects.Get("asteria:" + name).Visual.Texture!;
            Assert.Equal("textures/objects/" +
                (name == "pad_lily" ? "lily_pad" : name) + ".png", texture);
            Assert.True(seen.Add(texture));
        }

        var marshFern = blocks.GetDefinition(blocks.GetId("asteria:fern_marsh"));
        const string fernTexture = "textures/objects/fern_marsh.png";
        Assert.Equal(fernTexture, marshFern.Visual.Texture!.Value.Texture);
        Assert.True(seen.Add(fernTexture));

        var willow = blocks.GetDefinition(blocks.GetId("asteria:log_willow"));
        const string willowTexture = "textures/blocks/trees/willow/side.png";
        Assert.Equal(willowTexture,
            Assert.Single(willow.Textures.ForFace(BlockFace.Left)).Texture);
        Assert.True(seen.Add(willowTexture));
        Assert.Equal(8, seen.Count);
    }

    [Fact]
    public void NewMeadowFloraSelectionIsSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var plains = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/plains");
        var sample = new BiomeSample(plains.Id,
            [new BiomeInfluence(plains.Id, 1f)]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var first = new SurfaceDecorationField(29801, [plains], blocks,
            objects: objects);
        var again = new SurfaceDecorationField(29801, [plains], blocks,
            objects: objects);
        var grass = blocks.GetId("asteria:block_grass");
        var basalt = blocks.GetId("asteria:basalt");
        var valid = new SurfacePlacementContext(94, 0);
        for (var z = -24; z <= 24; z++)
        for (var x = -24; x <= 24; x++)
        {
            Assert.Equal(first.BlockAt(sample, grass, x, z, valid),
                again.BlockAt(sample, grass, x, z, valid));
            Assert.Equal(first.ObjectAt(sample, grass, x, z, valid),
                again.ObjectAt(sample, grass, x, z, valid));
            Assert.True(first.BlockAt(sample, basalt, x, z, valid).IsAir);
            Assert.Null(first.ObjectAt(sample, basalt, x, z, valid));
        }
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var folder = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
