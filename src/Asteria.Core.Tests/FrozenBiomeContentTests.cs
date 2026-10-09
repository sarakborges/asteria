using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrozenBiomeContentTests
{
    private static readonly string[] ArcticObjects =
        ["frost_shrub", "frost_lichen", "glacial_shard"];
    private static readonly string[] AlpsObjects =
        ["alpine_edelweiss", "alpine_sedge", "frost_lichen", "glacial_shard"];

    [Fact]
    public void ArcticAndAlpsDecorationsUseOnlyTheirAuthoredHabitatBands()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);

        var arctic = biomes.Get("asteria:overworld/arctic");
        var alps = biomes.Get("asteria:overworld/alps");
        Assert.Equal(3, arctic.SurfaceHabitats!.Bands.Count);
        Assert.Equal(3, alps.SurfaceHabitats!.Bands.Count);
        Assert.NotNull(arctic.SurfaceTerrain);
        Assert.NotNull(alps.SurfaceTerrain);

        foreach (var (biome, names) in new[]
        {
            (arctic, ArcticObjects),
            (alps, AlpsObjects),
        })
        {
            foreach (var name in names)
            {
                var rule = Assert.Single(biome.Decorations,
                    d => d.Block == $"asteria:{name}");
                Assert.NotNull(rule.HabitatWeights);
                Assert.NotNull(rule.Cluster);
                Assert.NotNull(rule.Conditions);
                Assert.True(rule.Conditions!.MaxSlope.HasValue);
                Assert.NotEmpty(rule.SurfaceBlocks);
                Assert.All(rule.SurfaceBlocks,
                    support => Assert.Contains(support, new[]
                    {
                        "asteria:snow", "asteria:ice",
                        "asteria:gravel", "asteria:stone",
                    }));
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.Contains(BlockFace.Top, block.PlacementFaces);
                Assert.True(block.LightDampening <= 1);
            }
        }

        Assert.DoesNotContain(alps.Decorations,
            d => d.Block == "asteria:frost_shrub");
        Assert.DoesNotContain(arctic.Decorations,
            d => d.Block is "asteria:alpine_edelweiss" or "asteria:alpine_sedge");

        Assert.True(Assert.Single(arctic.Decorations,
            d => d.Block == "asteria:frost_shrub")
            .HabitatWeights!.For("frozen_plain") >
            Assert.Single(arctic.Decorations,
                d => d.Block == "asteria:frost_shrub")
                .HabitatWeights!.For("ice_fields"));
        Assert.True(Assert.Single(arctic.Decorations,
            d => d.Block == "asteria:glacial_shard")
            .HabitatWeights!.For("ice_fields") >
            Assert.Single(arctic.Decorations,
                d => d.Block == "asteria:glacial_shard")
                .HabitatWeights!.For("frozen_plain"));

        Assert.True(Assert.Single(alps.Decorations,
            d => d.Block == "asteria:alpine_edelweiss")
            .HabitatWeights!.For("snowfields") >
            Assert.Single(alps.Decorations,
                d => d.Block == "asteria:alpine_edelweiss")
                .HabitatWeights!.For("wind_scoured_rock"));
        Assert.True(Assert.Single(alps.Decorations,
            d => d.Block == "asteria:alpine_sedge")
            .HabitatWeights!.For("wind_scoured_rock") >
            Assert.Single(alps.Decorations,
                d => d.Block == "asteria:alpine_sedge")
                .HabitatWeights!.For("snowfields"));
    }

    [Fact]
    public void NewFrozenObjectsUseCompatibleGeometryAndDistinctTextures()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var ids = ArcticObjects.Concat(AlpsObjects).Distinct().ToArray();
        Assert.Equal(5, ids.Length);
        var textures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in ids)
        {
            var block = blocks.GetDefinition(blocks.GetId($"asteria:{name}"));
            Assert.NotEqual(BlockRenderMode.Translucent, block.RenderMode);
            if (name == "glacial_shard")
            {
                Assert.Equal(BlockShapeKind.Spike, block.Shape.Kind);
                Assert.True(block.IsCollidable);
                var path = $"textures/blocks/{name}.png";
                Assert.All(block.Textures.AllLayers(),
                    layer => Assert.Equal(path, layer.Texture));
                textures.Add(path);
            }
            else
            {
                Assert.Equal(name == "frost_lichen"
                    ? BlockVisualKind.GroundSprite
                    : BlockVisualKind.CrossedSprite,
                    block.Visual.Kind);
                Assert.False(block.IsCollidable);
                Assert.False(block.CastsShadow);
                Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
                textures.Add(block.Visual.Texture!.Texture);
                Assert.Equal($"textures/objects/{name}.png",
                    block.Visual.Texture!.Texture);
            }
        }
        Assert.Equal(ids.Length, textures.Count);
    }

    [Fact]
    public void DecorationSamplingIsDeterministicAndRespectsMaterialsAndSlope()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        foreach (var (biome, height) in new[]
        {
            (biomes.Get("asteria:overworld/arctic"), 94),
            (biomes.Get("asteria:overworld/alps"), 145),
        })
        {
            var first = new SurfaceDecorationField(548, [biome], blocks);
            var again = new SurfaceDecorationField(548, [biome], blocks);
            var sample = new BiomeSample(biome.Id,
                [new BiomeInfluence(biome.Id, 1f)]);
            var snow = blocks.GetId("asteria:snow");
            var stone = blocks.GetId("asteria:stone");
            var valid = new SurfacePlacementContext(height, 1);
            var tooSteep = new SurfacePlacementContext(height, 510);
            var authoredIds = (biome.Id.EndsWith("arctic", StringComparison.Ordinal)
                    ? ArcticObjects : AlpsObjects)
                .Select(name => blocks.GetId($"asteria:{name}"))
                .ToHashSet();
            var found = new HashSet<BlockRuntimeId>();
            for (var z = -32; z <= 32; z++)
            for (var x = -32; x <= 32; x++)
            {
                var snowBlock = first.BlockAt(sample, snow, x, z, valid);
                Assert.Equal(snowBlock, again.BlockAt(sample, snow, x, z, valid));
                // Legacy pebble rules may be independent of slope;
                // only the newly authored objects must be excluded.
                Assert.DoesNotContain(
                    first.BlockAt(sample, snow, x, z, tooSteep), authoredIds);
                if (!snowBlock.IsAir)
                    found.Add(snowBlock);
                if (biome.Id.EndsWith("alps", StringComparison.Ordinal))
                {
                    var rocky = first.BlockAt(sample, stone, x, z, valid);
                    Assert.Equal(rocky, again.BlockAt(sample, stone, x, z, valid));
                    if (!rocky.IsAir)
                        found.Add(rocky);
                }
                else
                {
                    Assert.True(first.BlockAt(sample, stone, x, z, valid).IsAir);
                }
            }
            Assert.NotEmpty(found);
        }
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
