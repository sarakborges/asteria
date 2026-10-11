using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WallDecorationContentTests
{
    [Fact]
    public void LegacyWallJsonRemainsLoadableButCreatesNoSeparateBlock()
    {
        var biome = BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/old_wall",
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[
            {"block":"asteria:wall_moss","chance":1,
             "surfaceBlocks":["asteria:stone"],"supportSurface":"wall"}
          ]
        }
        """);
        var legacy = Assert.Single(biome.Decorations);
        Assert.Equal(DecorationSupportSurface.Wall, legacy.SupportSurface);
        Assert.False(legacy.IsLayer);

        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_moss")
        ]);
        var field = new SurfaceDecorationField(17, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");

        Assert.False(field.HasDecorations(sample));
        Assert.True(field.BlockAt(sample, stone, 3, 5,
            new SurfacePlacementContext(40, 0),
            verticalY: 40,
            supportSurface: DecorationSupportSurface.Wall).IsAir);
        Assert.True(field.BlockAt(sample, stone, 3, 5,
            supportSurface: DecorationSupportSurface.Floor).IsAir);

        Assert.Throws<FormatException>(() =>
            BiomeDefinitionJson.Parse("""
            {
              "id":"asteria:test/invalid",
              "palette":{"default":[{"block":"asteria:stone"}]},
              "decorations":[{"block":"asteria:wall_moss","chance":1,
                 "surfaceBlocks":["asteria:stone"],"supportSurface":"diagonal"}]
            }
            """));
    }

    [Fact]
    public void FlatCaveMossAndCliffLichenUseRealFaceLayers()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var layers = AttachedLayerRegistry.FromJson(ReadJson("layers"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateLayers(layers);

        foreach (var (biome, id) in new[]
        {
            ("caverns", "wall_moss"),
            ("gorge", "cliff_lichen"),
        })
        {
            var definition = biomes.Get("asteria:overworld/" + biome);
            var rule = Assert.Single(definition.Decorations,
                r => r.Block == "asteria:" + id);
            Assert.True(rule.IsLayer);
            Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
            Assert.Equal(["asteria:stone", "asteria:stone_cobble"],
                rule.SurfaceBlocks);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            var layer = layers.Get("asteria:" + id);
            Assert.Equal(BlockTint.None, layer.Tint);
            Assert.Equal(BlockRenderMode.Cutout, layer.RenderMode);
            Assert.Equal("textures/layers/" + id + ".png", layer.Texture);
            Assert.All(new[] {
                BlockFace.Left, BlockFace.Right, BlockFace.Front, BlockFace.Back
            }, face => Assert.True(layer.Supports(face)));
            Assert.False(layer.Supports(BlockFace.Top));
            Assert.False(layer.Supports(BlockFace.Bottom));
        }
    }

    [Fact]
    public void ProjectedWallFungiAndRosettesRemainSeparate3DContent()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        foreach (var (biome, id) in new[]
        {
            ("caverns", "wall_shelf_fungus"),
            ("gorge", "cliff_rosette"),
        })
        {
            var rule = Assert.Single(
                biomes.Get("asteria:overworld/" + biome).Decorations,
                r => r.Block == "asteria:" + id);
            Assert.False(rule.IsLayer);
            Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.UsesHorizontalFacing);
            Assert.True(block.HasTag("support_wall"));
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
        }
    }

    [Fact]
    public void WallLayerRulesRejectMissingOrIncompatibleDefinitions()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var cave = biomes.Get("asteria:overworld/caverns");
        var registry = new BiomeRegistry([cave]);
        Assert.Throws<ArgumentException>(() =>
            registry.ValidateLayers(new AttachedLayerRegistry([])));

        var missingFace = new AttachedLayerRegistry([
            new AttachedLayerDefinition(
                "asteria:wall_moss", "textures/layers/wall_moss.png",
                faces: [BlockFace.Front])
        ]);
        Assert.Throws<ArgumentException>(() =>
            registry.ValidateLayers(missingFace));
        registry.ValidateLayers(AttachedLayerRegistry.FromJson(ReadJson("layers")));
    }

    [Fact]
    public void PureWallLayerSelectionIsStableAndNeverCreatesABlock()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var layers = AttachedLayerRegistry.FromJson(ReadJson("layers"));
        var gorge = biomes.Get("asteria:overworld/gorge");
        var seed = 1457UL;
        var first = new SurfaceDecorationField(seed, [gorge], blocks, layers: layers);
        var again = new SurfaceDecorationField(seed, [gorge], blocks, layers: layers);
        var sample = new BiomeSample(gorge.Id,
            [new BiomeInfluence(gorge.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        var basalt = blocks.GetId("asteria:basalt");
        Assert.True(first.HasWallLayers);
        Assert.True(first.HasWallLayersFor(gorge.Id));
        var seen = 0;
        for (var x = -28; x <= 28; x++)
        for (var z = -28; z <= 28; z++)
        {
            var selected = first.WallLayerAt(sample, stone, x, 100, z);
            Assert.Equal(selected, again.WallLayerAt(sample, stone, x, 100, z));
            Assert.Null(first.WallLayerAt(sample, basalt, x, 100, z));
            Assert.True(first.BlockAt(sample, stone, x, z,
                supportSurface: DecorationSupportSurface.Wall).IsAir);
            if (selected is not null)
            {
                Assert.Equal("asteria:cliff_lichen", selected);
                seen++;
            }
        }
        Assert.True(seen > 0);
    }

    [Fact]
    public void ParserRejectsAmbiguousAndNonWallLayerDecorations()
    {
        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/bad_layers",
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[{"block":"asteria:stone","layer":"asteria:moss",
            "chance":1,"surfaceBlocks":["asteria:stone"],
            "supportSurface":"wall"}]
        }
        """));
        Assert.Throws<ArgumentException>(() => BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/bad_floor_layer",
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[{"layer":"asteria:moss",
            "chance":1,"surfaceBlocks":["asteria:stone"],
            "supportSurface":"floor"}]
        }
        """));
    }

    [Fact]
    public void LegacyWallDefinitionsDoNotRequireWallBlockPhysicsTags()
    {
        var biome = BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/old_wall",
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[{
            "block":"asteria:wall_moss","chance":1,
            "surfaceBlocks":["asteria:stone"],"supportSurface":"wall"
          }]
        }
        """);
        var biomes = new BiomeRegistry([biome]);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_moss", isCollidable: false)
        ]);
        biomes.ValidateBlocks(blocks);
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"packs","default","data",dir);
        return Directory.EnumerateFiles(path,"*.json")
            .OrderBy(file=>file,StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
