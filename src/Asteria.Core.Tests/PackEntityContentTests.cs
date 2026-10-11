using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PackEntityContentTests
{
    private static IReadOnlyList<string> Documents(string domain)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", domain);
        return Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
    }

    [Fact]
    public void ImportedItemsAreDeterministicAndExposeIconVariants()
    {
        var documents = Documents("items");
        var registry = PackContentRegistry<ItemDefinition>.FromJson(
            documents, ItemDefinition.Parse, item => item.Id);

        Assert.Equal(26, registry.Count);
        foreach (var metal in new[] { "iron", "copper", "lead", "silver", "tin", "zinc", "nickel", "gold" })
            Assert.Equal("textures/items/ores/raw_" + metal + ".png",
                registry.Get("asteria:raw_" + metal).Icon);
        Assert.Equal(240, registry.Get("asteria:wayfarer_chestplate").MaxDurability);
        var pebble = registry.Get("asteria:pebble");
        Assert.Equal("textures/items/pebble.png", pebble.Icon);
        var stick = registry.Get("asteria:stick");
        Assert.Equal("textures/items/stick.png", stick.Icon);
        var slicer = registry.Get("asteria:dimensional_slicer");
        Assert.Equal("tools", slicer.Category);
        Assert.Collection(slicer.IconVariants,
            umbral =>
            {
                Assert.Equal("target_dimension", umbral.MetadataKey);
                Assert.Equal("asteria:umbral", umbral.MetadataValue);
                Assert.Equal("textures/items/dimensional_slicer_umbral.png", umbral.Icon);
            },
            overworld =>
            {
                Assert.Equal("target_dimension", overworld.MetadataKey);
                Assert.Equal("asteria:overworld", overworld.MetadataValue);
                Assert.Equal("textures/items/dimensional_slicer.png", overworld.Icon);
            });

        var reversed = PackContentRegistry<ItemDefinition>.FromJson(
            documents.Reverse(), ItemDefinition.Parse, item => item.Id);
        Assert.Equal(registry.Definitions.Select(item => item.Id),
            reversed.Definitions.Select(item => item.Id));
    }

    [Fact]
    public void ImportedToolsKeepMiningAndInteractionContracts()
    {
        var registry = PackContentRegistry<ToolDefinition>.FromJson(
            Documents("tools"), ToolDefinition.Parse, tool => tool.Id);

        Assert.Equal(9, registry.Count);
        var pickaxe = registry.Get("asteria:pickaxe_rustic");
        Assert.Equal("asteria:mine", pickaxe.LeftBehavior);
        Assert.Equal("pickaxe", pickaxe.Mining!.Category);
        Assert.Equal(1.5f, pickaxe.Mining.Speed);
        Assert.Equal("asteria:brush/paint",
            registry.Get("asteria:brush_rustic").LeftBehavior);
        Assert.Equal("asteria:bucket/use",
            registry.Get("asteria:bucket").RightBehavior);
    }

    [Fact]
    public void ImportedCreaturesRetainGeometryMotionAndPresentationMetadata()
    {
        var registry = PackContentRegistry<CreatureDefinition>.FromJson(
            Documents("creatures"), CreatureDefinition.Parse, creature => creature.Id);

        Assert.Equal(20, registry.Count);
        var aqua = registry.Get("asteria:slime_aqua");
        var large = registry.Get("asteria:slime_aqua_large");
        Assert.Equal("models/creatures/slime_hydro/slime_hydro.glb", aqua.Model);
        Assert.Equal("Idle", aqua.Animations["idle"]);
        Assert.Equal("textures/creatures/slime_hydro/face.png", aqua.Textures["SlimeFace"]);
        Assert.True(large.Health > aqua.Health);
        Assert.True(large.Collider.Size.X > aqua.Collider.Size.X);
        Assert.True(large.ParticleEffects.ContainsKey("death"));
        Assert.Equal(1f, registry.Get("asteria:slime").FallGravityScale);
    }

    [Fact]
    public void RegistryRejectsDuplicateIdsAndUnsafeResourcePaths()
    {
        const string item = """
            {"id":"asteria:test","category":"materials","icon":"textures/items/test.png"}
            """;

        Assert.Throws<ArgumentException>(() =>
            PackContentRegistry<ItemDefinition>.FromJson(
                [item, item], ItemDefinition.Parse, definition => definition.Id));
        Assert.Throws<FormatException>(() =>
            ItemDefinition.Parse("""
                {"id":"asteria:test","category":"materials","icon":"../escape.png"}
                """));
        Assert.Throws<FormatException>(() =>
            ToolDefinition.Parse("""
                {"id":"asteria:test","category":"tools","icon":"icon.png","leftBehavior":"bad","rightBehavior":"asteria:none"}
                """));
    }
}
