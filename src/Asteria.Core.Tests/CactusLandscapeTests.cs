using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CactusLandscapeTests
{
    [Fact]
    public void CactusFamiliesAreMultiblockAndFlowering()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var families = new Dictionary<string, int>
        {
            ["asteria:cactus_saguaro"] = 3,
            ["asteria:cactus_barrel"] = 2,
        };

        foreach (var (family, count) in families)
        {
            var variants = structures.ResolveReference(family);
            Assert.Equal(count, variants.Count);
            Assert.Equal(count,
                variants.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());

            foreach (var item in variants)
            {
                Assert.True(item.Voxels.Count >= 3);
                Assert.True(item.MaximumY >= 2);
                Assert.True(item.Restrictions.RequiresDryGround);
                Assert.Equal(1f, item.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(StructureReplacePolicy.Terrain,
                    item.Generation.ReplacePolicy);
                Assert.Equal(StructureFluidPolicy.Forbid,
                    item.Generation.FluidPolicy);
                Assert.Contains(item.Voxels, v =>
                    v.Block is "asteria:flower_cactus_pink"
                        or "asteria:flower_cactus_yellow");
                var placements = item.Voxels.ToDictionary(
                    v => (v.X, v.Y, v.Z), v => v.Block);
                foreach (var flower in item.Voxels.Where(v =>
                    v.Block is "asteria:flower_cactus_pink"
                        or "asteria:flower_cactus_yellow"))
                {
                    Assert.True(placements.TryGetValue(
                        (flower.X, flower.Y - 1, flower.Z), out var support));
                    Assert.Contains(support, new[]
                    {
                        "asteria:cactus_column", "asteria:cactus_barrel",
                    });
                }
            }
        }

        var saguaros = structures.ResolveReference("asteria:cactus_saguaro");
        Assert.All(saguaros, s => Assert.True(s.MaximumY >= 6));
        Assert.True(saguaros.Max(s => s.MaximumY) -
                    saguaros.Min(s => s.MaximumY) >= 3);
        Assert.Contains(saguaros, variant =>
            variant.Voxels.Any(v =>
                v.Block == "asteria:cactus_column" && v.X != 0));

        foreach (var id in new[] { "asteria:cactus_column",
                 "asteria:cactus_barrel" })
        {
            var block = blocks.GetDefinition(blocks.GetId(id));
            Assert.True(block.IsCollidable);
            Assert.All(block.Textures.AllLayers(),
                layer => Assert.StartsWith("textures/blocks/cactus/", layer.Texture));
        }
        foreach (var id in new[] {
            "asteria:flower_cactus_pink", "asteria:flower_cactus_yellow" })
        {
            var block = blocks.GetDefinition(blocks.GetId(id));
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.False(block.IsCollidable);
        }
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", dir);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
