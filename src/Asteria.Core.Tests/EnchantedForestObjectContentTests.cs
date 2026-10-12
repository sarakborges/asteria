using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class EnchantedForestObjectContentTests
{
    private static readonly string[] MushroomNames =
        ["mushroom_blue", "mushroom_pink", "mushroom_red"];

    [Fact]
    public void EnchantedForestMushroomsAreOnlyAuthoredAsObjects()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);

        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        var forest = biomes.Get("asteria:overworld/enchanted_forest");
        foreach (var name in MushroomNames)
        {
            var id = $"asteria:{name}";
            var decoration = Assert.Single(forest.Decorations,
                rule => rule.Block == id);
            Assert.True(decoration.IsObject);
            Assert.False(decoration.IsLayer);
            Assert.False(blocks.TryGetId(id, out _));

            var definition = objects.Get(id);
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.Equal($"textures/objects/mushrooms/{name["mushroom_".Length..]}.png",
                definition.Visual.Texture);
            Assert.True(definition.Supports(BlockFace.Top));
            Assert.True(definition.DropSelf);
        }
    }

    [Theory]
    [InlineData("asteria:aa_flower")]
    [InlineData("asteria:zz_flower")]
    public void MixedBlockAndObjectRulesResolveTheirOwnKinds(string blockId)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition(blockId)
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:mushroom_blue","category":"foliage",
             "icon":"textures/objects/mushrooms/blue.png",
             "visual":{"type":"crossedSprite",
                       "texture":"textures/objects/mushrooms/blue.png",
                       "width":0.72,"height":0.62,"planes":2}}
            """
        ], ObjectDefinition.Parse, obj => obj.Id);
        var biome = new BiomeDefinition(
            "asteria:test/flower",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(80f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition(blockId, 1f, ["asteria:stone"]),
                new BiomeDecorationDefinition("asteria:mushroom_blue", 1f,
                    ["asteria:stone"], isObject: true)
            ]);
        var field = new SurfaceDecorationField(2026, [biome], blocks,
            objects: objects);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var surface = blocks.GetId("asteria:stone");
        var placement = new SurfacePlacementContext(80, 0);

        Assert.Equal(blocks.GetId(blockId),
            field.BlockAt(sample, surface, 1, 2, suppliedPlacement: placement));
        Assert.Equal("asteria:mushroom_blue",
            field.ObjectAt(sample, surface, 1, 2, placement: placement));
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs", "default",
            "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
