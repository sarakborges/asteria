using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DecorativeObjectMigrationTests
{
    private static readonly string Root = Path.Combine(
        AppContext.BaseDirectory, "packs", "default", "data");

    private static IEnumerable<string> Read(string directory) =>
        Directory.EnumerateFiles(Path.Combine(Root, directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

    [Fact]
    public void ConvertedPlantsHaveOnlyObjectIdentitiesAndMatchingGeometry()
    {
        var blocks = BlockRegistry.FromJson(Read("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Read("objects"), ObjectDefinition.Parse, definition => definition.Id);

        foreach (var (id, width, height, planes, dropSelf) in new[]
        {
            ("mushroom_green", .72f, .62f, 2, true),
            ("mushroom_yellow", .72f, .62f, 2, true),
            ("daisy", .79f, .79f, 4, false),
            ("purple_aster", .8f, .84f, 4, false),
            ("iris", .79f, .91f, 4, false),
        })
        {
            var key = "asteria:" + id;
            Assert.False(blocks.TryGetId(key, out _));
            var definition = objects.Get(key);
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.Equal(width, definition.Visual.Width);
            Assert.Equal(height, definition.Visual.Height);
            Assert.Equal(planes, definition.Visual.Planes);
            Assert.Equal(dropSelf, definition.DropSelf);
            Assert.True(definition.Supports(BlockFace.Top));
        }
    }

    [Fact]
    public void StickPickupRetainsItemIdentityAndThinExtrudedGeometry()
    {
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Read("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var blocks = BlockRegistry.FromJson(Read("blocks"));
        var stick = objects.Get("asteria:stick");

        Assert.False(blocks.TryGetId(stick.Id, out _));
        Assert.Equal(ObjectVisualKind.ExtrudedSprite, stick.Visual.Kind);
        Assert.Equal("textures/items/stick.png", stick.Visual.Texture);
        Assert.Equal(.68f, stick.Visual.Width);
        Assert.Equal(.68f, stick.Visual.Depth);
        Assert.Equal(.012f, stick.Visual.Height);
        Assert.Equal(.12f, stick.Target.Size.Y);
        Assert.Equal(ObjectInteraction.Pickup, stick.Interaction);
        Assert.Equal(InventoryEntryKind.Item, stick.RewardEntry.Kind);
        Assert.Equal("asteria:stick", stick.RewardEntry.Id);
    }

    [Fact]
    public void BiomeDecorationRulesRetainRequiredHabitatAndWaterConstraints()
    {
        var blocks = BlockRegistry.FromJson(Read("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Read("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var biomes = BiomeRegistry.FromJson(Read("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        foreach (var (biomeId, id) in new[]
        {
            ("plains", "stick"), ("plains", "daisy"),
            ("plains", "purple_aster"), ("swamp", "stick"),
            ("swamp", "iris"), ("enchanted_forest", "stick"),
            ("mountains", "stick"), ("gorge", "stick"),
            ("wasteland", "stick"), ("withered_waste", "stick")
        })
        {
            var rule = Assert.Single(biomes.Get("asteria:overworld/" + biomeId)
                .Decorations, decoration => decoration.Block == "asteria:" + id);
            Assert.True(rule.IsObject);
            Assert.True(rule.Chance > 0);
            Assert.NotEmpty(rule.SurfaceBlocks);
        }

        var iris = Assert.Single(biomes.Get("asteria:overworld/swamp")
            .Decorations, rule => rule.Block == "asteria:iris");
        Assert.Equal(DecorationFluidRelation.Nearby,
            iris.FluidRequirement!.Relation);
        Assert.Equal("asteria:water", iris.FluidRequirement.Fluid);
        Assert.Equal(3, iris.FluidRequirement.MaxDistance);
    }
}
