using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class InventoryContentCatalogTests
{
    private static InventoryContentCatalog CreateCatalog() =>
        new(
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            PackContentRegistry<ItemDefinition>.FromJson([
                """
                {
                  "id":"asteria:dimensional_slicer",
                  "category":"tools",
                  "icon":"textures/items/dimensional_slicer.png",
                  "iconVariants":[
                    {"metadataKey":"target_dimension","metadataValue":"asteria:umbral",
                     "icon":"textures/items/dimensional_slicer_umbral.png"}
                  ]
                }
                """
            ], ItemDefinition.Parse, x => x.Id),
            PackContentRegistry<ToolDefinition>.FromJson([
                """
                {
                  "id":"asteria:pickaxe_rustic",
                  "category":"tools",
                  "icon":"textures/tools/pickaxe-rustic.png",
                  "leftBehavior":"asteria:mine",
                  "rightBehavior":"asteria:none"
                }
                """
            ], ToolDefinition.Parse, x => x.Id));

    [Fact]
    public void OnlyAuthoredVariantsCanBeCreativePicked()
    {
        var catalog = CreateCatalog();
        Assert.Equal(4, catalog.Choices.Count);
        Assert.Equal(
            "textures/items/dimensional_slicer_umbral.png",
            catalog.Choices.Single(choice =>
                choice.Entry.Metadata.ContainsKey("target_dimension")).IconResourcePath);
        Assert.True(catalog.TryResolve(
            InventoryEntryKind.Item, "asteria:dimensional_slicer",
            "target_dimension", "asteria:umbral", out var item));
        Assert.Equal("asteria:umbral", item!.Metadata["target_dimension"]);
        Assert.False(catalog.TryResolve(
            InventoryEntryKind.Item, "asteria:dimensional_slicer",
            "target_dimension", "asteria:overworld", out _));
        Assert.False(catalog.TryResolve(
            InventoryEntryKind.Tool, "asteria:dimensional_slicer", null, null, out _));
        Assert.True(catalog.TryResolve(
            InventoryEntryKind.Tool, "asteria:pickaxe_rustic", null, null, out var tool));
        Assert.Equal(1, tool!.MaxStackSize);
        Assert.True(catalog.TryResolve(
            InventoryEntryKind.Block, "asteria:stone", null, null, out var block));
        Assert.NotNull(block!.Block);
    }

    [Fact]
    public void AuthoredStackLimitsAreValidatedAndApplied()
    {
        var item = ItemDefinition.Parse("""
            {
              "id":"asteria:compact_item",
              "category":"materials",
              "icon":"textures/items/compact.png",
              "maxStackSize":16
            }
            """);
        Assert.Equal(16, item.MaxStackSize);
        var tool = ToolDefinition.Parse("""
            {
              "id":"asteria:stackable_tool",
              "category":"tools",
              "icon":"textures/tools/tool.png",
              "leftBehavior":"asteria:none",
              "rightBehavior":"asteria:none",
              "maxStackSize":4
            }
            """);
        Assert.Equal(4, tool.MaxStackSize);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse("""
            {
              "id":"asteria:invalid",
              "category":"materials",
              "icon":"textures/items/x.png",
              "maxStackSize":65
            }
            """));
        Assert.Throws<FormatException>(() => ToolDefinition.Parse("""
            {
              "id":"asteria:invalid_tool",
              "category":"tools",
              "icon":"textures/tools/x.png",
              "leftBehavior":"asteria:none",
              "rightBehavior":"asteria:none",
              "maxStackSize":0
            }
            """));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InventoryEntry.FromItem("asteria:x", maxStackSize: 65));
    }

    [Fact]
    public void DropsPreserveFullBlockGeometryIdentity()
    {
        var catalog = CreateCatalog();
        var snapshot = BlockStateSnapshot.FromCell(
            new VoxelCell(new BlockRuntimeId(1),
                facing: HorizontalFacing.North,
                orientation: BlockOrientation.Z));
        var item = catalog.ForDroppedBlock(snapshot);
        Assert.Equal(InventoryEntryKind.Block, item.Kind);
        Assert.Equal(snapshot, item.Block);
    }
}
