using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockInteractionRuntimeTests
{
    [Fact]
    public void BreakingBlockSpawnsPortableSelfDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:stone"));
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var position =
            new WorldVoxelCoord(2, 2, 2);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                stone,
                out _));

        var decision =
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.True(decision.Accepted);
        Assert.True(
            fixture.World
                .GetCellOrEmpty(position)
                .IsEmpty);

        var drop =
            Assert.Single(
                fixture.Dropped.ActiveBlocks);

        Assert.Equal(
            stone,
            drop.Block.Cell.Block);
        Assert.Equal(
            new System.Numerics.Vector3(
                2.5f,
                2.5f,
                2.5f),
            drop.Position);
    }

    [Fact]
    public void CreativeBreakSuppressesLootButStillRemovesVoxel()
    {
        var fixture = CreateFixture(new BlockDefinition("asteria:stone"));
        var block = fixture.Blocks.GetId("asteria:stone");
        var pos = new WorldVoxelCoord(2, 2, 2);
        Assert.True(fixture.Mutations.SetBlockAt(pos, block, out _));

        var decision = fixture.Interactions.Break(
            new VoxelWorldHit(pos, 0, 0, 0),
            PlayerGameMode.Creative.BreakLootPolicy());

        Assert.True(decision.Accepted);
        Assert.True(fixture.World.GetCellOrEmpty(pos).IsEmpty);
        Assert.Equal(0, fixture.Dropped.ActiveCount);
        Assert.Equal(BlockBreakLootPolicy.DropSelf,
            PlayerGameMode.Survival.BreakLootPolicy());
    }

    [Fact]
    public void UnbreakableBlockRejectsBreakWithoutMutationOrDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:sphere_shell",
                    mining:
                        new BlockMiningDefinition(
                            unbreakable: true),
                    dropsSelf: false));
        var shell =
            fixture.Blocks.GetId(
                "asteria:sphere_shell");
        var position =
            new WorldVoxelCoord(2, 2, 2);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                shell,
                out _));

        var decision =
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.False(
            decision.Accepted);
        Assert.Equal(
            BlockBreakRejection.Unbreakable,
            decision.Rejection);
        Assert.Equal(
            shell,
            fixture.World
                .GetCellOrEmpty(
                    position)
                .Block);
        Assert.Equal(
            0,
            fixture.Dropped.ActiveCount);
    }

    [Fact]
    public void DropsSelfFalseBreaksWithoutSpawningDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:fixture",
                    dropsSelf: false));
        var block =
            fixture.Blocks.GetId(
                "asteria:fixture");
        var position =
            new WorldVoxelCoord(2, 2, 2);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                block,
                out _));

        var decision =
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0));

        Assert.True(decision.Accepted);
        Assert.Equal(
            0,
            fixture.Dropped.ActiveCount);
    }

    [Fact]
    public void BreakingSculptedBlockCopiesMaskIntoDrop()
    {
        var fixture =
            CreateFixture(
                new BlockDefinition(
                    "asteria:sculpted",
                    tags: ["fragmentable"]));
        var block =
            fixture.Blocks.GetId(
                "asteria:sculpted");
        var position =
            new WorldVoxelCoord(2, 2, 2);
        var mask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thin,
                occupied: false);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                block,
                out _));

        var address =
            VoxelCoordinates.FromWorld(
                position.X,
                position.Y,
                position.Z);
        Assert.True(
            fixture.World
                .GetChunk(address.Chunk)
                .SetMicroblockMask(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z,
                    mask));

        Assert.True(
            fixture.Interactions.Break(
                new VoxelWorldHit(
                    position,
                    0,
                    0,
                    0))
                .Accepted);

        Assert.Equal(
            mask,
            Assert.Single(
                    fixture.Dropped.ActiveBlocks)
                .Block
                .MicroblockMask);
    }

    [Fact]
    public void GroundObjectPickupMovesOneItemToInventoryWithoutSpawningDrop()
    {
        var definition = new BlockDefinition(
            "asteria:pebble", interaction: BlockInteractionKind.Pickup,
            pickupItemId: "asteria:pebble");
        var fixture = CreateFixture(definition);
        var position = new WorldVoxelCoord(2, 2, 2);
        Assert.True(fixture.Mutations.SetBlockAt(
            position, fixture.Blocks.GetId(definition.Id), out _));
        var inventory = new PlayerInventory();
        var item = InventoryEntry.FromItem("asteria:pebble");
        var hit = new VoxelWorldHit(position, 0, 0, 0);

        Assert.Equal(BlockBreakRejection.PickupOnly,
            fixture.Interactions.Break(hit).Rejection);
        Assert.Equal(BlockPickupResult.Collected,
            fixture.Interactions.Pickup(hit, inventory, item));
        Assert.True(fixture.World.GetCellOrEmpty(position).IsEmpty);
        Assert.Equal(0, fixture.Dropped.ActiveCount);
        Assert.Equal(InventoryEntryKind.Item, inventory.SelectedStack!.Kind);
        Assert.Equal("asteria:pebble", inventory.SelectedStack.Id);
        Assert.Equal(1, inventory.SelectedStack.Quantity);
        Assert.Equal(BlockPickupResult.UnloadedOrEmpty,
            fixture.Interactions.Pickup(hit, inventory, item));
        Assert.Equal(1, inventory.SelectedStack.Quantity);
    }

    [Fact]
    public void GroundObjectPickupPreservesVoxelWhenInventoryFullOrItemIsWrong()
    {
        var definition = new BlockDefinition(
            "asteria:stick", interaction: BlockInteractionKind.Pickup);
        var fixture = CreateFixture(definition);
        var position = new WorldVoxelCoord(2, 2, 2);
        var block = fixture.Blocks.GetId(definition.Id);
        Assert.True(fixture.Mutations.SetBlockAt(position, block, out _));
        var hit = new VoxelWorldHit(position, 0, 0, 0);
        var inventory = new PlayerInventory();
        Assert.Equal(BlockPickupResult.WrongItem,
            fixture.Interactions.Pickup(
                hit, inventory, InventoryEntry.FromItem("asteria:pebble")));
        for (var index = 0; index < PlayerInventory.TotalSlots; index++)
        {
            Assert.True(inventory.TryInsert(
                new InventoryStack(InventoryEntry.FromItem(
                    $"asteria:filled_{index}"), 64)));
        }

        Assert.Equal(BlockPickupResult.InventoryFull,
            fixture.Interactions.Pickup(
                hit, inventory, InventoryEntry.FromItem("asteria:stick")));
        Assert.Equal(block, fixture.World.GetCellOrEmpty(position).Block);
        Assert.Equal(0, fixture.Dropped.ActiveCount);
    }

    [Fact]
    public void NormalBlockCannotBePickedUp()
    {
        var fixture = CreateFixture(new BlockDefinition("asteria:stone"));
        var position = new WorldVoxelCoord(2, 2, 2);
        Assert.True(fixture.Mutations.SetBlockAt(
            position, fixture.Blocks.GetId("asteria:stone"), out _));
        Assert.Equal(BlockPickupResult.NotPickupable,
            fixture.Interactions.Pickup(
                new VoxelWorldHit(position, 0, 0, 0),
                new PlayerInventory(), InventoryEntry.FromItem("asteria:stone")));
        Assert.Equal(0, fixture.Dropped.ActiveCount);
    }

    private static Fixture CreateFixture(
        BlockDefinition definition)
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var blocks =
            new BlockRegistry(
            [
                definition,
            ]);
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var interactions =
            new BlockInteractionRuntime(
                world,
                blocks,
                mutations,
                dropped);

        return new Fixture(
            world,
            blocks,
            mutations,
            dropped,
            interactions);
    }

    private sealed record Fixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Dropped,
        BlockInteractionRuntime Interactions);
}
