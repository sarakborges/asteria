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
            blocks.GetId("asteria:storage_box"), out _));
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

}
