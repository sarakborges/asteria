using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectBiomeTintTests
{
    private static IEnumerable<string> ReadJson(string category) =>
        Directory.EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "packs", "default",
                    "data", category), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

    [Fact]
    public void GrassUsesTheFoliageChannelAndUniqueObjectIdentity()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        Assert.False(blocks.TryGetId("asteria:grass", out _));
        var grass = objects.Get("asteria:grass");
        Assert.Equal(BlockTint.Foliage, grass.Tint);
        Assert.Equal(BlockPreviewColor.Parse("6f9f50"), grass.TintFallback);
        Assert.Equal(ObjectVisualKind.CrossedSprite, grass.Visual.Kind);
        Assert.Equal(4, grass.Visual.Planes);
        Assert.Equal(.72f, grass.Visual.Width);
        Assert.Equal(.62f, grass.Visual.Height);
        Assert.True(grass.WindSway);
        Assert.False(grass.DropSelf);
        Assert.False(grass.Unlit);
        Assert.True(Assert.Single(biomes.Get("asteria:overworld/plains")
            .Decorations, rule => rule.Block == grass.Id).IsObject);
    }

    [Fact]
    public void TheSameGrassObjectUsesDifferentAuthoredFoliageColorsPerBiome()
    {
        var all = BiomeRegistry.FromJson(ReadJson("biomes"));
        var grass = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id)
            .Get("asteria:grass");

        Vector3 ColorFor(string id)
        {
            var biome = all.Get(id);
            var field = new BiomeTintField(
                new BiomeField(123UL, [id], all), [biome]);
            return field.SampleChunkGrid(0, 0).Resolve(
                grass.Tint, grass.TintFallback, 8, 8);
        }

        var plains = ColorFor("asteria:overworld/plains");
        var swamp = ColorFor("asteria:overworld/swamp");
        Assert.InRange(plains.X, 60f / 255f, 62f / 255f);
        Assert.InRange(plains.Y, 178f / 255f, 180f / 255f);
        Assert.InRange(plains.Z, 40f / 255f, 42f / 255f);
        Assert.InRange(swamp.X, 70f / 255f, 72f / 255f);
        Assert.InRange(swamp.Y, 91f / 255f, 93f / 255f);
        Assert.InRange(swamp.Z, 58f / 255f, 60f / 255f);
        Assert.NotEqual(plains, swamp);
        Assert.Equal(Vector3.One, new BiomeTintField(
            new BiomeField(123UL, ["asteria:overworld/plains"], all),
            [all.Get("asteria:overworld/plains")]).SampleChunkGrid(0, 0)
            .Resolve(BlockTint.None, grass.TintFallback, 8, 8));
    }

    [Fact]
    public void AuthoredObjectTintUsesTheSameValidationAsBlocks()
    {
        const string template = """
            {"id":"asteria:tint_test","category":"foliage",
             "icon":"textures/objects/grass.png",
             "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png",
                       "width":0.7,"height":0.6,"planes":2},
             "tint":"foliage","previewColor":"6f9f50"}
            """;
        var obj = ObjectDefinition.Parse(template);
        Assert.Equal(BlockTint.Foliage, obj.Tint);
        Assert.Equal(BlockPreviewColor.Parse("6f9f50"), obj.TintFallback);
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(template.Replace("\"foliage\"", "\"unknown\"")));
        Assert.Throws<FormatException>(() =>
            ObjectDefinition.Parse(template.Replace("6f9f50", "not-color")));
        Assert.Equal(BlockTint.None,
            ObjectDefinition.Parse(template.Replace("\"tint\":\"foliage\"", "\"tint\":\"none\"")).Tint);
    }
}
