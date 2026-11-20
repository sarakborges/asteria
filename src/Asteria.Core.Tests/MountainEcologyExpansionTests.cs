using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MountainEcologyExpansionTests
{
    [Fact]
    public void MountainBiomesUseOnlyRetainedGenericFloraAndNativeLichen()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        foreach (var (biomeName, lichenName) in new[]
        {
            ("mountains", "lichen_rock"),
            ("mountain_belt", "lichen_scree")
        })
        {
            var biome = biomes.Get("asteria:overworld/" + biomeName);
            Assert.Equal(4, biome.Decorations.Count);
            Assert.Equal("asteria:stone", biome.SurfaceLayers[0].Block);
            foreach (var objectName in new[] { "pebble", "grass", "stick" })
            {
                var rule = Assert.Single(biome.Decorations,
                    decoration => decoration.Block == "asteria:" + objectName);
                Assert.True(rule.IsObject);
                Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
                Assert.NotNull(rule.HabitatWeights);
                Assert.True(objects.Get(rule.Block).Supports(BlockFace.Top));
            }

            var lichenRule = Assert.Single(biome.Decorations,
                decoration => decoration.Block == "asteria:" + lichenName);
            Assert.False(lichenRule.IsObject);
            Assert.NotNull(lichenRule.Cluster);
            Assert.NotNull(lichenRule.HabitatWeights);
            Assert.Equal(DecorationSupportSurface.Floor, lichenRule.SupportSurface);
            Assert.All(lichenRule.SurfaceBlocks, support =>
                Assert.Contains(support, new[]
                {
                    "asteria:stone", "asteria:cobble_stone",
                    "asteria:gravel", "asteria:dirt"
                }));
            var lichen = blocks.GetDefinition(blocks.GetId(lichenRule.Block));
            Assert.True(lichen.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.Equal(BlockVisualKind.GroundSprite, lichen.Visual.Kind);
            Assert.False(lichen.IsCollidable);
            Assert.Equal(BlockRenderMode.Cutout, lichen.RenderMode);
        }
    }

    [Fact]
    public void MountainAndBeltRootsNoLongerReferenceRemovedFantasyTrees()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        var structureSets = StructureSetRegistry.FromJson(ReadPackJson("structure_sets"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);

        foreach (var biome in new[]
        {
            "asteria:overworld/mountains",
            "asteria:overworld/mountain_belt"
        })
        {
            var roots = dimension.GeneratedSurfaceStructures
                .Where(rule => rule.Biome == biome).ToArray();
            Assert.NotEmpty(roots);
            Assert.DoesNotContain(roots,
                root => root.Structure.StartsWith("asteria:tree_", StringComparison.Ordinal));
            foreach (var root in roots)
            {
                Assert.True(structures.ResolvesReference(root.Structure) ||
                            structureSets.ResolvesReference(root.Structure),
                    $"Missing mountain structure reference: {root.Structure}");
                if (!structures.ResolvesReference(root.Structure))
                    continue;

                Assert.All(structures.ResolveReference(root.Structure)
                    .SelectMany(variant => variant.Restrictions.GroundBlocks),
                    ground => Assert.True(blocks.TryGetId(ground, out _)));
            }
        }
    }

    [Fact]
    public void MountainLichenUsesItsDedicatedExistingLayerTexture()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        foreach (var name in new[] { "lichen_rock", "lichen_scree" })
        {
            var definition = blocks.GetDefinition(blocks.GetId("asteria:" + name));
            var texture = definition.Visual.Texture!.Value.Texture;
            Assert.Equal("textures/layers/" + name + ".png", texture);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "resources",
                texture.Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    [Fact]
    public void MountainSmallDecoratorsAreSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        var rock = blocks.GetId("asteria:stone");
        var basalt = blocks.GetId("asteria:basalt");
        foreach (var biomeName in new[] { "mountains", "mountain_belt" })
        {
            var b = biomes.Get("asteria:overworld/" + biomeName);
            var sample = new BiomeSample(b.Id, [new BiomeInfluence(b.Id, 1f)]);
            var first = new SurfaceDecorationField(9127, [b], blocks, objects: objects);
            var second = new SurfaceDecorationField(9127, [b], blocks, objects: objects);
            var surface = new SurfacePlacementContext(130, 0);
            for (var z = -18; z <= 18; z++)
            for (var x = -18; x <= 18; x++)
            {
                Assert.Equal(first.BlockAt(sample, rock, x, z, surface),
                             second.BlockAt(sample, rock, x, z, surface));
                Assert.True(first.BlockAt(sample, basalt, x, z, surface).IsAir);
                Assert.Equal(first.ObjectAt(sample, rock, x, z, placement: surface),
                             second.ObjectAt(sample, rock, x, z, placement: surface));
                Assert.Null(first.ObjectAt(sample, basalt, x, z, placement: surface));
            }
        }
    }

    private static IEnumerable<string> ReadPackJson(string folder)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", folder);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
