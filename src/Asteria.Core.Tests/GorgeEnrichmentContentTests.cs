using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GorgeEnrichmentContentTests
{
    private static readonly string[] Names =
    [
        "sagebrush", "tuft_dry",
        "roots_exposed", "spike_stone",
    ];

    [Fact]
    public void GorgeDecorationsUseActualGorgeMaterialsAndExistingHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var gorge = biomes.Get("asteria:overworld/gorge");

        Assert.NotNull(gorge.SurfaceLayout);
        Assert.NotNull(gorge.SurfaceTerrain);
        Assert.Null(gorge.VolumeLayout);
        Assert.Equal(
            ["valley_floor", "rubble_slopes", "high_rims"],
            gorge.SurfaceHabitats!.Bands.Select(band => band.Id));

        var knownMaterials = new HashSet<string>(StringComparer.Ordinal)
        {
            "asteria:stone", "asteria:gravel",
            "asteria:dirt", "asteria:cobble_stone",
        };
        foreach (var name in Names)
        {
            var rule = Assert.Single(gorge.Decorations,
                item => item.Block == "asteria:" + name);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.Conditions!.MinY.HasValue);
            Assert.True(rule.Conditions.MaxY.HasValue);
            Assert.True(rule.Conditions.MaxSlope.HasValue);
            Assert.All(rule.SurfaceBlocks,
                support => Assert.Contains(support, knownMaterials));
            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            if (name == "sagebrush")
            {
                Assert.True(rule.IsObject);
                Assert.False(blocks.TryGetId(rule.Block, out _));
                Assert.True(objects.Get(rule.Block).Supports(BlockFace.Top));
            }
            else
            {
                Assert.False(rule.IsObject);
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                if (name != "spike_stone")
                    Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.Contains(BlockFace.Top, block.PlacementFaces);
            }
        }

        var shrubs = Assert.Single(gorge.Decorations,
            item => item.Block == "asteria:sagebrush");
        Assert.True(shrubs.HabitatWeights!.For("valley_floor") >
                    shrubs.HabitatWeights.For("rubble_slopes"));
        var tufts = Assert.Single(gorge.Decorations,
            item => item.Block == "asteria:tuft_dry");
        Assert.True(tufts.HabitatWeights!.For("valley_floor") >
                    tufts.HabitatWeights.For("high_rims"));
        var spurs = Assert.Single(gorge.Decorations,
            item => item.Block == "asteria:spike_stone");
        Assert.True(spurs.HabitatWeights!.For("rubble_slopes") >
                    spurs.HabitatWeights.For("valley_floor"));
    }

    [Fact]
    public void EachNewObjectUsesSupportedShapeAndIndependentTexture()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var textures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            if (name == "sagebrush")
            {
                var plant = objects.Get("asteria:" + name);
                Assert.Equal(ObjectVisualKind.CrossedSprite, plant.Visual.Kind);
                Assert.True(plant.WindSway);
                Assert.False(plant.DropSelf);
                var texture = "textures/objects/" + name + ".png";
                Assert.Equal(texture, plant.Visual.Texture);
                Assert.True(textures.Add(texture));
                continue;
            }
            var block = blocks.GetDefinition(
                blocks.GetId("asteria:" + name));
            if (name == "spike_stone")
            {
                Assert.Equal(BlockShapeKind.Spike, block.Shape.Kind);
                Assert.True(block.IsCollidable);
                var expectedTexture = "textures/blocks/stone.png";
                Assert.Equal(expectedTexture,
                    Assert.Single(block.Textures.ForFace(BlockFace.Top)).Texture);
                Assert.True(textures.Add(expectedTexture));
                continue;
            }
            var expectedKind = name == "roots_exposed"
                ? BlockVisualKind.GroundSprite
                : BlockVisualKind.CrossedSprite;
            Assert.Equal(expectedKind, block.Visual.Kind);
            Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
            Assert.False(block.IsCollidable);
            Assert.False(block.CastsShadow);
            var expected = "textures/objects/" + name + ".png";
            Assert.Equal(expected, block.Visual.Texture!.Value.Texture);
            Assert.True(textures.Add(expected));
        }
        Assert.Equal(4, textures.Count);
    }

    [Fact]
    public void DecoratorFieldIsDeterministicAndAvoidsUnstableGround()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var gorge = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/gorge");
        var first = new SurfaceDecorationField(2288, [gorge], blocks, objects: objects);
        var again = new SurfaceDecorationField(2288, [gorge], blocks, objects: objects);
        var sample = new BiomeSample(gorge.Id,
            [new BiomeInfluence(gorge.Id, 1f)]);
        var valid = new SurfacePlacementContext(94, 1);
        var steep = new SurfacePlacementContext(94, 30);
        var basalt = blocks.GetId("asteria:basalt");
        var dirt = blocks.GetId("asteria:dirt");
        var stone = blocks.GetId("asteria:stone");
        var generated = new HashSet<BlockRuntimeId>();
        var generatedObjects = new HashSet<string>(StringComparer.Ordinal);

        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            var a = first.BlockAt(sample, dirt, x, z, valid);
            Assert.Equal(a, again.BlockAt(sample, dirt, x, z, valid));
            Assert.True(first.BlockAt(sample, dirt, x, z, steep).IsAir);
            Assert.True(first.BlockAt(sample, basalt, x, z, valid).IsAir);
            var obj = first.ObjectAt(sample, dirt, x, z, placement: valid);
            Assert.Equal(obj, again.ObjectAt(sample, dirt, x, z, placement: valid));
            Assert.Null(first.ObjectAt(sample, dirt, x, z, placement: steep));
            Assert.Null(first.ObjectAt(sample, basalt, x, z, placement: valid));
            if (obj is not null)
                generatedObjects.Add(obj);
            if (!a.IsAir)
                generated.Add(a);
            var rock = first.BlockAt(sample, stone, x, z, valid);
            Assert.Equal(rock, again.BlockAt(sample, stone, x, z, valid));
            if (!rock.IsAir)
                generated.Add(rock);
        }

        Assert.Contains(blocks.GetId("asteria:tuft_dry"), generated);
        Assert.Contains("asteria:sagebrush", generatedObjects);
        Assert.Contains(blocks.GetId("asteria:spike_stone"), generated);
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
