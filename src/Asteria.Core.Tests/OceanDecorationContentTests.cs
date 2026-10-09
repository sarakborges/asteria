using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanDecorationContentTests
{
    private static readonly string[] Names =
        ["seagrass", "kelp", "sea_anemone", "seashell", "starfish"];

    [Fact]
    public void OceanObjectsUseSubmergedHabitatRulesAndExistingSpriteModels()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var ocean = biomes.Get("asteria:overworld/ocean");
        Assert.Equal(Names.OrderBy(x => x, StringComparer.Ordinal),
            ocean.Decorations.Select(d => d.Block["asteria:".Length..])
                .OrderBy(x => x, StringComparer.Ordinal));

        foreach (var name in Names)
        {
            var rule = Assert.Single(ocean.Decorations, d => d.Block == $"asteria:{name}");
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.Equal(DecorationFluidPlacement.Submerged, rule.FluidPlacement);
            Assert.Equal((int?)86, rule.Conditions!.MaxY);
            Assert.NotNull(rule.Cluster);
            Assert.Equal(4, rule.Cluster!.Octaves);
            Assert.Contains("asteria:sand", rule.SurfaceBlocks);
            Assert.NotNull(rule.HabitatWeights);
            Assert.Equal("foliage", block.Category);
            Assert.True(block.HasTag("support_below"));
            Assert.False(block.IsCollidable);
            Assert.False(block.CastsShadow);
            Assert.Equal((byte)0, block.LightDampening);
            Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
            Assert.Equal(name is "seashell" or "starfish"
                ? BlockVisualKind.GroundSprite : BlockVisualKind.CrossedSprite,
                block.Visual.Kind);
            Assert.Equal($"textures/objects/{name}.png", block.Visual.Texture!.Texture);
        }
        var grass = Assert.Single(ocean.Decorations, d => d.Block == "asteria:seagrass");
        var anemone = Assert.Single(ocean.Decorations, d => d.Block == "asteria:sea_anemone");
        Assert.True(grass.HabitatWeights!.For("sand_flats") > grass.HabitatWeights.For("rocky_reefs"));
        Assert.True(anemone.HabitatWeights!.For("rocky_reefs") > anemone.HabitatWeights.For("sand_flats"));
    }

    [Fact]
    public void OceanObjectsSpawnInWaterNotOnDrySandOrUnsupportedStone()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var ocean = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/ocean");
        var sample = new BiomeSample(ocean.Id,
            [new BiomeInfluence(ocean.Id, 1f)]);
        var a = new SurfaceDecorationField(2026, [ocean], blocks);
        var b = new SurfaceDecorationField(2026, [ocean], blocks);
        var sand = blocks.GetId("asteria:sand");
        var stone = blocks.GetId("asteria:stone");
        var context = new SurfacePlacementContext(75, 0);
        var found = new HashSet<BlockRuntimeId>();

        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            var wet = a.BlockAt(sample, sand, x, z,
                suppliedPlacement: context, submerged: true);
            Assert.Equal(wet, b.BlockAt(sample, sand, x, z,
                suppliedPlacement: context, submerged: true));
            Assert.True(a.BlockAt(sample, sand, x, z,
                suppliedPlacement: context).IsAir);
            Assert.True(a.BlockAt(sample, stone, x, z,
                suppliedPlacement: context, submerged: true).IsAir);
            if (!wet.IsAir)
                found.Add(wet);
        }
        Assert.Contains(blocks.GetId("asteria:seagrass"), found);
        Assert.Contains(blocks.GetId("asteria:seashell"), found);
        Assert.Contains(blocks.GetId("asteria:kelp"), found);
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
