using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StorageBoxRuntimeTests
{
    private static (StorageBoxRuntime Storage, VoxelWorld World, BlockRegistry Blocks)
        OpenStorage(int x = 3, int y = 5, int z = 3)
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:storage_box"),
            new BlockDefinition("asteria:stone"),
        ]);
        var position = new WorldVoxelCoord(x, y, z);
        Assert.True(world.SetBlockAt(
            position, blocks.GetId("asteria:storage_box"), out _));
        var storage = new StorageBoxRuntime();
        Assert.True(storage.TryOpen(position, world, blocks));
        return (storage, world, blocks);
    }

    [Fact]
    public void StorageBoxIsExactlyTwentySevenSlotsAndRetainsPosition()
    {
        var (storage, _, _) = OpenStorage();
        var snapshot = Assert.IsType<StorageBoxSnapshot>(
            storage.CaptureActive());
        Assert.Equal(StorageBoxRuntime.SlotCount, snapshot.Slots.Length);
        Assert.All(snapshot.Slots, slot => Assert.Null(slot));
        Assert.Equal(new WorldVoxelCoord(3, 5, 3), snapshot.Position);
    }

    [Fact]
    public void CursorTransferMergesAndSwapsWithoutDuplicatingItems()
    {
        var (storage, _, _) = OpenStorage();
        var player = new PlayerInventory();
        var entry = InventoryEntry.FromItem("asteria:wood", maxStackSize: 64);
        Assert.True(player.TryCreativePick(entry, 28));
        Assert.True(storage.TryClickActive(0, player));
        Assert.Null(player.Cursor);
        Assert.Equal(28, storage.CaptureActive()!.Slots[0]!.Quantity);

        Assert.True(player.TryCreativePick(entry, 40));
        Assert.True(storage.TryClickActive(0, player));
        Assert.Equal(64, storage.CaptureActive()!.Slots[0]!.Quantity);
        Assert.Equal(4, player.Cursor!.Quantity);
        Assert.False(storage.TryClickActive(0, player));
        Assert.True(storage.TryClickActive(1, player));
        Assert.Null(player.Cursor);
        Assert.Equal(4, storage.CaptureActive()!.Slots[1]!.Quantity);

        Assert.True(storage.TryClickActive(0, player));
        Assert.Equal(64, player.Cursor!.Quantity);
        Assert.Null(storage.CaptureActive()!.Slots[0]);
        Assert.Equal(4, storage.CaptureActive()!.Slots[1]!.Quantity);
    }

    [Fact]
    public void InsertionFailureDoesNotPartiallyModifyStorage()
    {
        var (storage, _, _) = OpenStorage();
        var single = InventoryEntry.FromTool("asteria:special_tool");
        for (var i = 0; i < StorageBoxRuntime.SlotCount; i++)
            Assert.True(storage.TryInsertActive(new InventoryStack(single)));

        var previous = storage.CaptureActive()!;
        Assert.False(storage.TryInsertActive(new InventoryStack(single)));
        Assert.Equal(previous.Revision, storage.Revision);
        Assert.Equal(previous.Slots, storage.CaptureActive()!.Slots);
    }

    [Fact]
    public void DrainingRemovedBoxReturnsItemsOnceAndClosesTheModal()
    {
        var (storage, _, _) = OpenStorage();
        var stone = InventoryEntry.FromItem("asteria:stone");
        Assert.True(storage.TryInsertActive(new InventoryStack(stone, 4)));
        var position = new WorldVoxelCoord(3, 5, 3);
        Assert.Single(storage.Drain(position));
        Assert.Null(storage.ActivePosition);
        Assert.Null(storage.CaptureActive());
        Assert.Empty(storage.Drain(position));
        Assert.Empty(storage.CaptureOccupied());
    }

    [Fact]
    public void UnloadedOrNonStorageBlocksCannotOpen()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:storage_box"),
            new BlockDefinition("asteria:stone"),
        ]);
        var world = new VoxelWorld();
        var storage = new StorageBoxRuntime();
        var position = new WorldVoxelCoord(3, 5, 3);
        Assert.False(storage.TryOpen(position, world, blocks));
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.True(world.SetBlockAt(
            position, blocks.GetId("asteria:stone"), out _));
        Assert.False(storage.TryOpen(position, world, blocks));
        Assert.Equal(0, storage.Count);
    }
}
