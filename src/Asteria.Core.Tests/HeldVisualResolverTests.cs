using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class HeldVisualResolverTests
{
    [Fact]
    public void SelectsAuthoredMetadataIconAndFallsBackToBase()
    {
        var item = ItemDefinition.Parse("""
          {"id":"asteria:flask","category":"tools","icon":"textures/items/empty.png",
           "iconVariants":[{"metadataKey":"fluid","metadataValue":"water",
             "icon":"textures/items/water.png"}]}
          """);
        var resolver = Create(items: [item]);
        Assert.Equal("textures/items/water.png", resolver.Resolve(
            new InventoryStack(InventoryEntry.FromItem("asteria:flask",
                new Dictionary<string,string> { ["fluid"] = "water" })))!.FrontTexture);
        Assert.Equal("textures/items/empty.png", resolver.Resolve(
            new InventoryStack(InventoryEntry.FromItem("asteria:flask")))!.FrontTexture);
        Assert.Null(resolver.Resolve(null));
    }

    [Fact]
    public void CubeUsesDifferentAuthoredFaceTextures()
    {
        var block = new BlockDefinition("asteria:soil",
            textures: new BlockTextureSet(
                top: [new BlockTextureLayer("textures/top.png")],
                bottom: [new BlockTextureLayer("textures/bottom.png")],
                front: [new BlockTextureLayer("textures/front.png")]));
        var registry = new BlockRegistry([block]);
        var resolver = new HeldVisualResolver(registry,
            PackContentRegistry<ItemDefinition>.FromJson([], ItemDefinition.Parse, definition => definition.Id),
            PackContentRegistry<ToolDefinition>.FromJson([], ToolDefinition.Parse, definition => definition.Id),
            new AttachedLayerRegistry([]));
        var selected = new InventoryStack(InventoryEntry.FromBlock(
            block.Id, BlockStateSnapshot.FromCell(
                new VoxelCell(registry.GetId(block.Id)))));
        var visual = resolver.Resolve(selected)!;
        Assert.Equal(HeldVisualKind.Cube, visual.Kind);
        Assert.Equal("textures/front.png", visual.FrontTexture);
        Assert.Equal("textures/top.png", visual.TopTexture);
        Assert.Equal("textures/bottom.png", visual.BottomTexture);
    }

    private static HeldVisualResolver Create(
        ItemDefinition[]? items = null) =>
        new(new BlockRegistry([]),
            PackContentRegistry<ItemDefinition>.FromJson((items ?? []).Select(item => System.Text.Json.JsonSerializer.Serialize(new { id = item.Id, category = item.Category, icon = item.Icon, iconVariants = item.IconVariants.Select(variant => new { metadataKey = variant.MetadataKey, metadataValue = variant.MetadataValue, icon = variant.Icon }).ToArray() })), ItemDefinition.Parse, definition => definition.Id),
            new PackContentRegistry<ToolDefinition>([]),
            new AttachedLayerRegistry([]));
}
