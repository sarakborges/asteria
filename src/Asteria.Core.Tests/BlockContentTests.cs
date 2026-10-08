using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockContentTests
{
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

        var grass = registry.GetDefinition(registry.GetId("asteria:grass_block"));
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

        var snowLayer = registry.GetDefinition(registry.GetId("asteria:snow_layer"));
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
        Assert.Equal("stone_blocks", stone.Category);
        Assert.Equal(2f, stone.Mining.Hardness);
        Assert.Contains("pickaxe", stone.Mining.RequiredTools);
        Assert.Equal("textures/blocks/stone.png", stone.Textures.Top.Single().Texture);

        var grassPlant =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:grass"));
        Assert.Equal(
            BlockVisualKind.CrossedSprite,
            grassPlant.Visual.Kind);
        Assert.Equal(
            4,
            grassPlant.Visual.Planes);
        Assert.True(
            grassPlant.Visual.Texture!.Value.Dyable);
        Assert.Contains(
            BlockFace.Top,
            grassPlant.PlacementFaces);
        Assert.Single(
            grassPlant.PlacementFaces);
        Assert.True(
            grassPlant.HasTag(
                BlockPhysicsCapabilities.SupportBelow));
        Assert.False(
            grassPlant.IsCollidable);
        Assert.False(
            grassPlant.DropsSelf);

        var pebble =
            registry.GetDefinition(registry.GetId("asteria:pebble"));
        Assert.Equal(BlockVisualKind.GroundSprite, pebble.Visual.Kind);
        Assert.Equal(0.42f, pebble.Visual.Width);
        Assert.Equal("textures/items/pebble.png",
            pebble.Visual.Texture!.Value.Texture);
        Assert.True(pebble.HasTag(BlockPhysicsCapabilities.SupportBelow));
        Assert.True(pebble.DropsSelf);
        Assert.False(pebble.IsCollidable);
        Assert.Equal(BlockInteractionKind.Pickup, pebble.Interaction);
        Assert.Equal("asteria:pebble", pebble.PickupItemId);
        Assert.Equal(0.14f, pebble.Visual.TargetHeight);

        var stick =
            registry.GetDefinition(registry.GetId("asteria:stick"));
        Assert.Equal(BlockVisualKind.GroundSprite, stick.Visual.Kind);
        Assert.Equal(0.68f, stick.Visual.Width);
        Assert.Equal("textures/items/stick.png",
            stick.Visual.Texture!.Value.Texture);
        Assert.True(stick.HasTag(BlockPhysicsCapabilities.SupportBelow));
        Assert.True(stick.DropsSelf);
        Assert.False(stick.IsCollidable);
        Assert.Equal(BlockInteractionKind.Pickup, stick.Interaction);
        Assert.Equal("asteria:stick", stick.PickupItemId);
        Assert.Equal(0.12f, stick.Visual.TargetHeight);

        foreach (var mushroomId in
                 new[]
                 {
                     "asteria:mushroom_blue",
                     "asteria:mushroom_brown",
                     "asteria:mushroom_green",
                     "asteria:mushroom_pink",
                     "asteria:mushroom_purple",
                     "asteria:mushroom_red",
                     "asteria:mushroom_yellow",
                 })
        {
            var mushroom =
                registry.GetDefinition(
                    registry.GetId(
                        mushroomId));
            Assert.Equal(
                BlockVisualKind.CrossedSprite,
                mushroom.Visual.Kind);
            Assert.False(
                mushroom.IsCollidable);
            Assert.True(
                mushroom.DropsSelf);
            Assert.True(
                mushroom.HasTag(
                    BlockPhysicsCapabilities.SupportBelow));
        }

        var brownMushroom =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:mushroom_brown"));
        Assert.Equal(
            BlockVisualKind.CrossedSprite,
            brownMushroom.Visual.Kind);
        Assert.Equal(
            2,
            brownMushroom.Visual.Planes);
        Assert.True(
            brownMushroom.DropsSelf);

        var sand = registry.GetDefinition(registry.GetId("asteria:sand"));
        var gravel = registry.GetDefinition(registry.GetId("asteria:gravel"));
        Assert.True(sand.HasTag("gravity"));
        Assert.True(gravel.HasTag("gravity"));
        Assert.True(stone.DropsSelf);

        var sphereShell =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:sphere_shell"));
        Assert.True(
            sphereShell.Mining.Unbreakable);
        Assert.False(
            sphereShell.DropsSelf);
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
