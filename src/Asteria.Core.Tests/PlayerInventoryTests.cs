using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventoryTests
{
    private static InventoryStack Block(int id, int count = 1)
    {
        var block = BlockStateSnapshot.FromCell(
            new VoxelCell(new BlockRuntimeId((ushort)id)));
        return new InventoryStack(
            InventoryEntry.FromBlock($"asteria:fixture_{id}", block), count);
    }

    [Fact]
    public void PickupMergesTo64ThenUsesNextSlot()
    {
        var inventory = new PlayerInventory();
        for (var i = 0; i < 65; i++)
            Assert.True(inventory.TryInsert(Block(1)));
        Assert.Equal(64, inventory.SelectedStack!.Quantity);
        Assert.Equal(1, inventory.SlotAt(PlayerInventory.BackpackSlots + 1)!.Quantity);
    }

    [Fact]
    public void FullInventoryDoesNotConsumeNewStacks()
    {
        var inventory = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.TotalSlots; i++)
            Assert.True(inventory.TryInsert(Block(1, 64)));
        Assert.False(inventory.TryInsert(Block(1)));
        Assert.Equal(64, inventory.SelectedStack!.Quantity);
    }

    [Fact]
    public void CursorSwapMergeReturnPreserveQuantities()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(Block(1, 40)));
        Assert.True(inventory.TryInsert(Block(2, 3)));
        Assert.True(inventory.ClickSlot(27));
        Assert.Null(inventory.SelectedStack);
        Assert.True(inventory.ClickSlot(28));
        Assert.Equal(Block(2).Entry, inventory.Cursor!.Entry);
        Assert.True(inventory.TryReturnCursor());
        Assert.Null(inventory.Cursor);
        Assert.Equal(43, inventory.Capture().Backpack.Concat(inventory.Capture().Hotbar)
            .Sum(s => s?.Quantity ?? 0));
    }

    [Fact]
    public void SelectionConsumptionAndBlockDropAreModeIndependent()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(Block(1, 2)));
        Assert.True(inventory.TryConsumeSelected());
        Assert.Equal(1, inventory.SelectedStack!.Quantity);
        Assert.True(inventory.TryDropSelectedBlock(out var dropped));
        Assert.Equal(Block(1).Block, dropped);
        Assert.Null(inventory.SelectedStack);
        Assert.False(inventory.TryDropSelectedBlock(out _));
    }

    [Fact]
    public void CreativeCursorHasValidatedCapacity()
    {
        var inventory = new PlayerInventory();
        var stone = Block(1).Entry;
        Assert.True(inventory.TryCreativePick(stone, 63));
        Assert.False(inventory.TryCreativePick(Block(2).Entry));
        Assert.True(inventory.TryCreativePick(stone));
        Assert.False(inventory.TryCreativePick(stone));
        Assert.Equal(64, inventory.Cursor!.Quantity);
        Assert.True(inventory.DiscardCursor());
    }

    [Fact]
    public void PortableBlockSnapshotRetainsOrientation()
    {
        var inventory = new PlayerInventory();
        var cell = new VoxelCell(new BlockRuntimeId(1),
            orientation: BlockOrientation.X, facing: HorizontalFacing.West);
        var snapshot = BlockStateSnapshot.FromCell(cell);
        Assert.True(inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromBlock("asteria:log", snapshot))));
        Assert.Equal(BlockOrientation.X, inventory.SelectedStack!.Block!.Cell.Orientation);
        Assert.Equal(HorizontalFacing.West, inventory.SelectedStack.Block!.Cell.Facing);
    }

    [Fact]
    public void MetadataVariantsNeverMergeOrMutateCallerDictionary()
    {
        var original = new Dictionary<string, string> { ["target_dimension"] = "asteria:umbral" };
        var umbral = InventoryEntry.FromItem("asteria:dimensional_slicer", original);
        original["target_dimension"] = "asteria:overworld";
        Assert.Equal("asteria:umbral", umbral.Metadata["target_dimension"]);
        var equivalent = InventoryEntry.FromItem("asteria:dimensional_slicer",
            new Dictionary<string, string> { ["target_dimension"] = "asteria:umbral" });
        Assert.Equal(umbral, equivalent);
        Assert.NotEqual(umbral, InventoryEntry.FromItem("asteria:dimensional_slicer"));
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(new InventoryStack(umbral, 32)));
        Assert.True(inventory.TryInsert(new InventoryStack(equivalent, 32)));
        Assert.Equal(64, inventory.SelectedStack!.Quantity);
        Assert.True(inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromItem("asteria:dimensional_slicer"))));
        Assert.Equal(1, inventory.SlotAt(28)!.Quantity);
    }

    [Fact]
    public void ToolStacksRemainUnitaryAcrossCreativeAndClick()
    {
        var tool = InventoryEntry.FromTool("asteria:pickaxe_rustic");
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryCreativePick(tool));
        Assert.False(inventory.TryCreativePick(tool));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryStack(tool, 2));
        Assert.True(inventory.ClickSlot(27));
        Assert.Null(inventory.Cursor);
        Assert.True(inventory.TryInsert(new InventoryStack(tool)));
        Assert.NotNull(inventory.SlotAt(28));
        Assert.False(inventory.TryDropSelectedBlock(out _));
        Assert.Equal(1, inventory.SelectedStack!.Quantity);
    }

    [Fact]
    public void SortingBackpackCompactsCompatibleMetadataStacks()
    {
        var inventory = new PlayerInventory();
        var entry = InventoryEntry.FromItem("asteria:essence_aqua",
            new Dictionary<string, string> { ["source"] = "ocean" });
        Assert.True(inventory.TryCreativePick(entry, 20));
        Assert.True(inventory.ClickSlot(0));
        Assert.True(inventory.TryCreativePick(entry, 30));
        Assert.True(inventory.ClickSlot(1));
        Assert.Equal(20, inventory.SlotAt(0)!.Quantity);
        Assert.Equal(30, inventory.SlotAt(1)!.Quantity);

        Assert.True(inventory.SortBackpack());
        Assert.Equal(50, inventory.SlotAt(0)!.Quantity);
        Assert.Null(inventory.SlotAt(1));
        Assert.False(inventory.SortBackpack());
    }

    [Fact]
    public void IdNamespacesAndMetadataMustBeValid()
    {
        Assert.Throws<ArgumentException>(() => InventoryEntry.FromItem("bad"));
        Assert.Throws<ArgumentException>(() => InventoryEntry.FromTool("asteria:pickaxe",
            new Dictionary<string, string> { [" "] = "test" }));
    }

    [Fact]
    public void SameIdDifferentKindsNeverStack()
    {
        var item = InventoryEntry.FromItem("asteria:bucket");
        var tool = InventoryEntry.FromTool("asteria:bucket");
        Assert.NotEqual(item, tool);
        Assert.False(new InventoryStack(item).CanStackWith(new InventoryStack(tool)));
    }

    [Fact]
    public void RightClickSplitsOddStackAndDepositsExactlyOne()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(Block(1, 5)));
        Assert.True(inventory.ClickSlot(27, rightButton: true));
        Assert.Equal(3, inventory.Cursor!.Quantity);
        Assert.Equal(2, inventory.SlotAt(27)!.Quantity);

        Assert.True(inventory.ClickSlot(28, rightButton: true));
        Assert.Equal(2, inventory.Cursor!.Quantity);
        Assert.Equal(1, inventory.SlotAt(28)!.Quantity);
        Assert.True(inventory.ClickSlot(27, rightButton: true));
        Assert.Equal(3, inventory.SlotAt(27)!.Quantity);
        Assert.Equal(1, inventory.Cursor!.Quantity);
        Assert.True(inventory.ClickSlot(29, rightButton: true));
        Assert.Null(inventory.Cursor);
        Assert.False(inventory.ClickSlot(29, rightButton: true) &&
                     inventory.SlotAt(29) is null);
        Assert.Equal(5, inventory.Capture().Backpack.Concat(inventory.Capture().Hotbar)
            .Sum(stack => stack?.Quantity ?? 0));
    }

    [Fact]
    public void RightClickRejectsIncompatibleAndFullStacksWithoutRevision()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(Block(1, 64)));
        Assert.True(inventory.TryInsert(Block(2, 5)));
        Assert.True(inventory.ClickSlot(28, rightButton: true));
        var cursor = inventory.Cursor;
        var before = inventory.Revision;
        Assert.False(inventory.ClickSlot(27, rightButton: true));
        Assert.Equal(before, inventory.Revision);
        Assert.Equal(cursor, inventory.Cursor);
        Assert.Equal(64, inventory.SlotAt(27)!.Quantity);
        Assert.True(inventory.ClickSlot(28, rightButton: true));
        Assert.Equal(3, inventory.SlotAt(28)!.Quantity);
    }

    [Fact]
    public void ShiftClickBetweenHotbarAndBackpackMergesBeforeCreatingSlots()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(Block(1, 40)));
        Assert.True(inventory.QuickMoveSlot(27));
        Assert.Null(inventory.SlotAt(27));
        Assert.Equal(40, inventory.SlotAt(0)!.Quantity);
        Assert.True(inventory.TryInsert(Block(1, 30)));
        Assert.True(inventory.QuickMoveSlot(27));
        Assert.Null(inventory.SlotAt(27));
        Assert.Equal(64, inventory.SlotAt(0)!.Quantity);
        Assert.Equal(6, inventory.SlotAt(1)!.Quantity);
        Assert.True(inventory.QuickMoveSlot(1));
        Assert.Null(inventory.SlotAt(1));
        Assert.Equal(6, inventory.SlotAt(27)!.Quantity);
        Assert.Equal(70, inventory.Capture().Backpack.Concat(inventory.Capture().Hotbar)
            .Sum(stack => stack?.Quantity ?? 0));
        Assert.Null(inventory.Cursor);
    }

    [Fact]
    public void ShiftClickWhenDestinationIsFullDoesNotChangeAnything()
    {
        var inventory = new PlayerInventory();
        for (var i = 0; i < PlayerInventory.HotbarSlots; i++)
            Assert.True(inventory.TryInsert(Block(2, 64)));
        Assert.True(inventory.TryInsert(Block(1, 9)));
        var before = inventory.Capture();
        var revision = inventory.Revision;
        Assert.False(inventory.QuickMoveSlot(0));
        Assert.False(inventory.QuickMoveSlot(-1));
        Assert.Equal(revision, inventory.Revision);
        Assert.Equal(before.Backpack, inventory.Capture().Backpack);
        Assert.Equal(before.Hotbar, inventory.Capture().Hotbar);
    }

    [Fact]
    public void RightClickExternalRespectsMetadataAndLeavesCursorIntactOnRejection()
    {
        var inventory = new PlayerInventory();
        var water = InventoryEntry.FromItem("asteria:bucket",
            new Dictionary<string, string> { ["contained_fluid"] = "asteria:water" });
        var lava = InventoryEntry.FromItem("asteria:bucket",
            new Dictionary<string, string> { ["contained_fluid"] = "asteria:lava" });
        Assert.True(inventory.TryCreativePick(water, 7));
        var before = inventory.Revision;
        Assert.False(inventory.ClickExternalSlot(new InventoryStack(lava, 3),
            out var untouched, rightButton: true));
        Assert.Equal(3, untouched!.Quantity);
        Assert.Equal(before, inventory.Revision);
        Assert.Equal(7, inventory.Cursor!.Quantity);
    }

}
