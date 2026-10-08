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
