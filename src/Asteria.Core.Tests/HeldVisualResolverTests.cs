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
    public void AuthoredHandToolsSelectGrippedSpriteAndBrushMask()
    {
        var brushJson = """
          {"id":"asteria:brush_rustic","category":"tools",
           "icon":"textures/tools/brush.png",
           "tintIcon":"textures/tools/brush-tint.png",
           "heldPresentation":"grippedSprite",
           "leftBehavior":"asteria:brush/paint",
           "rightBehavior":"asteria:brush/open_palette"}
          """;
        var kitJson = """
          {"id":"asteria:artisans_kit_rustic","category":"tools",
           "icon":"textures/tools/artisans-kit.png",
           "heldPresentation":"grippedSprite",
           "leftBehavior":"asteria:artisans_kit/remove",
           "rightBehavior":"asteria:artisans_kit/restore"}
          """;
        var ordinaryJson = """
          {"id":"asteria:hatchet","category":"tools",
           "icon":"textures/tools/hatchet.png",
           "leftBehavior":"asteria:mine",
           "rightBehavior":"asteria:log/strip"}
          """;
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [brushJson, kitJson, ordinaryJson],
            ToolDefinition.Parse, definition => definition.Id);
        var resolver = new HeldVisualResolver(new BlockRegistry([]),
            PackContentRegistry<ItemDefinition>.FromJson([], ItemDefinition.Parse, definition => definition.Id),
            tools, new AttachedLayerRegistry([]));
        var heldBrush = resolver.Resolve(new InventoryStack(
            InventoryEntry.FromTool("asteria:brush_rustic")))!;
        Assert.Equal(HeldVisualKind.GrippedSprite, heldBrush.Kind);
        Assert.Equal("textures/tools/brush-tint.png", heldBrush.TintTexture);
        var heldKit = resolver.Resolve(new InventoryStack(
            InventoryEntry.FromTool("asteria:artisans_kit_rustic")))!;
        Assert.Equal(HeldVisualKind.GrippedSprite, heldKit.Kind);
        Assert.Null(heldKit.TintTexture);
        Assert.Equal(HeldVisualKind.Sprite, resolver.Resolve(new InventoryStack(
            InventoryEntry.FromTool("asteria:hatchet")))!.Kind);
        Assert.Throws<FormatException>(() => ToolDefinition.Parse("""
          {"id":"asteria:invalid","category":"tools","icon":"textures/tools/invalid.png",
           "heldPresentation":"unknown","leftBehavior":"asteria:mine",
           "rightBehavior":"asteria:mine"}
          """));
    }

    [Fact]
    public void CubeUsesDifferentAuthoredFaceTextures()
    {
        var block = new BlockDefinition("asteria:soil",
            textures: new BlockTextureSet(
                top: [new BlockTextureLayer("textures/top.png")],
                bottom: [new BlockTextureLayer("textures/bottom.png")],
                front: [new BlockTextureLayer("textures/front.png")],
                back: [new BlockTextureLayer("textures/back.png")],
                left: [new BlockTextureLayer("textures/left.png")],
                right: [new BlockTextureLayer("textures/right.png")]));
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
        Assert.Equal("textures/back.png", visual.BackTexture);
        Assert.Equal("textures/left.png", visual.LeftTexture);
        Assert.Equal("textures/right.png", visual.RightTexture);
    }

    private static HeldVisualResolver Create(
        ItemDefinition[]? items = null) =>
        new(new BlockRegistry([]),
            PackContentRegistry<ItemDefinition>.FromJson((items ?? []).Select(item => System.Text.Json.JsonSerializer.Serialize(new { id = item.Id, category = item.Category, icon = item.Icon, iconVariants = item.IconVariants.Select(variant => new { metadataKey = variant.MetadataKey, metadataValue = variant.MetadataValue, icon = variant.Icon }).ToArray() })), ItemDefinition.Parse, definition => definition.Id),
            PackContentRegistry<ToolDefinition>.FromJson([], ToolDefinition.Parse, definition => definition.Id),
            new AttachedLayerRegistry([]));
}
