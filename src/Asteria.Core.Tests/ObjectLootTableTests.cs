using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectLootTableTests
{
    private static readonly WorldVoxelCoord Support = new(2, 3, 4);

    private sealed record Fixture(
        VoxelWorld World, VoxelMutationRuntime Mutations,
        WorldObjectInteractionRuntime Interactions,
        DroppedBlockRuntime Drops, ObjectCell Object);

    private static Fixture Setup(string? table, int capacity = 64,
        bool dropSelf = true, ulong tick = 12)
    {
        var blocks = BlockRegistry.FromJson(["""{"id":"asteria:stone"}"""]);
        var items = PackContentRegistry<ItemDefinition>.FromJson([
            """
            {"id":"asteria:reed_fiber","category":"materials",
             "icon":"textures/items/fiber.png","maxStackSize":16}
            """
        ], ItemDefinition.Parse, x => x.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, x => x.Id);
        var lootPart = table is null ? "" : ",\"lootTable\":" + table;
        var objects = PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:flower","category":"foliage",
             "icon":"textures/objects/flower.png",
             "visual":{"type":"crossedSprite",
                       "texture":"textures/objects/flower.png",
                       "width":0.75,"height":0.75},
             "dropSelf":
            """ + (dropSelf ? "true" : "false") + lootPart + "}"
        ], ObjectDefinition.Parse, x => x.Id);
        var catalog = new InventoryContentCatalog(blocks, items, tools,
            objects: objects);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks, maximumActive: capacity);
        Func<ulong> now = () => tick;
        mutations.ConfigureObjectDetachment(
            new ObjectDetachmentRuntime(world, objects, drops,
                catalog: catalog, worldTick: now));
        Assert.True(mutations.SetBlockAt(Support,
            blocks.GetId("asteria:stone"), out _));
        var obj = new ObjectCell("asteria:flower", BlockFace.Top);
        Assert.True(mutations.AddObjectAt(Support, obj));
        var interactions = new WorldObjectInteractionRuntime(
            world, blocks, objects, mutations, drops,
            catalog: catalog, worldTick: now);
        return new Fixture(world, mutations, interactions, drops, obj);
    }

    [Fact]
    public void AuthoredLootOverridesSelfDropAndSplitsToActualStackLimits()
    {
        var f = Setup("""
            [{"item":"asteria:reed_fiber","quantity":33},
             {"item":"asteria:flower","quantity":2}]
            """, dropSelf: false);
        Assert.True(f.Interactions.TryBreak(
            new WorldObjectHit(Support, f.Object, 1f),
            PlayerGameMode.Survival));
        var rewards = f.Drops.ActiveBlocks.ToArray();
        Assert.Equal(4, rewards.Length);
        Assert.Equal([16, 16, 1],
            rewards.Where(x => x.Stack.Id == "asteria:reed_fiber")
                .Select(x => x.Stack.Quantity).ToArray());
        var self = Assert.Single(rewards.Where(x => x.Stack.Id == "asteria:flower"));
        Assert.Equal(InventoryEntryKind.Object, self.Stack.Kind);
        Assert.Equal(2, self.Stack.Quantity);
    }

    [Fact]
    public void EmptyTableAndCreativeBreakNeverDropAnything()
    {
        var empty = Setup("[]");
        Assert.True(empty.Interactions.TryBreak(
            new WorldObjectHit(Support, empty.Object, 1f),
            PlayerGameMode.Survival));
        Assert.Empty(empty.Drops.ActiveBlocks);

        var creative = Setup("""[{"item":"asteria:reed_fiber","quantity":33}]""");
        Assert.True(creative.Interactions.TryBreak(
            new WorldObjectHit(Support, creative.Object, 1f),
            PlayerGameMode.Creative));
        Assert.Empty(creative.Drops.ActiveBlocks);
    }

    [Fact]
    public void DirectBreakAndSupportRemovalRejectCapacityBeforeMutation()
    {
        const string table = """[{"item":"asteria:reed_fiber","quantity":33}]""";
        var direct = Setup(table, capacity: 2);
        var revision = direct.World.Revision;
        Assert.False(direct.Interactions.TryBreak(
            new WorldObjectHit(Support, direct.Object, 1f),
            PlayerGameMode.Survival));
        Assert.Equal(revision, direct.World.Revision);
        Assert.True(direct.World.TryGetObjects(Support, out var attached));
        Assert.Single(attached);
        Assert.Empty(direct.Drops.ActiveBlocks);

        var detached = Setup(table, capacity: 2);
        revision = detached.World.Revision;
        Assert.False(detached.Mutations.SetCellAt(
            Support, VoxelCell.Empty, out _));
        Assert.Equal(revision, detached.World.Revision);
        Assert.True(detached.World.TryGetObjects(Support, out attached));
        Assert.Single(attached);
        Assert.Empty(detached.Drops.ActiveBlocks);
    }

    [Fact]
    public void SupportRemovalAppliesAuthoredLootWithoutSelfDrop()
    {
        var f = Setup("""[{"item":"asteria:reed_fiber","quantity":33}]""",
            capacity: 3);
        Assert.True(f.Mutations.SetCellAt(Support, VoxelCell.Empty, out _));
        Assert.Equal([16, 16, 1],
            f.Drops.ActiveBlocks.Select(x => x.Stack.Quantity).ToArray());
        Assert.False(f.World.TryGetObjects(Support, out var attached) &&
            attached.Count > 0);
    }

    [Fact]
    public void ChanceIsRepeatableAndZeroChanceSuppressesRewards()
    {
        var first = Setup("""[{"item":"asteria:reed_fiber","chance":0.5}]""");
        var second = Setup("""[{"item":"asteria:reed_fiber","chance":0.5}]""");
        foreach (var f in new[] { first, second })
            Assert.True(f.Interactions.TryBreak(
                new WorldObjectHit(Support, f.Object, 1f),
                PlayerGameMode.Survival));
        Assert.Equal(first.Drops.ActiveBlocks.Select(x => x.Stack.Id),
            second.Drops.ActiveBlocks.Select(x => x.Stack.Id));
        var zero = Setup("""[{"item":"asteria:reed_fiber","chance":0}]""");
        Assert.True(zero.Interactions.TryBreak(
            new WorldObjectHit(Support, zero.Object, 1f),
            PlayerGameMode.Survival));
        Assert.Empty(zero.Drops.ActiveBlocks);
    }

    [Fact]
    public void DefaultPackGrassPaysRealCraftingFiberWithDeterministicChance()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data");
        var grass = ObjectDefinition.Parse(File.ReadAllText(Path.Combine(
            root, "objects", "grass.json")));
        var fiber = ItemDefinition.Parse(File.ReadAllText(Path.Combine(
            root, "items", "fiber_plant.json")));
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            [File.ReadAllText(Path.Combine(root, "items", "fiber_plant.json"))],
            ItemDefinition.Parse, item => item.Id);
        var blocks = BlockRegistry.FromJson(["""{"id":"asteria:stone"}"""]);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, tool => tool.Id);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            [File.ReadAllText(Path.Combine(root, "objects", "grass.json"))],
            ObjectDefinition.Parse, obj => obj.Id);
        var catalog = new InventoryContentCatalog(blocks, items, tools,
            objects: objects);
        var entry = Assert.Single(Assert.IsType<LootTable>(grass.LootTable).Entries);
        Assert.Equal("asteria:fiber_plant", fiber.Id);
        Assert.Equal(fiber.Id, entry.Item);
        Assert.Equal(0.2f, entry.Chance);
        Assert.Equal(1, entry.Quantity);
        Assert.False(grass.DropSelf);

        var objectCell = new ObjectCell(grass.Id, BlockFace.Top);
        var position = new WorldVoxelCoord(2, 3, 4);
        var drops = 0;
        for (ulong tick = 0; tick < 128; tick++)
        {
            var first = LootTableRoller.ResolveObject(
                grass, objectCell, position, catalog, tick);
            var second = LootTableRoller.ResolveObject(
                grass, objectCell, position, catalog, tick);
            Assert.Equal(first.Select(x => x.Id), second.Select(x => x.Id));
            Assert.True(first.Count <= 1);
            if (first.Count == 0) continue;
            var reward = Assert.Single(first);
            Assert.Equal(fiber.Id, reward.Id);
            Assert.Equal(InventoryEntryKind.Item, reward.Kind);
            Assert.Equal(1, reward.Quantity);
            drops++;
        }
        Assert.InRange(drops, 1, 127);
    }

    [Fact]
    public void InvalidAndUnknownAuthoredLootIsRejectedAtLoad()
    {
        Assert.Throws<FormatException>(() => Setup("{}"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Setup("""[{"item":"asteria:reed_fiber","quantity":0}]"""));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Setup("""[{"item":"asteria:reed_fiber","chance":1.1}]"""));
        Assert.Throws<InvalidOperationException>(() =>
            Setup("""[{"item":"asteria:unknown"}]"""));
    }
}
