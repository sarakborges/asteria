using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernBiodiversityTests
{
    [Fact]
    public void DefaultCavernContentHasValidLightingGeometryAndSupport()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var cavern = biomes.Get("asteria:overworld/caverns");
        Assert.Null(cavern.SurfaceLayout);
        Assert.Equal(VolumeBiomePlacement.CarvedVoid, cavern.VolumeLayout!.Placement);

        var crystals = Assert.Single(cavern.CaveSpikes,
            rule => rule.Block == "asteria:cave_crystal_azure");
        Assert.Equal(2, crystals.MinHeight);
        Assert.Equal(5, crystals.MaxHeight);
        Assert.Contains(CaveSpikeDirection.Up, crystals.Directions);
        Assert.Contains(CaveSpikeDirection.Down, crystals.Directions);
        Assert.Equal(2, crystals.MinSpacing);
        var crystal = blocks.GetDefinition(blocks.GetId(crystals.Block));
        Assert.Equal(BlockShapeKind.Spike, crystal.Shape.Kind);
        Assert.True(crystal.LightEmission.Blue > crystal.LightEmission.Green);
        Assert.Equal(6, cavern.CaveSpikes.Count);

        var roots = Assert.Single(cavern.Decorations,
            x => x.Block == "asteria:cave_roots");
        Assert.Equal(DecorationSupportSurface.Ceiling, roots.SupportSurface);
        Assert.Contains("asteria:stone", roots.SurfaceBlocks);
        var rootsBlock = blocks.GetDefinition(blocks.GetId(roots.Block));
        Assert.True(rootsBlock.HasTag(BlockPhysicsCapabilities.SupportAbove));
        Assert.Equal(BlockVisualKind.CrossedSprite, rootsBlock.Visual.Kind);
        Assert.False(rootsBlock.IsCollidable);

        foreach (var id in new[] { "asteria:cave_glowcap", "asteria:cave_glow_fern" })
        {
            var rule = Assert.Single(cavern.Decorations, d => d.Block == id);
            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            var block = blocks.GetDefinition(blocks.GetId(id));
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.False(block.LightEmission.IsDark);
            Assert.False(block.IsCollidable);
            Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
        }
    }

    [Fact]
    public void DirectionalDecoratorParsesAndRejectsUnknownSupport()
    {
        var cavern = BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/cavern",
          "volumeLayout":{"placement":"carvedVoid"},
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[
            {"block":"asteria:ceiling","surfaceBlocks":["asteria:stone"],
             "chance":1,"supportSurface":"ceiling"},
            {"block":"asteria:floor","surfaceBlocks":["asteria:stone"],"chance":1}
          ]
        }
        """);
        Assert.Equal(DecorationSupportSurface.Ceiling, cavern.Decorations[0].SupportSurface);
        Assert.Equal(DecorationSupportSurface.Floor, cavern.Decorations[1].SupportSurface);
        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse("""
        {
          "id":"asteria:test/cavern",
          "volumeLayout":{"placement":"carvedVoid"},
          "palette":{"default":[{"block":"asteria:stone"}]},
          "decorations":[
            {"block":"asteria:bad","surfaceBlocks":["asteria:stone"],
             "chance":1,"supportSurface":"walls"}
          ]
        }
        """));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BiomeDecorationDefinition("asteria:bad", 1f,
                ["asteria:stone"],
                supportSurface:(DecorationSupportSurface)123));
    }

    [Fact]
    public void CeilingCandidatesNeverAppearAsFloorDecorations()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:cave_roots"),
            new BlockDefinition("asteria:cave_glowcap")
        ]);
        var cavern = new BiomeDefinition("asteria:test/cavern", null, null,
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            decorations: [
                new BiomeDecorationDefinition(
                    "asteria:cave_roots", 1f, ["asteria:stone"],
                    supportSurface: DecorationSupportSurface.Ceiling),
                new BiomeDecorationDefinition(
                    "asteria:cave_glowcap", 1f, ["asteria:stone"])
            ],
            volumeLayout: new BiomeVolumeLayoutDefinition(
                placement: VolumeBiomePlacement.CarvedVoid));
        var field = new SurfaceDecorationField(11, [cavern], blocks);
        var sample = new BiomeSample(cavern.Id,
            [new BiomeInfluence(cavern.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        Assert.Equal(blocks.GetId("asteria:cave_glowcap"),
            field.BlockAt(sample, stone, 17, 29, verticalY: 61));
        Assert.Equal(blocks.GetId("asteria:cave_roots"),
            field.BlockAt(sample, stone, 17, 29, verticalY: 61,
                supportSurface: DecorationSupportSurface.Ceiling));
        Assert.True(field.HasCeilingDecorationsFor(cavern.Id));
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
