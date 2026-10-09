using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FloatingIslandContentTests
{
    private static readonly string[] NewObjects =
        ["sky_reed", "aether_bloom", "aether_crystal", "sky_vines"];

    [Fact]
    public void DefaultIslandObjectsAreOnlyAuthoredOnAdditiveVolume()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var islands = biomes.Get("asteria:overworld/floating_islands");
        Assert.Null(islands.SurfaceLayout);
        Assert.Equal(VolumeBiomePlacement.Additive, islands.VolumeLayout!.Placement);
        Assert.Single(islands.Terrain3d!.Additive);
        Assert.Equal(200, islands.Terrain3d.Additive[0].MinY);
        Assert.Equal(280, islands.Terrain3d.Additive[0].MaxY);

        foreach (var name in NewObjects)
        {
            var rule = Assert.Single(islands.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.Equal(200, rule.Conditions!.MinY);
            Assert.Equal(283, rule.Conditions.MaxY);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.LightDampening == 0);
            Assert.Contains("asteria:grass_block", rule.SurfaceBlocks);
            if (name == "sky_vines")
            {
                Assert.Equal(DecorationSupportSurface.Ceiling,
                    rule.SupportSurface);
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportAbove));
                Assert.Equal(BlockVisualKind.CrossedSprite,
                    block.Visual.Kind);
                Assert.True(block.WindSway);
            }
            else
            {
                Assert.Equal(DecorationSupportSurface.Floor,
                    rule.SupportSurface);
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            }
        }

        var crystal = blocks.GetDefinition(blocks.GetId("asteria:aether_crystal"));
        Assert.Equal(BlockShapeKind.Spike, crystal.Shape.Kind);
        Assert.True(crystal.LightEmission.Blue > 0);
        var bloom = blocks.GetDefinition(blocks.GetId("asteria:aether_bloom"));
        Assert.False(bloom.LightEmission.IsDark);
    }

    [Fact]
    public void CeilingOnlyVinesDoNotSpawnFromIslandFloor()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:sky_vines")
        ]);
        var islands = new BiomeDefinition(
            "asteria:test/islands", null, null,
            [new BiomeSurfaceLayerDefinition("asteria:stone")],
            [new BiomeDecorationDefinition("asteria:sky_vines",
                chance: 1f, surfaceBlocks: ["asteria:stone"],
                supportSurface: DecorationSupportSurface.Ceiling)],
            volumeLayout: new BiomeVolumeLayoutDefinition(
                placement: VolumeBiomePlacement.Additive),
            terrain3d: new BiomeTerrain3dDefinition([
                new BiomeAdditiveDensityDefinition(
                    minY: 200, maxY: 280, horizontalScale: 112,
                    detailScale: 40, coverage: 0.55f,
                    roughness: 0.18f, densityScale: 28,
                    verticalFalloff: 1, horizontalFalloff: 1,
                    densityBias: 0)
            ]));
        var field = new SurfaceDecorationField(17, [islands], blocks);
        var sample = new BiomeSample(islands.Id,
            [new BiomeInfluence(islands.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        Assert.True(field.BlockAt(sample, stone, 3, 7,
            verticalY: 240).IsAir);
        Assert.Equal(blocks.GetId("asteria:sky_vines"),
            field.BlockAt(sample, stone, 3, 7,
                verticalY: 240,
                supportSurface: DecorationSupportSurface.Ceiling));
        Assert.True(field.HasCeilingDecorationsFor(islands.Id));
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
