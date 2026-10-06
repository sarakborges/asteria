using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockContentTests
{
    [Fact]
    public void BaseBlockCatalogLoadsWithWorldRebuildSemantics()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "content", "blocks");
        var documents = Directory
            .EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

        var registry = BlockRegistry.FromJson(documents);

        Assert.Equal(14, registry.AuthoredCount);

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

        var sand = registry.GetDefinition(registry.GetId("asteria:sand"));
        var gravel = registry.GetDefinition(registry.GetId("asteria:gravel"));
        Assert.True(sand.HasTag("gravity"));
        Assert.True(gravel.HasTag("gravity"));
        Assert.True(stone.DropsSelf);
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
