using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockLootTableTests
{
    private static readonly WorldVoxelCoord Position = new(2, 3, 4);
    private static readonly VoxelWorldHit Hit = new(Position, 0, 1, 0);

    private sealed record Fixture(
        BlockRegistry Blocks, VoxelWorld World, VoxelMutationRuntime Mutations,
        BlockInteractionRuntime Interactions, DroppedBlockRuntime Drops);

    private static Fixture Setup(string table, int capacity = 64)
    {
        var blocks = BlockRegistry.FromJson([
            $$"""
            {"id":"asteria:stone","mining":{"hardness":1},
             "lootTable":{{table}}}
            """,
            """{"id":"asteria:gravel","mining":{"hardness":1}}"""
        ]);
        var items = PackContentRegistry<ItemDefinition>.FromJson([
            """
            {"id":"asteria:raw_iron","category":"materials",
             "icon":"textures/items/iron.png","maxStackSize":16}
            """
        ], ItemDefinition.Parse, x => x.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, x => x.Id);
        var catalog = new InventoryContentCatalog(blocks, items, tools);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks, maximumActive: capacity);
        return new Fixture(blocks, world, mutations,
            new BlockInteractionRuntime(world, blocks, mutations, drops,
                inventoryCatalog: catalog), drops);
    }

    private static void Place(Fixture f) =>
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:stone"), out _));

    private static Fixture SetupLayer(int capacity)
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:iron_ore","texture":"textures/layers/iron.png",
             "mining":{"hardness":0.5},
             "drops":[{"kind":"item","id":"asteria:raw_iron","quantity":33}]}
            """
        ]);
        var items = PackContentRegistry<ItemDefinition>.FromJson([
            """
            {"id":"asteria:raw_iron","category":"materials",
             "icon":"textures/items/iron.png","maxStackSize":16}
            """
        ], ItemDefinition.Parse, x => x.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, x => x.Id);
        var catalog = new InventoryContentCatalog(blocks, items, tools, layers);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks, maximumActive: capacity);
        return new Fixture(blocks, world, mutations,
            new BlockInteractionRuntime(world, blocks, mutations, drops,
                layers, catalog), drops);
    }

    private static void PlaceOre(Fixture fixture)
    {
        Place(fixture);
        Assert.True(fixture.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Top, "asteria:iron_ore")
            ]), out _));
    }

    [Fact]
    public void LayerLootUsesItemStackLimitAndRejectsCapacityBeforeRemovingOre()
    {
        var f = SetupLayer(capacity: 3);
        PlaceOre(f);
        Assert.True(f.Interactions.Break(Hit).Accepted);
        Assert.Equal([16, 16, 1], f.Drops.ActiveBlocks
            .Select(x => x.Stack.Quantity).ToArray());
        Assert.Empty(f.World.GetBlockSurfaceStateOrEmpty(Position).Layers);
        Assert.Equal(f.Blocks.GetId("asteria:stone"),
            f.World.GetCellOrEmpty(Position).Block);

        var full = SetupLayer(capacity: 2);
        PlaceOre(full);
        Assert.False(full.Interactions.Break(Hit).Accepted);
        Assert.Equal(0, full.Drops.ActiveCount);
        Assert.True(full.World.GetBlockSurfaceStateOrEmpty(Position)
            .TryGetTop(BlockFace.Top, out _));
    }

    [Fact]
    public void AuthoredEntriesOverrideSelfDropAndSplitToActualStackLimits()
    {
        var f = Setup("""
            [{"item":"asteria:raw_iron","quantity":33},
             {"item":"asteria:gravel","quantity":2}]
            """);
        Place(f);
        Assert.True(f.Interactions.Break(Hit, worldTick: 23).Accepted);
        Assert.True(f.World.GetCellOrEmpty(Position).IsEmpty);

        var drops = f.Drops.ActiveBlocks.ToArray();
        Assert.Equal(4, drops.Length);
        Assert.Equal([16, 16, 1],
            drops.Where(x => x.Stack.Id == "asteria:raw_iron")
                .Select(x => x.Stack.Quantity).ToArray());
        var gravel = Assert.Single(drops.Where(x => x.Stack.Id == "asteria:gravel"));
        Assert.Equal(2, gravel.Stack.Quantity);
        Assert.Equal(InventoryEntryKind.Block, gravel.Stack.Kind);
        Assert.DoesNotContain(drops, x => x.Stack.Id == "asteria:stone");
        Assert.False(f.Interactions.Break(Hit, worldTick: 23).Accepted);
        Assert.Equal(4, f.Drops.ActiveCount);
    }

    [Fact]
    public void ExplicitEmptyTableAndZeroChanceSuppressSelfDrops()
    {
        var empty = Setup("[]");
        Place(empty);
        Assert.True(empty.Interactions.Break(Hit).Accepted);
        Assert.Equal(0, empty.Drops.ActiveCount);

        var zero = Setup("""[{"item":"asteria:raw_iron","chance":0}]""");
        Place(zero);
        Assert.True(zero.Interactions.Break(Hit, worldTick: 99).Accepted);
        Assert.Equal(0, zero.Drops.ActiveCount);
    }

    [Fact]
    public void CreativeRemovalDoesNotRollOrSpawnAuthoredLoot()
    {
        var f = Setup("""[{"item":"asteria:raw_iron","quantity":5}]""");
        Place(f);
        Assert.True(f.Interactions.Break(Hit, BlockBreakLootPolicy.Suppress).Accepted);
        Assert.True(f.World.GetCellOrEmpty(Position).IsEmpty);
        Assert.Equal(0, f.Drops.ActiveCount);
    }

    [Fact]
    public void SelfEntryRetainsPortableVoxelState()
    {
        var f = Setup("""[{"item":"asteria:stone"}]""");
        var state = new VoxelCell(f.Blocks.GetId("asteria:stone"), state: 7);
        Assert.True(f.Mutations.SetCellAt(Position, state, out _));
        Assert.True(f.Interactions.Break(Hit).Accepted);
        var drop = Assert.Single(f.Drops.ActiveBlocks);
        Assert.Equal(InventoryEntryKind.Block, drop.Stack.Kind);
        Assert.Equal(state, drop.Block!.Cell);
    }

    [Fact]
    public void InsufficientDropCapacityRejectsWithoutDestroyingBlock()
    {
        var f = Setup("""[{"item":"asteria:raw_iron","quantity":33}]""",
            capacity: 2);
        Place(f);
        Assert.False(f.Interactions.Break(Hit).Accepted);
        Assert.False(f.World.GetCellOrEmpty(Position).IsEmpty);
        Assert.Equal(0, f.Drops.ActiveCount);
    }

    [Fact]
    public void AuthoredValidationRejectsInvalidChancesQuantitiesAndUnknownContent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockLootTable.Parse(System.Text.Json.JsonDocument.Parse(
                """[{"item":"asteria:raw_iron","chance":1.1}]""").RootElement));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockLootTable.Parse(System.Text.Json.JsonDocument.Parse(
                """[{"item":"asteria:raw_iron","quantity":0}]""").RootElement));
        Assert.Throws<FormatException>(() =>
            BlockDefinitionJson.Parse(
                """{"id":"asteria:stone","lootTable":{}}"""));
        Assert.Throws<InvalidOperationException>(() =>
            Setup("""[{"item":"asteria:unknown"}]"""));
    }

    [Fact]
    public void RandomChanceIsRepeatableForSameVoxelAndWorldTick()
    {
        const string table = """
            [{"item":"asteria:raw_iron","chance":0.5}]
            """;
        var first = Setup(table);
        var second = Setup(table);
        Place(first);
        Place(second);
        Assert.True(first.Interactions.Break(Hit, worldTick: 824).Accepted);
        Assert.True(second.Interactions.Break(Hit, worldTick: 824).Accepted);
        Assert.Equal(first.Drops.ActiveBlocks.Select(x => x.Stack.Id),
            second.Drops.ActiveBlocks.Select(x => x.Stack.Id));
    }
}
