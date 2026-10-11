using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WallDecorationContentTests
{
    [Fact]
    public void WallSupportIsAnExplicitAuthoredFaceAndNotImplicitFloor()
    {
        var biome = BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/wall",
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[
            {"block":"asteria:wall_fern","chance":1,
             "surfaceBlocks":["asteria:stone"],"supportSurface":"wall"}
          ]
        }
        """);
        var rule = Assert.Single(biome.Decorations);
        Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);

        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_fern")
        ]);
        var field = new SurfaceDecorationField(17, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");

        Assert.True(field.HasWallDecorations);
        Assert.True(field.HasWallDecorationsFor(biome.Id));
        Assert.Equal(blocks.GetId("asteria:wall_fern"),
            field.BlockAt(sample, stone, 3, 5,
                new SurfacePlacementContext(40, 0),
                verticalY:40, supportSurface:DecorationSupportSurface.Wall));
        Assert.True(field.BlockAt(sample, stone, 3, 5,
            supportSurface:DecorationSupportSurface.Floor).IsAir);
        Assert.True(field.BlockAt(sample, blocks.GetId("asteria:wall_fern"), 3, 5,
            supportSurface:DecorationSupportSurface.Wall).IsAir);
        Assert.Throws<FormatException>(() =>
            BiomeDefinitionJson.Parse("""
            {
              "id":"asteria:test/badwall",
              "palette":{"default":[{"block":"asteria:stone"}]},
              "decorations":[{"block":"asteria:wall_fern","chance":1,
                "surfaceBlocks":["asteria:stone"],"supportSurface":"diagonal"}]
            }
            """));
    }

    [Fact]
    public void CaveAndGorgeWallFloraRequireRealWallMaterials()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        biomes.ValidateBlocks(blocks);

        foreach (var (biome, ids) in new[]
        {
            ("caverns", new[] {"cave_wall_moss","cave_wall_shelf_fungus"}),
            ("gorge", new[] {"gorge_cliff_rosette","gorge_cliff_lichen"})
        })
        {
            var ruleBiome = biomes.Get("asteria:overworld/" + biome);
            foreach (var id in ids)
            {
                var rule = Assert.Single(ruleBiome.Decorations,
                    d => d.Block == "asteria:" + id);
                Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
                Assert.NotNull(rule.Cluster);
                Assert.NotNull(rule.Conditions);
                Assert.All(rule.SurfaceBlocks,
                    support => Assert.Contains(support, new[] {
                        "asteria:stone", "asteria:stone_cobble"
                    }));
                var block = blocks.GetDefinition(blocks.GetId("asteria:" + id));
                Assert.True(block.UsesHorizontalFacing);
                Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
                Assert.False(block.IsCollidable);
                Assert.True(block.HasTag("support_wall"));
                Assert.Equal(4, block.PlacementFaces.Count);
                Assert.Equal("textures/objects/" + id + ".png",
                    block.Visual.Texture!.Value.Texture);
            }
        }
    }

    [Fact]
    public void WallDecorationsRemainDeterministicAndDoNotLeakToOtherBiomes()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var cave = biomes.Get("asteria:overworld/caverns");
        var gorge = biomes.Get("asteria:overworld/gorge");
        var field = new SurfaceDecorationField(771, [cave,gorge],blocks);
        var other = new SurfaceDecorationField(771, [cave,gorge],blocks);
        var stone = blocks.GetId("asteria:stone");
        var sample = new BiomeSample(gorge.Id,
            [new BiomeInfluence(gorge.Id, 1f)]);

        Assert.True(field.HasWallDecorationsFor(gorge.Id));
        Assert.True(field.HasWallDecorationsFor(cave.Id));
        for(var x=-16;x<=16;x++)
        for(var y=72;y<=86;y++)
        {
            var a=field.BlockAt(sample,stone,x,7,
                new SurfacePlacementContext(y,0),
                verticalY:y,supportSurface:DecorationSupportSurface.Wall);
            var b=other.BlockAt(sample,stone,x,7,
                new SurfacePlacementContext(y,0),
                verticalY:y,supportSurface:DecorationSupportSurface.Wall);
            Assert.Equal(a,b);
        }
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"packs","default","data",dir);
        return Directory.EnumerateFiles(path,"*.json")
            .OrderBy(file=>file,StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
