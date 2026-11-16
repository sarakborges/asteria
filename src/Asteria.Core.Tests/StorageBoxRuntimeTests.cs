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
            new BlockDefinition("asteria:box_storage"),
            new BlockDefinition("asteria:stone"),
        ]);
        var position = new WorldVoxelCoord(x, y, z);
        Assert.True(world.SetBlockAt(
            position, blocks.GetId("asteria:box_storage"), out _));
        var storage = new StorageBoxRuntime();
        Assert.True(storage.TryOpen(position, world, blocks));
        return (storage, world, blocks);
    }

    [Fact]
    public void StorageBoxIsExactlyTwentySevenSlotsAndRetainsPosition()
    {
        var (storage, world, blocks) = OpenStorage();
        var snapshot = Assert.IsType<StorageBoxSnapshot>(
            storage.CaptureActive());
        Assert.Equal(StorageBoxRuntime.SlotCount, snapshot.Slots.Length);
        Assert.All(snapshot.Slots, slot => Assert.Null(slot));
        Assert.Equal(new WorldVoxelCoord(3, 5, 3), snapshot.Position);
    }

    [Fact]
    public void CursorTransferMergesAndSwapsWithoutDuplicatingItems()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        var entry = InventoryEntry.FromItem("asteria:wood", maxStackSize: 64);
        Assert.True(player.TryCreativePick(entry, 28));
        Assert.True(storage.TryClickActive(0, player, world, blocks));
        Assert.Null(player.Cursor);
        Assert.Equal(28, storage.CaptureActive()!.Slots[0]!.Quantity);

        Assert.True(player.TryCreativePick(entry, 40));
        Assert.True(storage.TryClickActive(0, player, world, blocks));
        Assert.Equal(64, storage.CaptureActive()!.Slots[0]!.Quantity);
        Assert.Equal(4, player.Cursor!.Quantity);
        Assert.False(storage.TryClickActive(0, player, world, blocks));
        Assert.True(storage.TryClickActive(1, player, world, blocks));
        Assert.Null(player.Cursor);
        Assert.Equal(4, storage.CaptureActive()!.Slots[1]!.Quantity);

        Assert.True(storage.TryClickActive(0, player, world, blocks));
        Assert.Equal(64, player.Cursor!.Quantity);
        Assert.Null(storage.CaptureActive()!.Slots[0]);
        Assert.Equal(4, storage.CaptureActive()!.Slots[1]!.Quantity);
    }

    [Fact]
    public void InsertionFailureDoesNotPartiallyModifyStorage()
    {
        var (storage, world, blocks) = OpenStorage();
        var single = InventoryEntry.FromTool("asteria:special_tool");
        for (var i = 0; i < StorageBoxRuntime.SlotCount; i++)
            Assert.True(storage.TryInsertActive(new InventoryStack(single), world, blocks));

        var previous = storage.CaptureActive()!;
        Assert.False(storage.TryInsertActive(new InventoryStack(single), world, blocks));
        Assert.Equal(previous.Revision, storage.Revision);
        Assert.Equal(previous.Slots, storage.CaptureActive()!.Slots);
    }

    [Fact]
    public void DrainingRemovedBoxReturnsItemsOnceAndClosesTheModal()
    {
        var (storage, world, blocks) = OpenStorage();
        var stone = InventoryEntry.FromItem("asteria:stone");
        Assert.True(storage.TryInsertActive(new InventoryStack(stone, 4), world, blocks));
        var position = new WorldVoxelCoord(3, 5, 3);
        Assert.Single(storage.Drain(position));
        Assert.Null(storage.ActivePosition);
        Assert.Null(storage.CaptureActive());
        Assert.Empty(storage.Drain(position));
        Assert.Empty(storage.CaptureOccupied());
    }

    [Fact]
    public void RemovedOrUnloadedBoxCannotAcceptCursorTransactions()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        Assert.True(player.TryCreativePick(
            InventoryEntry.FromItem("asteria:wood"), 3));
        var playerRevision = player.Revision;
        world.ArchiveChunk(ChunkCoord.Zero);

        Assert.False(storage.TryClickActive(0, player, world, blocks));
        Assert.False(storage.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem("asteria:wood")),
            world, blocks));
        Assert.Equal(playerRevision, player.Revision);
        Assert.Equal(3, player.Cursor!.Quantity);

        Assert.Equal(ChunkRestoreResult.Restored,
            world.RestoreChunk(ChunkCoord.Zero));
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(3, 5, 3),
            blocks.GetId("asteria:stone"), out _));
        Assert.False(storage.TryClickActive(0, player, world, blocks));
        Assert.Equal(3, player.Cursor!.Quantity);
    }

    [Fact]
    public void DetachedSnapshotAndDeterministicOccupiedOrder()
    {
        var (storage, world, blocks) = OpenStorage();
        var original = storage.CaptureActive()!;
        Assert.True(storage.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem("asteria:wood"), 4),
            world, blocks));
        Assert.Null(original.Slots[0]);
        var copied = storage.CaptureActive()!;
        copied.Slots[0] = null;
        Assert.Equal(4, storage.CaptureActive()!.Slots[0]!.Quantity);

        var left = new WorldVoxelCoord(1, 5, 3);
        Assert.True(world.SetBlockAt(left,
            blocks.GetId("asteria:box_storage"), out _));
        Assert.True(storage.TryOpen(left, world, blocks));
        Assert.True(storage.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem("asteria:wood"), 2),
            world, blocks));
        var occupied = storage.CaptureOccupied();
        Assert.Equal(2, occupied.Count);
        Assert.Equal(left, occupied[0].Position);
        Assert.Equal(new WorldVoxelCoord(3, 5, 3), occupied[1].Position);
    }

    [Fact]
    public void UnloadedOrNonStorageBlocksCannotOpen()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:box_storage"),
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

    [Fact]
    public void CommittedBlockRemovalDropsEachStoredItemExactlyOnce()
    {
        var (storage, world, blocks) = OpenStorage();
        var position = new WorldVoxelCoord(3, 5, 3);
        var entry = InventoryEntry.FromItem("asteria:wood",
            new Dictionary<string, string> { ["quality"] = "fine" });
        Assert.True(storage.TryInsertActive(
            new InventoryStack(entry, 4), world, blocks));

        var drops = new DroppedBlockRuntime(world, blocks);
        var lifecycle = new StorageBoxBlockLifecycle(storage, blocks, drops);
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        mutations.BlockCellChanged += lifecycle.OnBlockCellChanged;

        // Replacing the box through the canonical mutation pipeline
        // drains its contents even when the replacement is not air.
        Assert.True(mutations.SetBlockAt(
            position, blocks.GetId("asteria:stone"), out _));
        Assert.Null(storage.ActivePosition);
        Assert.Empty(storage.CaptureOccupied());
        Assert.Equal(4, drops.ActiveCount);
        Assert.All(drops.ActiveBlocks, drop =>
        {
            Assert.Equal("asteria:wood", drop.Stack.Id);
            Assert.Equal(1, drop.Stack.Quantity);
            Assert.Equal("fine", drop.Stack.Entry.Metadata["quality"]);
        });

        Assert.False(mutations.SetBlockAt(
            position, blocks.GetId("asteria:stone"), out _));
        Assert.Equal(4, drops.ActiveCount);
        Assert.Empty(storage.Drain(position));
    }

    [Fact]
    public void UnloadingClosesModalWithoutDrainingStoredItems()
    {
        var (storage, world, blocks) = OpenStorage();
        Assert.True(storage.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem("asteria:wood"), 7),
            world, blocks));

        world.ArchiveChunk(ChunkCoord.Zero);
        Assert.True(storage.CloseIfUnavailable(world, blocks));
        Assert.Null(storage.ActivePosition);
        Assert.Single(storage.CaptureOccupied());
        Assert.False(storage.CloseIfUnavailable(world, blocks));

        Assert.Equal(ChunkRestoreResult.Restored,
            world.RestoreChunk(ChunkCoord.Zero));
        Assert.True(storage.TryOpen(new WorldVoxelCoord(3, 5, 3), world, blocks));
        Assert.Equal(7, storage.CaptureActive()!.Slots[0]!.Quantity);
    }
    [Fact]
    public void SortingStorageIsDeterministicAndLeavesPlayerCursorUntouched()
    {
        var (storage, world, blocks) = OpenStorage();
        var wood = InventoryEntry.FromItem("asteria:wood");
        var stone = InventoryEntry.FromItem("asteria:stone");
        var player = new PlayerInventory();
        Assert.True(storage.TryInsertActive(new InventoryStack(wood, 7), world, blocks));
        Assert.True(storage.TryInsertActive(new InventoryStack(stone, 3), world, blocks));
        Assert.True(player.TryCreativePick(wood, 5));
        var cursor = player.Cursor;
        Assert.True(storage.TrySortActive(world, blocks));
        var ordered = storage.CaptureActive()!;
        Assert.Equal("asteria:stone", ordered.Slots[0]!.Id);
        Assert.Equal(3, ordered.Slots[0]!.Quantity);
        Assert.Equal("asteria:wood", ordered.Slots[1]!.Id);
        Assert.Equal(7, ordered.Slots[1]!.Quantity);
        Assert.Equal(cursor, player.Cursor);
        Assert.False(storage.TrySortActive(world, blocks));
        Assert.Equal(ordered.Revision, storage.Revision);

        world.ArchiveChunk(ChunkCoord.Zero);
        Assert.False(storage.TrySortActive(world, blocks));
        Assert.Equal(ordered.Revision, storage.Revision);
    }

    [Fact]
    public void SortingKeepsDifferentMetadataAsDistinctStacks()
    {
        var (storage, world, blocks) = OpenStorage();
        var first = InventoryEntry.FromItem("asteria:wood",
            new Dictionary<string, string> { ["variant"] = "b" });
        var second = InventoryEntry.FromItem("asteria:wood",
            new Dictionary<string, string> { ["variant"] = "a" });
        Assert.True(storage.TryInsertActive(new InventoryStack(first, 12), world, blocks));
        Assert.True(storage.TryInsertActive(new InventoryStack(second, 6), world, blocks));
        Assert.True(storage.TrySortActive(world, blocks));
        var slots = storage.CaptureActive()!.Slots;
        Assert.Equal("a", slots[0]!.Entry.Metadata["variant"]);
        Assert.Equal(6, slots[0]!.Quantity);
        Assert.Equal("b", slots[1]!.Entry.Metadata["variant"]);
        Assert.Equal(12, slots[1]!.Quantity);
    }


    [Fact]
    public void StorageRightClickSplitsAndPlacesSingleItems()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        var wood = InventoryEntry.FromItem("asteria:wood");
        Assert.True(storage.TryInsertActive(new InventoryStack(wood, 5), world, blocks));
        Assert.True(storage.TryClickActive(0, player, world, blocks, rightButton: true));
        Assert.Equal(3, player.Cursor!.Quantity);
        Assert.Equal(2, storage.CaptureActive()!.Slots[0]!.Quantity);
        Assert.True(storage.TryClickActive(1, player, world, blocks, rightButton: true));
        Assert.Equal(2, player.Cursor!.Quantity);
        Assert.Equal(1, storage.CaptureActive()!.Slots[1]!.Quantity);
        Assert.True(storage.TryClickActive(0, player, world, blocks, rightButton: true));
        Assert.Equal(3, storage.CaptureActive()!.Slots[0]!.Quantity);
        Assert.Equal(1, player.Cursor!.Quantity);
        Assert.True(storage.TryClickActive(1, player, world, blocks, rightButton: true));
        Assert.Null(player.Cursor);
        Assert.Equal(2, storage.CaptureActive()!.Slots[1]!.Quantity);
    }

    [Fact]
    public void QuickTransferBothDirectionsPreservesMetadataAndCursor()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        var wood = InventoryEntry.FromItem("asteria:wood",
            new Dictionary<string, string> { ["quality"] = "fine" });
        Assert.True(player.TryCreativePick(
            InventoryEntry.FromItem("asteria:stone"), 3));
        var initialCursor = player.Cursor;

        Assert.True(storage.TryInsertActive(new InventoryStack(wood, 38), world, blocks));
        Assert.True(storage.TryQuickTransferActive(0, player, world, blocks));
        Assert.Null(storage.CaptureActive()!.Slots[0]);
        Assert.Equal(38, player.SlotAt(27)!.Quantity);
        Assert.Equal("fine", player.SlotAt(27)!.Entry.Metadata["quality"]);
        Assert.Equal(initialCursor, player.Cursor);

        Assert.True(storage.TryQuickTransferPlayer(27, player, world, blocks));
        Assert.Null(player.SlotAt(27));
        Assert.Equal(38, storage.CaptureActive()!.Slots[0]!.Quantity);
        Assert.Equal(initialCursor, player.Cursor);
    }

    [Fact]
    public void QuickTransferAcceptsPartialCapacityButRejectsFullOrClosedBoxes()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        var stone = InventoryEntry.FromItem("asteria:stone");
        var wood = InventoryEntry.FromItem("asteria:wood");
        for (var i = 0; i < PlayerInventory.TotalSlots - 1; i++)
            Assert.True(player.TryInsert(new InventoryStack(stone, 64)));
        Assert.True(player.TryInsert(new InventoryStack(wood, 62)));
        Assert.True(storage.TryInsertActive(new InventoryStack(wood, 10), world, blocks));

        Assert.True(storage.TryQuickTransferActive(0, player, world, blocks));
        Assert.Equal(64, player.SlotAt(26)!.Quantity);
        Assert.Equal(8, storage.CaptureActive()!.Slots[0]!.Quantity);
        var storageRevision = storage.Revision;
        var playerRevision = player.Revision;
        Assert.False(storage.TryQuickTransferActive(0, player, world, blocks));
        Assert.Equal(storageRevision, storage.Revision);
        Assert.Equal(playerRevision, player.Revision);

        world.ArchiveChunk(ChunkCoord.Zero);
        Assert.False(storage.TryQuickTransferPlayer(27, player, world, blocks));
        Assert.False(storage.TryQuickTransferActive(0, player, world, blocks));
        Assert.Equal(storageRevision, storage.Revision);
        Assert.Equal(playerRevision, player.Revision);
    }

    [Fact]
    public void QuickTransferToFullStorageDoesNotConsumePlayerSlots()
    {
        var (storage, world, blocks) = OpenStorage();
        var player = new PlayerInventory();
        var stone = InventoryEntry.FromTool("asteria:stone_tool");
        for (var i = 0; i < StorageBoxRuntime.SlotCount; i++)
            Assert.True(storage.TryInsertActive(new InventoryStack(stone), world, blocks));
        var wood = InventoryEntry.FromItem("asteria:wood");
        Assert.True(player.TryInsert(new InventoryStack(wood, 9)));
        var playerRevision = player.Revision;
        var storageRevision = storage.Revision;
        Assert.False(storage.TryQuickTransferPlayer(27, player, world, blocks));
        Assert.Equal(playerRevision, player.Revision);
        Assert.Equal(storageRevision, storage.Revision);
        Assert.Equal(9, player.SlotAt(27)!.Quantity);
    }

}
