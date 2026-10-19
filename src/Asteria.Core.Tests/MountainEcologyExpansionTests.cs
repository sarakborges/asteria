using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MountainEcologyExpansionTests
{
    private static readonly (string Biome, string[] Names)[] SmallFlora =
    [
        ("mountains", [
            "stonecrop", "saxifrage", "lichen_rock"
        ]),
        ("mountain_belt", [
            "grass_pass", "rosette_thorn", "lichen_scree"
        ]),
    ];

    [Fact]
    public void SmallFloraIsBiomeSpecificAndRootedInExistingMountainMaterials()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        foreach (var (biomeName, names) in SmallFlora)
        {
            var biome = biomes.Get("asteria:overworld/" + biomeName);
            Assert.NotNull(biome.SurfaceHabitats);
            Assert.Equal("asteria:stone", biome.SurfaceLayers[0].Block);
            Assert.Equal(6, biome.Decorations.Count);
            foreach (var name in names)
            {
                var rule = Assert.Single(biome.Decorations,
                    r => r.Block == "asteria:" + name);
                Assert.NotNull(rule.HabitatWeights);
                Assert.NotNull(rule.Conditions);
                Assert.NotNull(rule.Cluster);
                Assert.True(rule.Conditions!.MaxSlope.HasValue);
                Assert.True(rule.Conditions.MaxY.HasValue);
                Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
                Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
                Assert.All(rule.SurfaceBlocks, ground =>
                    Assert.Contains(ground, new[]
                    {
                        "asteria:stone", "asteria:cobble_stone",
                        "asteria:gravel", "asteria:dirt"
                    }));
                if (name == "grass_pass")
                {
                    Assert.True(rule.IsObject);
                    Assert.False(blocks.TryGetId(rule.Block, out _));
                    var plant = objects.Get(rule.Block);
                    Assert.True(plant.Supports(BlockFace.Top));
                    Assert.Equal(ObjectVisualKind.CrossedSprite, plant.Visual.Kind);
                    Assert.True(plant.WindSway);
                    Assert.False(plant.DropSelf);
                    Assert.Equal("textures/objects/" + name + ".png",
                        plant.Visual.Texture);
                }
                else
                {
                    Assert.False(rule.IsObject);
                    var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                    Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                    Assert.False(block.IsCollidable);
                    Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
                    Assert.Equal(
                        name.StartsWith("lichen_", StringComparison.Ordinal)
                            ? BlockVisualKind.GroundSprite : BlockVisualKind.CrossedSprite,
                        block.Visual.Kind);
                    Assert.Equal(
                        (name.StartsWith("lichen_", StringComparison.Ordinal)
                            ? "textures/layers/" : "textures/objects/") +
                        name + ".png",
                        block.Visual.Texture!.Value.Texture);
                }
            }
        }
        Assert.DoesNotContain(biomes.Get("asteria:overworld/mountains")
            .Decorations, r => r.Block.StartsWith("asteria:belt_", StringComparison.Ordinal));
        Assert.DoesNotContain(biomes.Get("asteria:overworld/mountain_belt")
            .Decorations, r => r.Block.StartsWith("asteria:mountain_", StringComparison.Ordinal));
    }

    [Fact]
    public void MountainAndBeltTreesHaveDistinctPalettesAndGroundCoverage()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);

        var treeRoots = dimension.GeneratedSurfaceStructures
            .Where(root =>
                (root.Biome is "asteria:overworld/mountains" or "asteria:overworld/mountain_belt") &&
                root.Structure.StartsWith("asteria:tree_", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(treeRoots);

        var palettes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in treeRoots)
        {
            Assert.NotNull(root.HabitatWeights);
            var variants = structures.ResolveReference(root.Structure);
            Assert.NotEmpty(variants);
            var foliage = variants.SelectMany(tree => tree.Voxels)
                .Select(voxel => voxel.Block)
                .Where(id => blocks.TryGetId(id, out _))
                .Where(id => blocks.GetDefinition(blocks.GetId(id)).Category == "foliage")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.NotEmpty(foliage);
            Assert.True(palettes.Add(string.Join("|", foliage)),
                $"Tree family {root.Structure} must have its own foliage palette.");

            Assert.All(variants, variant =>
            {
                Assert.Equal((int?)0, variant.GroundAnchorY);
                Assert.Equal(1f, variant.Restrictions.RequiredBiomeCoverage);
                Assert.True(variant.Restrictions.RequiresDryGround);
                Assert.Equal(StructureFluidPolicy.Forbid, variant.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain, variant.Generation.ReplacePolicy);
                Assert.True(variant.Voxels.Count > 10);
            });
        }
    }

    [Fact]
    public void SmallDecoratorsUseOwnTextures()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (biome, names) in SmallFlora)
            foreach (var name in names)
            {
                var texture = name == "grass_pass"
                    ? objects.Get("asteria:" + name).Visual.Texture
                    : blocks.GetDefinition(blocks.GetId("asteria:" + name))
                        .Visual.Texture!.Value.Texture;
                Assert.True(seen.Add(texture));
            }
        Assert.Equal(6, seen.Count);
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
