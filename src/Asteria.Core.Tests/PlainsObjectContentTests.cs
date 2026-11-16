using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlainsObjectContentTests
{
    private static readonly string[] Flowers =
        ["wildflower_blue", "wildflower_yellow"];

    [Fact]
    public void PlainsWildflowersAreOnlyAuthoredAsObjects()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);

        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        var plains = biomes.Get("asteria:overworld/plains");
        foreach (var name in Flowers)
        {
            var id = $"asteria:{name}";
            var decoration = Assert.Single(plains.Decorations, rule => rule.Block == id);
            Assert.True(decoration.IsObject);
            Assert.False(decoration.IsLayer);
            Assert.False(blocks.TryGetId(id, out _));

            var definition = objects.Get(id);
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.Equal($"textures/objects/{name}.png", definition.Visual.Texture);
            Assert.True(definition.Supports(BlockFace.Top));
            Assert.False(definition.DropSelf);
            Assert.True(definition.WindSway);
        }
    }

    [Theory]
    [InlineData("asteria:aa_flower")]
    [InlineData("asteria:zz_flower")]
    public void BlockAndObjectRulesDoNotMaskOneAnother(string blockId)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition(blockId)
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:wildflower_blue","category":"foliage",
             "icon":"textures/objects/wildflower_blue.png",
             "visual":{"type":"crossedSprite",
                       "texture":"textures/objects/wildflower_blue.png",
                       "width":0.7,"height":0.79,"planes":4}}
            """
        ], ObjectDefinition.Parse, obj => obj.Id);
        var biome = new BiomeDefinition(
            "asteria:test/flower",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(80f, 0f, 64, 0f, 32),
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition(blockId, 1f, ["asteria:stone"]),
                new BiomeDecorationDefinition("asteria:wildflower_blue", 1f,
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
        Assert.Equal("asteria:wildflower_blue",
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
