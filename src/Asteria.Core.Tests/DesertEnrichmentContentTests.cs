using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DesertEnrichmentContentTests
{
    private static readonly string[] Objects =
    [
        "desert_dune_grass",
        "desert_thornbush",
        "desert_yucca",
        "desert_sandstone_spur",
    ];

    [Fact]
    public void DesertDecoratorsUseExistingSandSurfaceAndHabitatBands()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var desert = biomes.Get("asteria:overworld/desert");

        Assert.NotNull(desert.SurfaceLayout);
        Assert.NotNull(desert.SurfaceTerrain);
        Assert.Null(desert.VolumeLayout);
        Assert.Equal(
            ["open_dunes", "sand_flats", "sandstone_outcrops"],
            desert.SurfaceHabitats!.Bands.Select(band => band.Id));
        Assert.Equal("asteria:sand", desert.SurfaceLayers[0].Block);
        Assert.Equal((uint?)8, desert.SurfaceLayers[0].Depth);
        Assert.Equal("asteria:sandstone", desert.SurfaceLayers[1].Block);
        Assert.Equal((uint?)4, desert.SurfaceLayers[1].Depth);
        Assert.Equal("asteria:stone", desert.SurfaceLayers[2].Block);
        Assert.DoesNotContain(desert.Decorations,
            d => d.Block == "asteria:pebble");

        Assert.Equal(Objects.Length, desert.Decorations.Count);
        foreach (var name in Objects)
        {
            var rule = Assert.Single(desert.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.Equal(["asteria:sand"], rule.SurfaceBlocks);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.Conditions!.MinY.HasValue);
            Assert.True(rule.Conditions.MaxY.HasValue);
            Assert.True(rule.Conditions.MaxSlope.HasValue);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.Contains(BlockFace.Top, block.PlacementFaces);
        }

        var grass = Assert.Single(desert.Decorations,
            d => d.Block == "asteria:desert_dune_grass");
        Assert.True(grass.HabitatWeights!.For("sand_flats") >
                    grass.HabitatWeights.For("open_dunes"));
        var yucca = Assert.Single(desert.Decorations,
            d => d.Block == "asteria:desert_yucca");
        Assert.Equal(0f, yucca.HabitatWeights!.For("open_dunes"));
        Assert.True(yucca.HabitatWeights.For("sandstone_outcrops") >
                    yucca.HabitatWeights.For("sand_flats"));
        var spur = Assert.Single(desert.Decorations,
            d => d.Block == "asteria:desert_sandstone_spur");
        Assert.True(spur.HabitatWeights!.For("sandstone_outcrops") >
                    spur.HabitatWeights.For("sand_flats"));
    }

    [Fact]
    public void DesertObjectsKeepSquareGeometryAndIndependent64PxTexturePaths()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var textures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Objects)
        {
            var block = blocks.GetDefinition(blocks.GetId($"asteria:{name}"));
            if (name == "desert_sandstone_spur")
            {
                Assert.Equal(BlockShapeKind.Spike, block.Shape.Kind);
                Assert.True(block.IsCollidable);
                var path = $"textures/blocks/{name}.png";
                Assert.Equal(path,
                    Assert.Single(block.Textures.ForFace(BlockFace.Top)).Texture);
                Assert.True(textures.Add(path));
            }
            else
            {
                Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
                Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
                Assert.False(block.IsCollidable);
                Assert.False(block.CastsShadow);
                Assert.Equal((byte)0, block.LightDampening);
                var path = $"textures/objects/{name}.png";
                Assert.Equal(path, block.Visual.Texture!.Value.Texture);
                Assert.True(textures.Add(path));
                Assert.Equal(name is "desert_dune_grass" or "desert_thornbush",
                    block.WindSway);
            }
        }
        Assert.Equal(4, textures.Count);
    }

    [Fact]
    public void SurfaceDecoratorSamplingIsDeterministicAndRestrictsMaterialAndSlope()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var desert = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/desert");
        var a = new SurfaceDecorationField(2026, [desert], blocks);
        var b = new SurfaceDecorationField(2026, [desert], blocks);
        var sample = new BiomeSample(desert.Id,
            [new BiomeInfluence(desert.Id, 1f)]);
        var sand = blocks.GetId("asteria:sand");
        var sandstone = blocks.GetId("asteria:sandstone");
        var gravel = blocks.GetId("asteria:gravel");
        var flat = new SurfacePlacementContext(94, 1);
        var steep = new SurfacePlacementContext(94, 50);
        var tooHigh = new SurfacePlacementContext(180, 1);
        var found = new HashSet<BlockRuntimeId>();

        for (var z = -40; z <= 40; z++)
        for (var x = -40; x <= 40; x++)
        {
            var first = a.BlockAt(sample, sand, x, z, flat);
            Assert.Equal(first, b.BlockAt(sample, sand, x, z, flat));
            Assert.True(a.BlockAt(sample, sandstone, x, z, flat).IsAir);
            Assert.True(a.BlockAt(sample, gravel, x, z, flat).IsAir);
            Assert.True(a.BlockAt(sample, sand, x, z, steep).IsAir);
            Assert.True(a.BlockAt(sample, sand, x, z, tooHigh).IsAir);
            if (!first.IsAir)
                found.Add(first);
        }
        Assert.NotEmpty(found);
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
