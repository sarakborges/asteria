using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class InteractiveLayerMiningTests
{
    private sealed record Fixture(
        BlockRegistry Blocks, VoxelWorld World, VoxelMutationRuntime Mutations,
        BlockInteractionRuntime Interactions, BlockMiningRuntime Mining,
        DroppedBlockRuntime Drops, ToolGameplayRuntime Tools);

    private static readonly WorldVoxelCoord Position = new(3, 3, 3);
    private static readonly VoxelWorldHit TopHit = new(Position, 0, 1, 0);
    private static readonly VoxelWorldHit FrontHit = new(Position, 0, 0, 1);

    private static Fixture Setup(int dropCapacity = 2048)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone", mining: new BlockMiningDefinition(
                hardness: 2, requiredTools: ["pickaxe"])),
            new BlockDefinition("asteria:protected", mining: new BlockMiningDefinition(
                unbreakable: true))
        ]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:moss","texture":"textures/layers/moss.png",
             "scope":"face","mining":{"hardness":0.2,"preferredTools":["shears"]}}
            """,
            """
            {"id":"asteria:iron_ore","texture":"textures/layers/iron.png",
             "scope":"voxel","mining":{"hardness":0.5,"requiredTools":["pickaxe"]},
             "drops":[{"kind":"item","id":"asteria:raw_iron","quantity":2}]}
            """,
            """
            {"id":"asteria:lichen","texture":"textures/layers/lichen.png",
             "mining":{"hardness":0.1}}
            """
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks,
            maximumActive: dropCapacity);
        var tools = PackContentRegistry<ToolDefinition>.FromJson([
            """
            {"id":"asteria:pickaxe","category":"tools","icon":"textures/pickaxe.png",
             "leftBehavior":"asteria:mine","rightBehavior":"asteria:none",
             "mining":{"category":"pickaxe","speed":2}}
            """,
            """
            {"id":"asteria:shears","category":"tools","icon":"textures/shears.png",
             "leftBehavior":"asteria:mine","rightBehavior":"asteria:none",
             "mining":{"category":"shears","speed":2}}
            """
        ], ToolDefinition.Parse, item => item.Id);
        var toolRuntime = new ToolGameplayRuntime(world, blocks, mutations, tools);
        var interactionRuntime = new BlockInteractionRuntime(
            world, blocks, mutations, drops, layers);
        var miningRuntime = new BlockMiningRuntime(
            world, blocks, toolRuntime, interactionRuntime, layers);
        return new Fixture(blocks, world, mutations, interactionRuntime,
            miningRuntime, drops, toolRuntime);
    }

    private static InventoryStack Pickaxe() =>
        new(InventoryEntry.FromTool("asteria:pickaxe"));

    private static InventoryStack Shears() =>
        new(InventoryEntry.FromTool("asteria:shears"));

    private static void PutStone(Fixture f, BlockSurfaceState surface)
    {
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:stone"), out _));
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position, surface, out _));
    }

    [Fact]
    public void MiningRemovesOnlyTopFaceLayerAndThenMinesVoxelWideOre()
    {
        var f = Setup();
        PutStone(f, new BlockSurfaceState("asteria:red", [
            new AttachedBlockLayer(BlockFace.Top, "asteria:iron_ore"),
            new AttachedBlockLayer(BlockFace.Front, "asteria:iron_ore"),
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss")
        ]));

        Assert.False(f.Mining.Advance(TopHit, Shears(), 0, 10, PlayerGameMode.Survival));
        Assert.Equal(0.5f, f.Mining.Progress!.Value);
        Assert.True(f.Mining.Advance(TopHit, Shears(), 0, 10, PlayerGameMode.Survival));
        Assert.Equal(f.Blocks.GetId("asteria:stone"), f.World.GetCellOrEmpty(Position).Block);
        Assert.Equal("asteria:red", f.World.GetBlockSurfaceStateOrEmpty(Position).DyeId);
        Assert.Equal(2, f.World.GetBlockSurfaceStateOrEmpty(Position).Layers.Count);
        Assert.Equal("asteria:moss", Assert.Single(f.Drops.ActiveBlocks).Stack.Id);

        // A mismatched tool cannot mine the ore, even though it worked for moss.
        Assert.False(f.Mining.Advance(TopHit, Shears(), 0, 300, PlayerGameMode.Survival));
        Assert.Null(f.Mining.Progress);
        Assert.False(f.Mining.Advance(TopHit, Pickaxe(), 0, 49, PlayerGameMode.Survival));
        Assert.True(f.Mining.Advance(TopHit, Pickaxe(), 0, 1, PlayerGameMode.Survival));
        Assert.Empty(f.World.GetBlockSurfaceStateOrEmpty(Position).Layers);
        Assert.Equal(f.Blocks.GetId("asteria:stone"), f.World.GetCellOrEmpty(Position).Block);
        var oreDrop = Assert.Single(f.Drops.ActiveBlocks.Where(
            state => state.Stack.Id == "asteria:raw_iron"));
        Assert.Equal(2, oreDrop.Stack.Quantity);
        Assert.Equal(InventoryEntryKind.Item, oreDrop.Stack.Entry.Kind);

        // Only now is the host's own pickaxe requirement/hardness used.
        Assert.False(f.Mining.Advance(TopHit, Pickaxe(), 0, 100, PlayerGameMode.Survival));
        Assert.True(f.Mining.Advance(TopHit, Pickaxe(), 0, 100, PlayerGameMode.Survival));
        Assert.True(f.World.GetCellOrEmpty(Position).IsEmpty);
    }


    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 0)]
    public void OreDropSpawnsBeyondFaceAndCanBeCollectedOnlyOnce(
        int nx, int ny, int nz)
    {
        var f = Setup();
        var face = (nx, ny, nz) switch
        {
            (0, 1, 0) => BlockFace.Top,
            (0, 0, 1) => BlockFace.Front,
            (-1, 0, 0) => BlockFace.Left,
            _ => throw new ArgumentOutOfRangeException()
        };
        var hit = new VoxelWorldHit(Position, nx, ny, nz);
        PutStone(f, new BlockSurfaceState(null, [
            new AttachedBlockLayer(face, "asteria:iron_ore"),
            new AttachedBlockLayer(BlockFace.Bottom, "asteria:iron_ore")
        ]));
        Assert.True(f.Interactions.Break(hit).Accepted);
        Assert.Equal(f.Blocks.GetId("asteria:stone"),
            f.World.GetCellOrEmpty(Position).Block);
        Assert.Empty(f.World.GetBlockSurfaceStateOrEmpty(Position).Layers);

        var drop = Assert.Single(f.Drops.ActiveBlocks);
        Assert.Equal("asteria:raw_iron", drop.Stack.Id);
        Assert.Equal(InventoryEntryKind.Item, drop.Stack.Entry.Kind);
        Assert.Equal(2, drop.Stack.Quantity);
        var center = new System.Numerics.Vector3(
            Position.X + 0.5f, Position.Y + 0.5f, Position.Z + 0.5f);
        var normal = new System.Numerics.Vector3(nx, ny, nz);
        Assert.True(System.Numerics.Vector3.Dot(drop.Position - center, normal) >=
            0.5f + DroppedBlockRuntime.HalfExtent);

        // The normal pickup delay prevents immediate re-collection.
        var inventory = new PlayerInventory();
        Assert.Equal(0, f.Drops.CollectNearby(drop.Position, 1f, inventory.TryInsert));
        f.Drops.Advance(0.5, 0);
        Assert.Equal(1, f.Drops.CollectNearby(drop.Position, 1f, inventory.TryInsert));
        Assert.Equal(0, f.Drops.ActiveCount);
        Assert.Equal(0, f.Drops.CollectNearby(drop.Position, 1f, inventory.TryInsert));
        Assert.Contains(Enumerable.Range(0, PlayerInventory.TotalSlots)
                .Select(inventory.SlotAt),
            stack => stack?.Id == "asteria:raw_iron" && stack.Quantity == 2);
    }

    [Fact]
    public void OtherFacesAreIndependentAndTargetChangeResetsProgress()
    {
        var f = Setup();
        PutStone(f, new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss"),
            new AttachedBlockLayer(BlockFace.Front, "asteria:lichen")
        ]));
        Assert.False(f.Mining.Advance(TopHit, null, 0, 20, PlayerGameMode.Survival));
        Assert.Equal(0.5f, f.Mining.Progress!.Value);
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Top, "asteria:moss"),
                new AttachedBlockLayer(BlockFace.Front, "asteria:lichen"),
                new AttachedBlockLayer(BlockFace.Top, "asteria:lichen")
            ]), out _));
        Assert.False(f.Mining.Advance(TopHit, null, 0, 5, PlayerGameMode.Survival));
        Assert.Equal(0.25f, f.Mining.Progress!.Value);

        Assert.True(f.Interactions.Break(FrontHit).Accepted);
        var surface = f.World.GetBlockSurfaceStateOrEmpty(Position);
        Assert.True(surface.TryGetTop(BlockFace.Top, out var top));
        Assert.Equal("asteria:lichen", top.LayerId);
        Assert.False(surface.TryGetTop(BlockFace.Front, out _));
    }

    [Fact]
    public void CreativeRemovesLayerWithoutDropsAndPreservesHost()
    {
        var f = Setup();
        PutStone(f, new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Top, "asteria:moss")
        ]));
        Assert.True(f.Interactions.Break(TopHit, BlockBreakLootPolicy.Suppress).Accepted);
        Assert.Empty(f.World.GetBlockSurfaceStateOrEmpty(Position).Layers);
        Assert.Equal(0, f.Drops.ActiveCount);
        Assert.Equal(f.Blocks.GetId("asteria:stone"),
            f.World.GetCellOrEmpty(Position).Block);
    }

    [Fact]
    public void FullDropPoolPreservesOreAndPreviouslyDroppedItems()
    {
        var f = Setup(dropCapacity: 1);
        PutStone(f, new BlockSurfaceState(null, [
            new AttachedBlockLayer(BlockFace.Top, "asteria:iron_ore")
        ]));
        var existing = f.Drops.Spawn(
            new InventoryStack(InventoryEntry.FromItem("asteria:existing")),
            new System.Numerics.Vector3(1, 4, 1));

        Assert.False(f.Interactions.Break(TopHit).Accepted);
        Assert.Equal(existing, Assert.Single(f.Drops.ActiveBlocks).Id);
        Assert.Equal(f.Blocks.GetId("asteria:stone"),
            f.World.GetCellOrEmpty(Position).Block);
        Assert.True(f.World.GetBlockSurfaceStateOrEmpty(Position)
            .TryGetTop(BlockFace.Top, out var top));
        Assert.Equal("asteria:iron_ore", top.LayerId);
    }

    [Fact]
    public void UnbreakableHostCannotLoseLayerThroughMining()
    {
        var f = Setup();
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:protected"), out _));
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Top, "asteria:moss")
            ]), out _));
        Assert.False(f.Mining.Advance(TopHit, Shears(), 0, 100, PlayerGameMode.Survival));
        Assert.False(f.Interactions.Break(TopHit).Accepted);
        Assert.Single(f.World.GetBlockSurfaceStateOrEmpty(Position).Layers);
    }
}
