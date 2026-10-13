using Asteria.Core.Content;
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
          "volumeLayout":{"placement":"carvedVoid"},
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[
            {"block":"asteria:moss_wall","chance":1,
             "surfaceBlocks":["asteria:stone"],"supportSurface":"wall"}
          ]
        }
        """);
        var legacy = Assert.Single(biome.Decorations);
        Assert.Equal(DecorationSupportSurface.Wall, legacy.SupportSurface);
        Assert.False(legacy.IsLayer);

        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:moss_wall")
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
              "decorations":[{"block":"asteria:moss_wall","chance":1,
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
            ("caverns", "moss_wall"),
            ("gorge", "lichen_cliff"),
        })
        {
            var definition = biomes.Get("asteria:overworld/" + biome);
            var rule = Assert.Single(definition.Decorations,
                r => r.Block == "asteria:" + id);
            Assert.True(rule.IsLayer);
            Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
            Assert.Equal(["asteria:stone", "asteria:cobble_stone"],
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
            ("caverns", "fungus_shelf_wall"),
            ("gorge", "rosette_cliff"),
        })
        {
            var rule = Assert.Single(
                biomes.Get("asteria:overworld/" + biome).Decorations,
                r => r.Block == "asteria:" + id);
            Assert.False(rule.IsLayer);
            Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
            if (id == "fungus_shelf_wall")
            {
                Assert.True(rule.IsObject);
                var objects = PackContentRegistry<ObjectDefinition>.FromJson(
                    ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
                var obj = objects.Get(rule.Block);
                Assert.Equal(ObjectVisualKind.CrossedSprite, obj.Visual.Kind);
                Assert.All(new[] { BlockFace.Left, BlockFace.Right,
                    BlockFace.Front, BlockFace.Back },
                    face => Assert.True(obj.Supports(face)));
            }
            else
            {
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.UsesHorizontalFacing);
                Assert.True(block.HasTag("support_wall"));
                Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
            }
        }
    }

    [Fact]
    public void WallLayerRulesRejectMissingOrIncompatibleDefinitions()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        // Keep surface-biome dependencies referenced by cave spike rules.
        var registry = biomes;
        Assert.Throws<ArgumentException>(() =>
            registry.ValidateLayers(new AttachedLayerRegistry([])));

        var missingFace = new AttachedLayerRegistry([
            new AttachedLayerDefinition(
                "asteria:moss_wall", "textures/layers/moss_wall.png",
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
                Assert.Equal("asteria:lichen_cliff", selected);
                seen++;
            }
        }
        Assert.True(seen > 0);
    }

    [Fact]
    public void ProjectedWallObjectsAreSelectedSeparatelyFromFaceOverlays()
    {
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var layers = AttachedLayerRegistry.FromJson(ReadJson("layers"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        foreach (var (biomeName, objectName, layerName) in new[]
        {
            ("caverns", "fungus_shelf_wall", "moss_wall"),
            ("gorge", "rosette_cliff", "lichen_cliff"),
        })
        {
            var biome = biomes.Get("asteria:overworld/" + biomeName);
            var decorators = new SurfaceDecorationField(
                1739, [biome], blocks, layers: layers, objects: objects);
            var sample = new BiomeSample(biome.Id,
                [new BiomeInfluence(biome.Id, 1f)]);
            var stone = blocks.GetId("asteria:stone");
            Assert.True(biomeName == "caverns"
                ? decorators.HasWallObjectsFor(biome.Id)
                : decorators.HasWallBlocksFor(biome.Id));
            Assert.True(decorators.HasWallLayersFor(biome.Id));
            var foundObject = false;
            var foundLayer = false;
            for (var x = -52; x <= 52; x++)
            for (var z = -52; z <= 52; z++)
            {
                var height = biomeName == "caverns" ? 75 : 110;
                var blockAt = decorators.WallBlockAt(
                    sample, stone, x, height, z);
                var objectAt = biomeName == "caverns"
                    ? decorators.WallObjectAt(sample, stone, x, height, z)
                    : blockAt.IsAir ? null : "asteria:" + objectName;
                var overlayAt = decorators.WallLayerAt(
                    sample, stone, x, height, z);
                Assert.True(objectAt is null ||
                    objectAt == "asteria:" + objectName);
                Assert.True(overlayAt is null ||
                    overlayAt == "asteria:" + layerName);
                if (objectAt is not null) foundObject = true;
                if (overlayAt is not null) foundLayer = true;
                if (foundObject && foundLayer)
                    break;
            }
            Assert.True(foundObject);
            Assert.True(foundLayer);
            Assert.True(decorators.WallBlockAt(
                sample, blocks.GetId("asteria:basalt"), 0, 90, 0).IsAir);
        }
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
          "volumeLayout":{"placement":"carvedVoid"},
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[{
            "block":"asteria:moss_wall","chance":1,
            "surfaceBlocks":["asteria:stone"],"supportSurface":"wall"
          }]
        }
        """);
        var biomes = new BiomeRegistry([biome]);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:moss_wall", isCollidable: false)
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
