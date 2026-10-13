using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DroppedBlockRuntimeTests
{
    [Fact]
    public void PickupLeavesRejectedDropsIntactAndCollectsInIdOrder()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var runtime = new DroppedBlockRuntime(
            LoadedWorld(), blocks);
        var firstBlock = BlockStateSnapshot.FromCell(
            new VoxelCell(blocks.GetId("asteria:stone")));
        var secondBlock = BlockStateSnapshot.FromCell(
            new VoxelCell(blocks.GetId("asteria:dirt")));
        var center = new Vector3(5f, 5f, 5f);
        var first = runtime.Spawn(firstBlock, center);
        var second = runtime.Spawn(secondBlock, center + new Vector3(0.2f, 0f, 0f));
        var distant = runtime.Spawn(firstBlock, new Vector3(10f, 10f, 10f));
        // Newly thrown blocks are not eligible until the pickup grace period.
        runtime.Advance(0.5, gravityStrength: 0);
        var tried = new List<BlockStateSnapshot>();
        var count = runtime.CollectNearby(center, 1.5f, stack =>
        {
            tried.Add(stack.Block!);
            return stack.Block == secondBlock;
        });
        Assert.Equal(1, count);
        Assert.Equal([firstBlock, secondBlock], tried);
        Assert.Equal([first, distant],
            runtime.ActiveBlocks.Select(s => s.Id).ToArray());
        Assert.Equal(1,
            runtime.CollectNearby(center, 1.5f, _ => true));
        Assert.Equal(distant, Assert.Single(runtime.ActiveBlocks).Id);
    }

    [Fact]
    public void NonBlockDropsPreserveMetadataAndRespectPickupCapacity()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var runtime = new DroppedBlockRuntime(LoadedWorld(), blocks);
        var item = InventoryEntry.FromItem("asteria:dimensional_slicer",
            new Dictionary<string, string> {
                ["target_dimension"] = "asteria:umbral",
            });
        var tool = InventoryEntry.FromTool("asteria:pickaxe_rustic");
        var first = runtime.Spawn(new InventoryStack(item), new Vector3(3, 4, 5));
        runtime.Spawn(new InventoryStack(tool), new Vector3(3.2f, 4, 5));
        runtime.Advance(0.5, 0);

        var inventory = new PlayerInventory();
        var count = runtime.CollectNearby(new Vector3(3, 4, 5), 1f,
            stack => stack.Kind == InventoryEntryKind.Tool &&
                inventory.TryInsert(stack));
        Assert.Equal(1, count);
        Assert.Equal(first, Assert.Single(runtime.ActiveBlocks).Id);
        Assert.Equal("asteria:umbral",
            runtime.ActiveBlocks.Single().Stack.Entry.Metadata["target_dimension"]);

        var restored = new DroppedBlockRuntime(
            LoadedWorld(), blocks, restore: runtime.CaptureState());
        Assert.Equal(1, restored.CollectNearby(
            new Vector3(3, 4, 5), 1f, inventory.TryInsert));
        Assert.Empty(restored.ActiveBlocks);
        Assert.Equal("asteria:dimensional_slicer",
            inventory.SlotAt(PlayerInventory.BackpackSlots + 1)!.Id);
        var stacked = runtime.Spawn(new InventoryStack(item, 2),
            new Vector3(3, 4, 5));
        Assert.Equal(2, runtime.ActiveBlocks.Single(
            state => state.Id == stacked).Stack.Quantity);
    }

    [Fact]
    public void PartialPickupPreservesDropIdentityMetadataAndRestorableRemainder()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var runtime = new DroppedBlockRuntime(LoadedWorld(), blocks);
        var metadata = new Dictionary<string, string>
        {
            ["target_dimension"] = "asteria:umbral",
        };
        var entry = InventoryEntry.FromItem("asteria:dimensional_slicer", metadata);
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(new InventoryStack(entry, 63)));
        for (var i = 0; i < PlayerInventory.TotalSlots - 1; i++)
            Assert.True(inventory.TryInsert(new InventoryStack(
                InventoryEntry.FromTool($"asteria:filler_{i}"))));

        var position = new Vector3(3f, 4f, 5f);
        var id = runtime.Spawn(new InventoryStack(entry, 10), position);
        runtime.Advance(0.5, 0);
        var before = runtime.ActiveBlocks.Single();
        var revision = inventory.Revision;
        Assert.Equal(1, runtime.CollectNearbyPartial(
            position, 1f, inventory.InsertUpTo));
        Assert.Equal(revision + 1, inventory.Revision);
        Assert.Equal(64, inventory.SelectedStack!.Quantity);

        var remaining = Assert.Single(runtime.ActiveBlocks);
        Assert.Equal(id, remaining.Id);
        Assert.Equal(9, remaining.Stack.Quantity);
        Assert.Equal(entry, remaining.Stack.Entry);
        Assert.Equal(before.Position, remaining.Position);
        Assert.Equal(before.AgeSeconds, remaining.AgeSeconds);
        Assert.Equal(before.IsSettled, remaining.IsSettled);
        Assert.Equal(0, runtime.CollectNearbyPartial(
            position, 1f, inventory.InsertUpTo));
        Assert.Equal(revision + 1, inventory.Revision);

        // A restored drop has the same portable stack and remains collectible
        // when the player opens a slot; the saved remainder isn't duplicated.
        var restored = new DroppedBlockRuntime(
            LoadedWorld(), blocks, restore: runtime.CaptureState());
        Assert.Equal(9, Assert.Single(restored.ActiveBlocks).Stack.Quantity);
        Assert.True(inventory.ClickSlot(PlayerInventory.BackpackSlots));
        Assert.Equal(1, restored.CollectNearbyPartial(
            position, 1f, inventory.InsertUpTo));
        Assert.Empty(restored.ActiveBlocks);
        Assert.Equal(9, inventory.SelectedStack!.Quantity);
        Assert.Equal(entry, inventory.SelectedStack.Entry);
        Assert.Equal(64, inventory.Cursor!.Quantity);
    }

    [Fact]
    public void PartialPickupUsesStableDropIdOrderAndPreservesUnacceptedRemainders()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var runtime = new DroppedBlockRuntime(LoadedWorld(), blocks);
        var entry = InventoryEntry.FromItem("asteria:pebble");
        var position = new Vector3(3f, 4f, 5f);
        var oldest = runtime.Spawn(new InventoryStack(entry, 5), position);
        var newest = runtime.Spawn(new InventoryStack(entry, 7),
            position + new Vector3(0.2f, 0, 0));
        runtime.Advance(0.5, 0);
        var capacity = 6;
        var seen = new List<int>();
        Assert.Equal(2, runtime.CollectNearbyPartial(position, 1f, stack =>
        {
            seen.Add(stack.Quantity);
            var accepted = Math.Min(stack.Quantity, capacity);
            capacity -= accepted;
            return accepted;
        }));
        Assert.Equal(new[] { 5, 7 }, seen);
        var remainder = Assert.Single(runtime.ActiveBlocks);
        Assert.Equal(newest, remainder.Id);
        Assert.NotEqual(oldest, remainder.Id);
        Assert.Equal(6, remainder.Stack.Quantity);
        Assert.Equal(0, runtime.CollectNearbyPartial(position, 1f, _ => 0));
    }

    [Fact]
    public void AcceptedPickupIsCommittedBeforeLaterRecipientFailure()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var runtime = new DroppedBlockRuntime(LoadedWorld(), blocks);
        var entry = InventoryEntry.FromItem("asteria:pebble");
        var position = new Vector3(3f, 4f, 5f);
        var first = runtime.Spawn(new InventoryStack(entry, 4), position);
        var second = runtime.Spawn(new InventoryStack(entry, 5),
            position + new Vector3(0.2f, 0, 0));
        runtime.Advance(0.5, 0);

        var accepted = 0;
        Assert.Throws<InvalidOperationException>(() =>
            runtime.CollectNearbyPartial(position, 1f, stack =>
            {
                if (stack.Quantity == 5)
                    throw new InvalidOperationException("Recipient rejected pickup.");
                accepted += stack.Quantity;
                return stack.Quantity;
            }));
        Assert.Equal(4, accepted);
        var remainder = Assert.Single(runtime.ActiveBlocks);
        Assert.Equal(second, remainder.Id);
        Assert.NotEqual(first, remainder.Id);
        Assert.Equal(5, remainder.Stack.Quantity);
    }

    [Fact]
    public void PartialPickupGracePeriodAndInvalidQuantityCannotConsumeDrops()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var runtime = new DroppedBlockRuntime(LoadedWorld(), blocks);
        var entry = InventoryEntry.FromItem("asteria:pebble");
        runtime.Spawn(new InventoryStack(entry, 4), new Vector3(3f, 4f, 5f));
        var callbacks = 0;
        Assert.Equal(0, runtime.CollectNearbyPartial(
            new Vector3(3f, 4f, 5f), 1f, _ =>
            {
                callbacks++;
                return 4;
            }));
        Assert.Equal(0, callbacks);
        runtime.Advance(0.5, 0);
        Assert.Throws<InvalidOperationException>(() =>
            runtime.CollectNearbyPartial(new Vector3(3f, 4f, 5f),
                1f, _ => 5));
        Assert.Equal(4, Assert.Single(runtime.ActiveBlocks).Stack.Quantity);
    }

    [Fact]
    public void DroppedBlockFallsSettlesAndWakesWhenSupportChanges()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var world = LoadedWorld();
        var support =
            new WorldVoxelCoord(2, 0, 2);

        Assert.True(
            world.SetBlockAt(
                support,
                stone,
                out _));

        var runtime =
            new DroppedBlockRuntime(
                world,
                blocks);
        runtime.Spawn(
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone)),
            new Vector3(
                2.5f,
                3.5f,
                2.5f));

        for (var frame = 0;
             frame < 240;
             frame++)
        {
            runtime.Advance(
                1.0 / 60.0,
                18.0);

            if (runtime.ActiveBlocks
                .Single()
                .IsSettled)
            {
                break;
            }
        }

        var settled =
            Assert.Single(
                runtime.ActiveBlocks);

        Assert.True(settled.IsSettled);
        Assert.InRange(
            settled.Position.Y,
            1.17f,
            1.30f);

        Assert.True(
            world.SetBlockAt(
                support,
                BlockRuntimeId.Air,
                out _));
        runtime.NotifyVoxelEdit(support);

        var awakened =
            Assert.Single(
                runtime.ActiveBlocks);

        Assert.False(awakened.IsSettled);

        var previousY =
            awakened.Position.Y;
        runtime.Advance(
            1.0 / 60.0,
            18.0);

        Assert.True(
            runtime.ActiveBlocks
                .Single()
                .Position.Y <
            previousY);
    }

    [Fact]
    public void OverlappingDropsSeparateDeterministically()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                LoadedWorld(),
                blocks);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));
        var center =
            new Vector3(
                4.5f,
                4.5f,
                4.5f);

        var first =
            runtime.Spawn(
                block,
                center);
        var second =
            runtime.Spawn(
                block,
                center);

        runtime.Advance(
            1.0 / 60.0,
            gravityStrength: 0.0);

        var states =
            runtime.ActiveBlocks
                .ToDictionary(
                    state => state.Id);

        Assert.False(
            DroppedBlockRuntime.BoundsAt(
                    states[first].Position)
                .Intersects(
                    DroppedBlockRuntime.BoundsAt(
                        states[second].Position)));
        Assert.True(
            states[first].Position.X <
            states[second].Position.X);
    }

    [Fact]
    public void ContactResolutionDoesNotPushDropIntoSolidVoxel()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var world =
            LoadedWorld();

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(
                    3,
                    4,
                    4),
                stone,
                out _));

        var runtime =
            new DroppedBlockRuntime(
                world,
                blocks);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));

        runtime.Spawn(
            block,
            new Vector3(
                4.20f,
                4.5f,
                4.5f));
        runtime.Spawn(
            block,
            new Vector3(
                4.34f,
                4.5f,
                4.5f));

        runtime.Advance(
            1.0 / 60.0,
            gravityStrength: 0.0);

        foreach (var state in
                 runtime.ActiveBlocks)
        {
            Assert.True(
                VoxelWorldCollision
                    .QueryDetailed(
                        world,
                        blocks,
                        DroppedBlockRuntime.BoundsAt(
                            state.Position))
                    .IsClear);
        }
    }

    [Fact]
    public void CapacityEvictsOldestDropDeterministically()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                LoadedWorld(),
                blocks,
                maximumActive: 2);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));

        var first =
            runtime.Spawn(
                block,
                new Vector3(1.5f, 1.5f, 1.5f));
        var second =
            runtime.Spawn(
                block,
                new Vector3(2.5f, 1.5f, 1.5f));
        var third =
            runtime.Spawn(
                block,
                new Vector3(3.5f, 1.5f, 1.5f));

        var ids =
            runtime.ActiveBlocks
                .Select(state => state.Id)
                .ToArray();

        Assert.Equal(2, ids.Length);
        Assert.DoesNotContain(first, ids);
        Assert.Equal(
            [second, third],
            ids);
    }

    [Fact]
    public void LifetimeExpiresDropsEvenWhenWorldIsUnloaded()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                new VoxelWorld(),
                blocks,
                lifetimeSeconds: 0.1);

        runtime.Spawn(
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone)),
            new Vector3(
                20.5f,
                20.5f,
                20.5f));

        var result =
            runtime.Advance(
                0.2,
                18.0);

        Assert.Equal(1, result.Expired);
        Assert.Equal(0, runtime.ActiveCount);
    }

    [Fact]
    public void AdvanceCanExpireMultipleDropsInOnePass()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var runtime =
            new DroppedBlockRuntime(
                new VoxelWorld(),
                blocks,
                lifetimeSeconds: 0.1);
        var block =
            BlockStateSnapshot.FromCell(
                new VoxelCell(stone));

        runtime.Spawn(
            block,
            new Vector3(
                20.5f,
                20.5f,
                20.5f));
        runtime.Spawn(
            block,
            new Vector3(
                21.5f,
                20.5f,
                20.5f));

        var result =
            runtime.Advance(
                0.2,
                18.0);

        Assert.Equal(
            2,
            result.Expired);
        Assert.Equal(
            0,
            runtime.ActiveCount);
    }

    [Fact]
    public void ItemEmbeddedInMultipleSolidBlocksEscapesToFirstAirWithoutGravity()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
        ]);
        var world = LoadedWorld();
        var stone = blocks.GetId("asteria:stone");
        for (var y = 2; y <= 5; y++)
            Assert.True(world.SetBlockAt(
                new WorldVoxelCoord(4, y, 4), stone, out _));

        var runtime = new DroppedBlockRuntime(world, blocks);
        var dropId = runtime.Spawn(
            new InventoryStack(InventoryEntry.FromItem("asteria:pebble")),
            new Vector3(4.5f, 2.5f, 4.5f));

        var advance = runtime.Advance(1.0 / 60.0, gravityStrength: 0);
        var drop = Assert.Single(runtime.ActiveBlocks);

        Assert.Equal(dropId, drop.Id);
        Assert.Equal(1, advance.Moved);
        Assert.Equal(4.5f, drop.Position.X);
        Assert.Equal(4.5f, drop.Position.Z);
        Assert.InRange(drop.Position.Y, 6.18f, 6.19f);
        Assert.False(drop.IsSettled);
        Assert.True(VoxelWorldCollision.QueryDetailed(
            world, blocks, DroppedBlockRuntime.BoundsAt(drop.Position)).IsClear);
    }

    [Fact]
    public void PlacingBlockAroundSettledDropPushesItAboveNewBlock()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
        ]);
        var world = LoadedWorld();
        var stone = blocks.GetId("asteria:stone");
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(3, 0, 3), stone, out _));
        var runtime = new DroppedBlockRuntime(world, blocks);
        runtime.Spawn(
            BlockStateSnapshot.FromCell(new VoxelCell(stone)),
            new Vector3(3.5f, 3.5f, 3.5f));

        for (var frame = 0; frame < 240; frame++)
        {
            runtime.Advance(1.0 / 60.0, 18.0);
            if (Assert.Single(runtime.ActiveBlocks).IsSettled)
                break;
        }
        Assert.True(Assert.Single(runtime.ActiveBlocks).IsSettled);

        var addedBlock = new WorldVoxelCoord(3, 1, 3);
        Assert.True(world.SetBlockAt(addedBlock, stone, out _));
        runtime.NotifyVoxelEdit(addedBlock);
        var result = runtime.Advance(1.0 / 60.0, 18.0);
        var escaped = Assert.Single(runtime.ActiveBlocks);

        Assert.Equal(1, result.Moved);
        Assert.InRange(escaped.Position.Y, 2.18f, 2.19f);
        Assert.True(VoxelWorldCollision.QueryDetailed(
            world, blocks, DroppedBlockRuntime.BoundsAt(escaped.Position)).IsClear);
    }

    [Fact]
    public void EmbeddedDropWaitsUntilAboveChunkIsLoaded()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
        ]);
        var world = LoadedWorld();
        var stone = blocks.GetId("asteria:stone");
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(5, 15, 5), stone, out _));

        var runtime = new DroppedBlockRuntime(world, blocks);
        runtime.Spawn(
            BlockStateSnapshot.FromCell(new VoxelCell(stone)),
            new Vector3(5.5f, 15.5f, 5.5f));

        runtime.Advance(1.0 / 60.0, 18.0);
        var pending = Assert.Single(runtime.ActiveBlocks);
        Assert.Equal(15.5f, pending.Position.Y);
        Assert.False(pending.IsSettled);

        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        runtime.Advance(1.0 / 60.0, 18.0);
        var escaped = Assert.Single(runtime.ActiveBlocks);

        Assert.InRange(escaped.Position.Y, 16.18f, 16.19f);
        Assert.True(VoxelWorldCollision.QueryDetailed(
            world, blocks, DroppedBlockRuntime.BoundsAt(escaped.Position)).IsClear);
    }

    [Fact]
    public void NonCollidableBlocksDoNotEjectDrops()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:plant", isCollidable: false),
        ]);
        var world = LoadedWorld();
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(4, 4, 4),
            blocks.GetId("asteria:plant"), out _));

        var runtime = new DroppedBlockRuntime(world, blocks);
        var start = new Vector3(4.5f, 4.5f, 4.5f);
        runtime.Spawn(
            new InventoryStack(InventoryEntry.FromItem("asteria:pebble")),
            start);

        var result = runtime.Advance(1.0 / 60.0, gravityStrength: 0);

        Assert.Equal(0, result.Moved);
        Assert.Equal(start, Assert.Single(runtime.ActiveBlocks).Position);
    }

    private static VoxelWorld LoadedWorld()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        return world;
    }
}
