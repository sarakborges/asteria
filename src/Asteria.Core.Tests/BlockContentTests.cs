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

        Assert.Equal(7, registry.AuthoredCount);

        var grass = registry.GetDefinition(registry.GetId("asteria:grass_block"));
        Assert.Equal(BlockTint.Grass, grass.Tint);
        Assert.True(grass.RotateTexture.Top);
        Assert.True(grass.Textures.Top.Single().Dyable);
        Assert.Equal(2, grass.Textures.Left.Count);
        Assert.True(grass.Textures.Left[1].Dyable);

        var stone = registry.GetDefinition(registry.GetId("asteria:stone"));
        Assert.Equal("stone_blocks", stone.Category);
        Assert.Equal(2f, stone.Mining.Hardness);
        Assert.Contains("pickaxe", stone.Mining.RequiredTools);
        Assert.Equal("textures/blocks/stone.png", stone.Textures.Top.Single().Texture);

        var sand = registry.GetDefinition(registry.GetId("asteria:sand"));
        var gravel = registry.GetDefinition(registry.GetId("asteria:gravel"));
        Assert.True(sand.HasTag("gravity"));
        Assert.True(gravel.HasTag("gravity"));

        Assert.True(registry.GetId("asteria:clay").Value < registry.GetId("asteria:dirt").Value);
        Assert.True(registry.GetId("asteria:mud").Value < registry.GetId("asteria:sand").Value);
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
