using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GorgeEnrichmentContentTests
{
    private static readonly string[] DecorationNames =
    [
        "pebble", "grass", "stick", "tuft_dry", "spike_stone", "lichen_cliff",
    ];

    [Fact]
    public void GorgeDecorationsUseActualMaterialsAndFaceSupport()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var layers = AttachedLayerRegistry.FromJson(ReadPackJson("layers"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        biomes.ValidateLayers(layers);
        var gorge = biomes.Get("asteria:overworld/gorge");

        Assert.NotNull(gorge.SurfaceLayout);
        Assert.NotNull(gorge.SurfaceTerrain);
        Assert.Null(gorge.VolumeLayout);
        Assert.Equal(
            ["valley_floor", "rubble_slopes", "high_rims"],
            gorge.SurfaceHabitats!.Bands.Select(band => band.Id));

        Assert.Equal(DecorationNames.Select(name => "asteria:" + name)
                .OrderBy(id => id, StringComparer.Ordinal),
            gorge.Decorations.Select(rule => rule.Block)
                .OrderBy(id => id, StringComparer.Ordinal));

        var knownMaterials = new HashSet<string>(StringComparer.Ordinal)
        {
            "asteria:stone", "asteria:gravel",
            "asteria:dirt", "asteria:cobble_stone",
        };
        foreach (var rule in gorge.Decorations)
        {
            var name = rule.Block["asteria:".Length..];
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.Conditions!.MinY.HasValue ||
                        rule.Conditions.MaxY.HasValue ||
                        rule.Conditions.MaxSlope.HasValue);
            Assert.All(rule.SurfaceBlocks,
                support => Assert.Contains(support, knownMaterials));
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);

            if (name == "lichen_cliff")
            {
                Assert.True(rule.IsLayer);
                Assert.Equal(DecorationSupportSurface.Wall, rule.SupportSurface);
                var layer = layers.Get(rule.Block);
                Assert.Equal("textures/layers/lichen_cliff.png", layer.Texture);
                Assert.All(new[] { BlockFace.Left, BlockFace.Right,
                    BlockFace.Front, BlockFace.Back },
                    face => Assert.True(layer.Supports(face)));
                continue;
            }

            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
            if (name is "pebble" or "grass" or "stick")
            {
                Assert.True(rule.IsObject);
                var obj = objects.Get(rule.Block);
                Assert.True(obj.Supports(BlockFace.Top));
                Assert.NotNull(obj.Visual.Texture);
                continue;
            }

            Assert.False(rule.IsObject);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            if (name == "spike_stone")
            {
                Assert.Equal(BlockShapeKind.Spike, block.Shape.Kind);
                Assert.True(block.IsCollidable);
            }
            else
            {
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.False(block.IsCollidable);
            }
            Assert.Contains(BlockFace.Top, block.PlacementFaces);
        }

        var pebble = Assert.Single(gorge.Decorations,
            rule => rule.Block == "asteria:pebble");
        Assert.True(pebble.HabitatWeights!.For("rubble_slopes") >
                    pebble.HabitatWeights.For("high_rims"));
        var grass = Assert.Single(gorge.Decorations,
            rule => rule.Block == "asteria:grass");
        Assert.True(grass.HabitatWeights!.For("valley_floor") >
                    grass.HabitatWeights.For("high_rims"));
        var spike = Assert.Single(gorge.Decorations,
            rule => rule.Block == "asteria:spike_stone");
        Assert.True(spike.HabitatWeights!.For("rubble_slopes") >
                    spike.HabitatWeights.For("valley_floor"));
    }

    [Fact]
    public void RetainedGorgeDecorationsUseDistinctAvailableAssets()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var layers = AttachedLayerRegistry.FromJson(ReadPackJson("layers"));
        var textures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in DecorationNames)
        {
            var id = "asteria:" + name;
            string texture;
            if (name == "lichen_cliff")
                texture = layers.Get(id).Texture;
            else if (name is "pebble" or "grass" or "stick")
                texture = objects.Get(id).Visual.Texture!;
            else if (name == "spike_stone")
                texture = Assert.Single(
                    blocks.GetDefinition(blocks.GetId(id)).Textures.ForFace(BlockFace.Top)).Texture;
            else
                texture = blocks.GetDefinition(blocks.GetId(id)).Visual.Texture!.Value.Texture;
            Assert.True(textures.Add(texture), $"{id} must have an independent texture.");
        }
        Assert.Equal(DecorationNames.Length, textures.Count);
    }

    [Fact]
    public void DecoratorFieldIsDeterministicAndAvoidsUnstableGround()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var layers = AttachedLayerRegistry.FromJson(ReadPackJson("layers"));
        var gorge = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/gorge");
        var first = new SurfaceDecorationField(2288, [gorge], blocks,
            objects: objects, layers: layers);
        var again = new SurfaceDecorationField(2288, [gorge], blocks,
            objects: objects, layers: layers);
        var sample = new BiomeSample(gorge.Id,
            [new BiomeInfluence(gorge.Id, 1f)]);
        var valid = new SurfacePlacementContext(94, 1);
        var steep = new SurfacePlacementContext(94, 30);
        var dirt = blocks.GetId("asteria:dirt");
        var stone = blocks.GetId("asteria:stone");
        var basalt = blocks.GetId("asteria:basalt");
        var generated = new HashSet<BlockRuntimeId>();

        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            var block = first.BlockAt(sample, dirt, x, z, valid);
            Assert.Equal(block, again.BlockAt(sample, dirt, x, z, valid));
            Assert.True(first.BlockAt(sample, dirt, x, z, steep).IsAir);
            Assert.True(first.BlockAt(sample, basalt, x, z, valid).IsAir);

            var obj = first.ObjectAt(sample, dirt, x, z, placement: valid);
            Assert.Equal(obj, again.ObjectAt(sample, dirt, x, z, placement: valid));
            Assert.Null(first.ObjectAt(sample, dirt, x, z, placement: steep));
            Assert.Null(first.ObjectAt(sample, basalt, x, z, placement: valid));
            if (!block.IsAir)
                generated.Add(block);

            var rock = first.BlockAt(sample, stone, x, z, valid);
            Assert.Equal(rock, again.BlockAt(sample, stone, x, z, valid));
            if (!rock.IsAir)
                generated.Add(rock);
        }

        Assert.Contains(blocks.GetId("asteria:tuft_dry"), generated);
        Assert.Contains(blocks.GetId("asteria:spike_stone"), generated);
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
