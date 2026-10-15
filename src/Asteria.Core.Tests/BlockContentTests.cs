using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockContentTests
{

    [Fact]
    public void DefaultPackVisualReferencesResolveToFiles()
    {
        var pack = Path.Combine(AppContext.BaseDirectory, "packs", "default");
        var data = Path.Combine(pack, "data");
        var resources = Path.Combine(pack, "resources");
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     data, "*.json", SearchOption.AllDirectories)
                 .OrderBy(path => path, StringComparer.Ordinal))
        {
            using var document =
                System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            Check(document.RootElement, file);
        }

        Assert.True(missing.Count == 0,
            "Missing pack-owned resources:\n" + string.Join("\n", missing));

        void Check(System.Text.Json.JsonElement node, string file)
        {
            switch (node.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                        Check(property.Value, file);
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var value in node.EnumerateArray())
                        Check(value, file);
                    break;
                case System.Text.Json.JsonValueKind.String:
                    var path = node.GetString()!;
                    if (!path.StartsWith("textures/", StringComparison.Ordinal) &&
                        !path.StartsWith("models/", StringComparison.Ordinal))
                        break;

                    var fullPath = Path.Combine(
                        resources, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                        missing.Add(Path.GetRelativePath(pack, file) + " -> " + path);
                    break;
            }
        }
    }

    [Fact]
    public void BaseBlockCatalogLoadsWithWorldRebuildSemantics()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "blocks");
        var documents = Directory
            .EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        var registry = BlockRegistry.FromJson(documents);

        Assert.Equal(
            Directory.EnumerateFiles(
                    directory,
                    "*.json")
                .Count(),
            registry.AuthoredCount);

        var grass = registry.GetDefinition(registry.GetId("asteria:block_grass"));
        Assert.Equal(BlockTint.Grass, grass.Tint);
        Assert.True(grass.RotateTexture.Top);
        Assert.True(grass.Textures.Top.Single().Dyable);
        Assert.Equal(2, grass.Textures.Left.Count);
        Assert.True(grass.Textures.Left[1].Dyable);
        Assert.True(grass.SupportsMicroblocks);

        var sandLayer = registry.GetDefinition(registry.GetId("asteria:sand_layer"));
        Assert.True(sandLayer.Shape.IsStackableLayer);
        Assert.Equal(0.125f, sandLayer.Shape.Thickness);
        Assert.Equal("asteria:sand", sandLayer.Shape.StackToBlockId);

        var snowLayer = registry.GetDefinition(registry.GetId("asteria:layer_snow"));
        Assert.True(snowLayer.Shape.IsStackableLayer);
        Assert.Equal("asteria:snow", snowLayer.Shape.StackToBlockId);

        var oak = registry.GetId("asteria:log_oak");
        var oakDefinition = registry.GetDefinition(oak);
        Assert.Equal(
            [BlockOrientation.Y, BlockOrientation.Z, BlockOrientation.X],
            oakDefinition.Orientations);

        Assert.True(registry.TryGetVariant(oak, "stripped", out var stripped));
        Assert.Equal(registry.GetId("asteria:log_oak_stripped"), stripped);

        Assert.True(registry.TryGetVariant(oak, "hollow", out var hollow));
        Assert.Equal(BlockShapeKind.Hollow, registry.GetDefinition(hollow).Shape.Kind);

        var stone = registry.GetDefinition(registry.GetId("asteria:stone"));
        Assert.Equal("blocks_stone", stone.Category);
        Assert.Equal(2f, stone.Mining.Hardness);
        Assert.Contains("pickaxe", stone.Mining.RequiredTools);
        Assert.Equal("textures/blocks/stone.png", stone.Textures.Top.Single().Texture);

        Assert.False(registry.TryGetId("asteria:grass", out _));
        var grassPlant = Asteria.Core.Content.ObjectDefinition.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "objects", "grass.json")));
        Assert.Equal(Asteria.Core.Content.ObjectVisualKind.CrossedSprite,
            grassPlant.Visual.Kind);
        Assert.Equal(4, grassPlant.Visual.Planes);
        Assert.Equal(BlockTint.Foliage, grassPlant.Tint);
        Assert.Equal(BlockPreviewColor.Parse("6f9f50"), grassPlant.TintFallback);
        Assert.False(grassPlant.Unlit);
        Assert.Contains(
            BlockFace.Top,
            grassPlant.PlacementFaces);
        Assert.Single(
            grassPlant.PlacementFaces);
        Assert.True(grassPlant.Supports(BlockFace.Top));
        Assert.False(grassPlant.DropSelf);

        Assert.False(registry.TryGetId("asteria:pebble", out _));
        var pebble = Asteria.Core.Content.ObjectDefinition.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "objects", "pebble.json")));
        Assert.Equal(Asteria.Core.Content.ObjectVisualKind.ExtrudedSprite,
            pebble.Visual.Kind);
        Assert.Equal(0.42f, pebble.Visual.Width);
        Assert.Equal("textures/items/pebble.png", pebble.Visual.Texture);
        Assert.True(pebble.Supports(BlockFace.Top));
        Assert.True(pebble.DropSelf);
        Assert.Equal(Asteria.Core.Content.ObjectInteraction.Pickup,
            pebble.Interaction);
        Assert.Equal("asteria:pebble", pebble.PickupItem);
        Assert.Equal(InventoryEntryKind.Item, pebble.RewardEntry.Kind);
        Assert.Equal(0.14f, pebble.Target.Size.Y);

        Assert.False(registry.TryGetId("asteria:stick", out _));
        var stick = Asteria.Core.Content.ObjectDefinition.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", "objects", "stick.json")));
        Assert.Equal(Asteria.Core.Content.ObjectVisualKind.ExtrudedSprite,
            stick.Visual.Kind);
        Assert.Equal(.68f, stick.Visual.Width);
        Assert.Equal("textures/items/stick.png", stick.Visual.Texture);
        Assert.True(stick.Supports(BlockFace.Top));
        Assert.True(stick.DropSelf);
        Assert.Equal(Asteria.Core.Content.ObjectInteraction.Pickup, stick.Interaction);
        Assert.Equal("asteria:stick", stick.PickupItem);
        Assert.Equal(InventoryEntryKind.Item, stick.RewardEntry.Kind);
        Assert.Equal(.12f, stick.Target.Size.Y);

        foreach (var mushroomId in new[]
        {
            "asteria:mushroom_green", "asteria:mushroom_yellow"
        })
        {
            Assert.False(registry.TryGetId(mushroomId, out _));
            var mushroom = Asteria.Core.Content.ObjectDefinition.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                    "packs", "default", "data", "objects",
                    mushroomId["asteria:".Length..] + ".json")));
            Assert.Equal(Asteria.Core.Content.ObjectVisualKind.CrossedSprite,
                mushroom.Visual.Kind);
            Assert.True(mushroom.DropSelf);
            Assert.True(mushroom.Supports(BlockFace.Top));
        }

        var objectCatalog = Asteria.Core.Content.PackContentRegistry<Asteria.Core.Content.ObjectDefinition>
            .FromJson(Directory.EnumerateFiles(
                    Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "objects"),
                    "*.json")
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText),
                Asteria.Core.Content.ObjectDefinition.Parse, obj => obj.Id);
        foreach (var name in new[] { "mushroom_blue", "mushroom_pink", "mushroom_red", "mushroom_brown", "mushroom_purple" })
        {
            var id = "asteria:" + name;
            Assert.False(registry.TryGetId(id, out _));
            var obj = objectCatalog.Get(id);
            Assert.Equal(Asteria.Core.Content.ObjectVisualKind.CrossedSprite, obj.Visual.Kind);
            Assert.True(obj.Supports(BlockFace.Top));
            Assert.True(obj.DropSelf);
        }

        var brownMushroom = objectCatalog.Get("asteria:mushroom_brown");
        Assert.Equal(2, brownMushroom.Visual.Planes);
        Assert.True(brownMushroom.DropSelf);

        var sand = registry.GetDefinition(registry.GetId("asteria:sand"));
        var gravel = registry.GetDefinition(registry.GetId("asteria:gravel"));
        Assert.True(sand.HasTag("gravity"));
        Assert.True(gravel.HasTag("gravity"));
        Assert.True(stone.DropsSelf);

        var sphereShell =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:shell_sphere"));
        Assert.True(
            sphereShell.Mining.Unbreakable);
        Assert.False(
            sphereShell.DropsSelf);
    }

    [Theory]
    [InlineData("stone", "asteria:pebble", 1f, 4)]
    [InlineData("block_grass", "asteria:dirt", 1f, 1)]
    [InlineData("dirt", "asteria:dirt", 1f, 1)]
    [InlineData("gravel", "asteria:gravel", 1f, 1)]
    [InlineData("mud", "asteria:mud", 1f, 1)]
    [InlineData("leaf_oak", "asteria:stick", 0.6f, 1)]
    [InlineData("leaf_willow", "asteria:stick", 0.6f, 1)]
    public void MineCloneAuthoredBlockDropsAreRetained(
        string blockId, string itemId, float chance, int quantity)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs", "default",
            "data", "blocks", blockId + ".json");
        var block = BlockDefinitionJson.Parse(File.ReadAllText(path));
        var drop = Assert.Single(Assert.IsType<LootTable>(
            block.LootTable).Entries);
        Assert.Equal(itemId, drop.Item);
        Assert.Equal(chance, drop.Chance);
        Assert.Equal(quantity, drop.Quantity);
    }

    [Fact]
    public void UnbreakableCanBeEnabledByAuthoredContent()
    {
        const string json = """
            {
              "id": "asteria:fixture",
              "mining": {
                "unbreakable": true
              }
            }
            """;

        var registry =
            BlockRegistry.FromJson([json]);

        Assert.True(
            registry
                .GetDefinition(
                    registry.GetId(
                        "asteria:fixture"))
                .Mining
                .Unbreakable);
    }

    [Fact]
    public void DropsSelfCanBeDisabledByAuthoredContent()
    {
        const string json = """
            {
              "id": "asteria:fixture",
              "dropsSelf": false
            }
            """;

        var registry =
            BlockRegistry.FromJson([json]);

        Assert.False(
            registry
                .GetDefinition(
                    registry.GetId(
                        "asteria:fixture"))
                .DropsSelf);
    }

    [Fact]
    public void JsonRegistryIdsAreDeterministicByBlockId()
    {
        const string stone = """
            { "id": "asteria:stone", "previewColor": "888888" }
            """;
        const string dirt = """
            { "id": "asteria:dirt", "previewColor": "775544" }
            """;

        var forward = BlockRegistry.FromJson([stone, dirt]);
        var reverse = BlockRegistry.FromJson([dirt, stone]);

        Assert.Equal(forward.GetId("asteria:dirt"), reverse.GetId("asteria:dirt"));
        Assert.Equal(forward.GetId("asteria:stone"), reverse.GetId("asteria:stone"));
    }
}
