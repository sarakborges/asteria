using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrozenBiomeContentTests
{
    private static readonly string[] DecorationNames =
        ["pebble", "lichen_frost", "spike_ice"];

    [Fact]
    public void ArcticAndAlpsDecorationsUseOnlyTheirAuthoredHabitatBands()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        var arctic = biomes.Get("asteria:overworld/arctic");
        var alps = biomes.Get("asteria:overworld/alps");
        Assert.Equal(3, arctic.SurfaceHabitats!.Bands.Count);
        Assert.Equal(3, alps.SurfaceHabitats!.Bands.Count);
        Assert.NotNull(arctic.SurfaceTerrain);
        Assert.NotNull(alps.SurfaceTerrain);

        foreach (var biome in new[] { arctic, alps })
        {
            Assert.Equal(DecorationNames.OrderBy(name => name, StringComparer.Ordinal),
                biome.Decorations.Select(rule => rule.Block["asteria:".Length..])
                    .OrderBy(name => name, StringComparer.Ordinal));

            foreach (var name in DecorationNames)
            {
                var rule = Assert.Single(biome.Decorations,
                    decoration => decoration.Block == "asteria:" + name);
                Assert.NotNull(rule.HabitatWeights);
                Assert.NotNull(rule.Cluster);
                Assert.NotEmpty(rule.SurfaceBlocks);
                Assert.All(rule.SurfaceBlocks, support => Assert.Contains(support, new[]
                {
                    "asteria:snow", "asteria:ice", "asteria:gravel", "asteria:stone"
                }));

                if (name == "pebble")
                {
                    Assert.True(rule.IsObject);
                    Assert.True(objects.Get(rule.Block).Supports(BlockFace.Top));
                    Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
                    continue;
                }

                Assert.False(rule.IsObject);
                Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.Contains(BlockFace.Top, block.PlacementFaces);
                Assert.True(block.LightDampening <= 1);
                if (name == "lichen_frost")
                    Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                else
                {
                    Assert.Equal(BlockShapeKind.Spike, block.Shape.Kind);
                    Assert.True(block.IsCollidable);
                }
            }
        }

        Assert.True(Assert.Single(arctic.Decorations,
            rule => rule.Block == "asteria:lichen_frost")
            .HabitatWeights!.For("ice_fields") >
            Assert.Single(arctic.Decorations,
                rule => rule.Block == "asteria:lichen_frost")
                .HabitatWeights!.For("frozen_plain"));
        Assert.True(Assert.Single(arctic.Decorations,
            rule => rule.Block == "asteria:spike_ice")
            .HabitatWeights!.For("ice_fields") >
            Assert.Single(arctic.Decorations,
                rule => rule.Block == "asteria:spike_ice")
                .HabitatWeights!.For("frozen_plain"));
        Assert.True(Assert.Single(alps.Decorations,
            rule => rule.Block == "asteria:lichen_frost")
            .HabitatWeights!.For("glacial_ice") >
            Assert.Single(alps.Decorations,
                rule => rule.Block == "asteria:lichen_frost")
                .HabitatWeights!.For("wind_scoured_rock"));
        Assert.True(Assert.Single(alps.Decorations,
            rule => rule.Block == "asteria:spike_ice")
            .HabitatWeights!.For("glacial_ice") >
            Assert.Single(alps.Decorations,
                rule => rule.Block == "asteria:spike_ice")
                .HabitatWeights!.For("wind_scoured_rock"));
    }

    [Fact]
    public void FrozenDecorationsUseCompatibleGeometryAndDistinctTextures()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var textures = new HashSet<string>(StringComparer.Ordinal);

        var pebble = objects.Get("asteria:pebble");
        Assert.Equal(ObjectVisualKind.ExtrudedSprite, pebble.Visual.Kind);
        Assert.Equal("textures/items/pebble.png", pebble.Visual.Texture);
        Assert.True(textures.Add(pebble.Visual.Texture!));

        var lichen = blocks.GetDefinition(blocks.GetId("asteria:lichen_frost"));
        Assert.Equal(BlockVisualKind.GroundSprite, lichen.Visual.Kind);
        Assert.Equal("textures/layers/lichen_frost.png", lichen.Visual.Texture!.Value.Texture);
        Assert.False(lichen.IsCollidable);
        Assert.Equal(BlockRenderMode.Cutout, lichen.RenderMode);
        Assert.True(textures.Add(lichen.Visual.Texture.Value.Texture));

        var spike = blocks.GetDefinition(blocks.GetId("asteria:spike_ice"));
        Assert.Equal(BlockShapeKind.Spike, spike.Shape.Kind);
        Assert.True(spike.IsCollidable);
        const string iceTexture = "textures/blocks/ice.png";
        Assert.All(spike.Textures.AllLayers(),
            layer => Assert.Equal(iceTexture, layer.Texture));
        Assert.True(textures.Add(iceTexture));

        Assert.Equal(3, textures.Count);
    }

    [Fact]
    public void DecorationSamplingIsDeterministicAndRespectsMaterialsAndSlope()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        foreach (var (biome, height) in new[]
        {
            (biomes.Get("asteria:overworld/arctic"), 94),
            (biomes.Get("asteria:overworld/alps"), 145),
        })
        {
            var first = new SurfaceDecorationField(548, [biome], blocks, objects: objects);
            var again = new SurfaceDecorationField(548, [biome], blocks, objects: objects);
            var sample = new BiomeSample(biome.Id,
                [new BiomeInfluence(biome.Id, 1f)]);
            var snow = blocks.GetId("asteria:snow");
            var stone = blocks.GetId("asteria:stone");
            var valid = new SurfacePlacementContext(height, 1);
            var tooSteep = new SurfacePlacementContext(height, 510);
            var authoredBlockIds = new[] { "lichen_frost", "spike_ice" }
                .Select(name => blocks.GetId("asteria:" + name)).ToHashSet();
            var found = new HashSet<BlockRuntimeId>();

            for (var z = -32; z <= 32; z++)
            for (var x = -32; x <= 32; x++)
            {
                var snowBlock = first.BlockAt(sample, snow, x, z, valid);
                Assert.Equal(snowBlock, again.BlockAt(sample, snow, x, z, valid));
                Assert.DoesNotContain(first.BlockAt(sample, snow, x, z, tooSteep),
                    authoredBlockIds);

                var objectId = first.ObjectAt(sample, snow, x, z, placement: valid);
                Assert.Equal(objectId, again.ObjectAt(sample, snow, x, z, placement: valid));
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
                    Assert.True(first.BlockAt(sample, stone, x, z, valid).IsAir);
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
