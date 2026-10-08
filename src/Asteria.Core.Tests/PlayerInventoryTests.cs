using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventoryTests
{
    private static BlockStateSnapshot Block(int id) =>
        BlockStateSnapshot.FromCell(new VoxelCell(new BlockRuntimeId((ushort)id)));

    [Fact]
    public void PickupMergesTo64ThenUsesNextSlot()
    {
        var inventory = new PlayerInventory();
        var stone = Block(1);
        for (var i = 0; i < 65; i++)
            Assert.True(inventory.TryInsert(stone));
        Assert.Equal(64, inventory.SelectedStack!.Quantity);
        Assert.Equal(1, inventory.SlotAt(PlayerInventory.BackpackSlots + 1)!.Quantity);
        Assert.Equal(65, inventory.Capture().Hotbar.Sum(s => s?.Quantity ?? 0));
    }

    [Fact]
    public void FullInventoryNeverConsumesIncomingBlock()
    {
        var inventory = new PlayerInventory();
        var block = Block(1);
        for (var i = 0; i < PlayerInventory.TotalSlots; i++)
            Assert.True(inventory.TryInsert(new InventoryBlockStack(block, 64)));
        Assert.False(inventory.TryInsert(block));
        Assert.Equal(64, inventory.SelectedStack!.Quantity);
    }

    [Fact]
    public void CursorSwapMergeAndReturnPreserveCounts()
    {
        var inventory = new PlayerInventory();
        var stone = Block(1);
        var dirt = Block(2);
        Assert.True(inventory.TryInsert(new InventoryBlockStack(stone, 40)));
        Assert.True(inventory.TryInsert(new InventoryBlockStack(dirt, 3)));
        Assert.True(inventory.ClickSlot(27));
        Assert.Equal(stone, inventory.Cursor!.Block);
        Assert.Null(inventory.SelectedStack);
        Assert.True(inventory.ClickSlot(28));
        Assert.Equal(dirt, inventory.Cursor!.Block);
        Assert.Equal(stone, inventory.SlotAt(28)!.Block);
        Assert.True(inventory.TryReturnCursor());
        Assert.Null(inventory.Cursor);
        Assert.Equal(43,
            inventory.Capture().Backpack.Concat(inventory.Capture().Hotbar)
                .Sum(s => s?.Quantity ?? 0));
    }

    [Fact]
    public void SelectionConsumptionAndDropAreModeIndependent()
    {
        var inventory = new PlayerInventory();
        var stone = Block(1);
        Assert.True(inventory.TryInsert(new InventoryBlockStack(stone, 2)));
        Assert.True(inventory.TryConsumeSelected());
        Assert.Equal(1, inventory.SelectedStack!.Quantity);
        Assert.True(inventory.TryDropSelected(out var dropped));
        Assert.Equal(stone, dropped);
        Assert.Null(inventory.SelectedStack);
        Assert.False(inventory.TryDropSelected(out _));
    }

    [Fact]
    public void CreativePickRequiresCursorCompatibilityAndNeverOverflows()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryCreativePick(Block(1), 63));
        Assert.False(inventory.TryCreativePick(Block(2), 1));
        Assert.True(inventory.TryCreativePick(Block(1), 1));
        Assert.False(inventory.TryCreativePick(Block(1), 1));
        Assert.Equal(64, inventory.Cursor!.Quantity);
        Assert.True(inventory.DiscardCursor());
        Assert.Null(inventory.Cursor);
    }

    [Fact]
    public void StatePreservesCompleteBlockOrientation()
    {
        var inventory = new PlayerInventory();
        var block = BlockStateSnapshot.FromCell(
            new VoxelCell(new BlockRuntimeId(1),
                orientation: BlockOrientation.X, facing: HorizontalFacing.West));
        Assert.True(inventory.TryInsert(block));
        Assert.Equal(BlockOrientation.X,
            inventory.SelectedStack!.Block.Cell.Orientation);
        Assert.Equal(HorizontalFacing.West,
            inventory.SelectedStack.Block.Cell.Facing);
    }
}
