using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LeafDecayRuntimeTests
{
    private const string LogJson = """
        {"id":"asteria:log_test","tags":["leaf_support"],
         "variant":{"family":"asteria:log_test","key":"natural"},"dropsSelf":false}
        """;
    private const string StrippedLogJson = """
        {"id":"asteria:log_test_stripped",
         "variant":{"family":"asteria:log_test","key":"stripped"},"dropsSelf":false}
        """;
    private const string LeafJson = """
        {"id":"asteria:leaf_test","category":"foliage","tags":["leaf_decay"],
         "dropsSelf":false,
         "lootTable":[{"item":"asteria:sapling_test","chance":1.0,"quantity":1}]}
        """;
    private const string StoneJson = """{"id":"asteria:stone"}""";
    private const string SaplingJson = """
        {"id":"asteria:sapling_test","category":"foliage",
         "icon":"textures/objects/grass.png",
         "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png"},
         "dropSelf":false}
        """;

    private sealed record Fixture(
        BlockRegistry Blocks, VoxelWorld World, VoxelMutationRuntime Mutations,
        BlockInteractionRuntime Interactions, DroppedBlockRuntime Drops,
        LeafDecayRuntime Decay);

    private static Fixture CreateFixture()
    {
        var blocks = BlockRegistry.FromJson([
            StoneJson, LogJson, StrippedLogJson, LeafJson,
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            [SaplingJson], ObjectDefinition.Parse, obj => obj.Id);
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            [], ItemDefinition.Parse, item => item.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, tool => tool.Id);
        var catalog = new InventoryContentCatalog(blocks, items, tools, objects: objects);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops, inventoryCatalog: catalog);
        var decay = new LeafDecayRuntime(world, blocks, mutations, interactions);
        return new Fixture(blocks, world, mutations, interactions, drops, decay);
    }

    private static WorldVoxelCoord At(int x, int y, int z) => new(x, y, z);

    private static void PlaceBlock(Fixture fixture, WorldVoxelCoord position, string id) =>
        Assert.True(fixture.Mutations.SetBlockAt(position, fixture.Blocks.GetId(id), out _));

    [Fact]
    public void UnsupportedNaturalLeavesDecayAndProduceTheirAuthoredPhysicalLoot()
    {
        var fixture = CreateFixture();
        var trunk = At(5, 5, 5);
        var lowerLeaf = At(5, 6, 5);
        var upperLeaf = At(5, 7, 5);
        PlaceBlock(fixture, trunk, "asteria:log_test");
        PlaceBlock(fixture, lowerLeaf, "asteria:leaf_test");
        PlaceBlock(fixture, upperLeaf, "asteria:leaf_test");

        Assert.True(fixture.Mutations.SetCellAt(trunk, VoxelCell.Empty, out _));
        Assert.Equal(2, fixture.Decay.Advance(1));

        Assert.True(fixture.World.GetCellOrEmpty(lowerLeaf).IsEmpty);
        Assert.True(fixture.World.GetCellOrEmpty(upperLeaf).IsEmpty);
        Assert.Equal(2, fixture.Drops.ActiveCount);
        Assert.All(fixture.Drops.ActiveBlocks, drop =>
        {
            Assert.Equal(InventoryEntryKind.Object, drop.Stack.Kind);
            Assert.Equal("asteria:sapling_test", drop.Stack.Id);
        });
    }

    [Fact]
    public void LeavesRemainWhenConnectedToAnotherLogThroughLeafBlocksAndLogVariants()
    {
        var fixture = CreateFixture();
        var removedTrunk = At(5, 5, 5);
        var firstLeaf = At(6, 5, 5);
        var secondLeaf = At(7, 5, 5);
        var survivingTrunk = At(8, 5, 5);
        PlaceBlock(fixture, removedTrunk, "asteria:log_test");
        PlaceBlock(fixture, firstLeaf, "asteria:leaf_test");
        PlaceBlock(fixture, secondLeaf, "asteria:leaf_test");
        PlaceBlock(fixture, survivingTrunk, "asteria:log_test_stripped");

        Assert.True(fixture.Mutations.SetCellAt(removedTrunk, VoxelCell.Empty, out _));
        Assert.Equal(0, fixture.Decay.Advance(1));
        Assert.False(fixture.World.GetCellOrEmpty(firstLeaf).IsEmpty);
        Assert.False(fixture.World.GetCellOrEmpty(secondLeaf).IsEmpty);
        Assert.Equal(0, fixture.Drops.ActiveCount);
    }

    [Fact]
    public void DiagonalLeafRemainsSupportedByNearbyLog()
    {
        var fixture = CreateFixture();
        var trunk = At(5, 5, 5);
        var diagonalLeaf = At(6, 6, 5);
        PlaceBlock(fixture, trunk, "asteria:log_test");
        PlaceBlock(fixture, diagonalLeaf, "asteria:leaf_test");

        Assert.Equal(0, fixture.Decay.Advance(1));
        Assert.False(fixture.World.GetCellOrEmpty(diagonalLeaf).IsEmpty);
        Assert.Equal(0, fixture.Drops.ActiveCount);
    }

    [Fact]
    public void PlayerPlacedLeavesStayProtectedAfterTheirNearbyLogIsRemoved()
    {
        var fixture = CreateFixture();
        var soil = At(5, 4, 5);
        var leaf = At(5, 5, 5);
        var trunk = At(6, 5, 5);
        PlaceBlock(fixture, soil, "asteria:stone");
        PlaceBlock(fixture, trunk, "asteria:log_test");
        var farAwayPlayer = new WorldAabb(
            new Vector3(100f, 100f, 100f),
            new Vector3(101f, 101f, 101f));
        var placement = fixture.Interactions.Place(
            new VoxelWorldHit(soil, 0, 1, 0),
            new VoxelCell(fixture.Blocks.GetId("asteria:leaf_test")),
            farAwayPlayer);
        Assert.True(placement.Accepted);
        Assert.True(fixture.World.GetCellOrEmpty(leaf).IsPlayerPlaced);

        Assert.True(fixture.Mutations.SetCellAt(trunk, VoxelCell.Empty, out _));
        Assert.Equal(0, fixture.Decay.Advance(1));
        Assert.False(fixture.World.GetCellOrEmpty(leaf).IsEmpty);
        Assert.True(fixture.World.GetCellOrEmpty(leaf).IsPlayerPlaced);
        Assert.Equal(0, fixture.Drops.ActiveCount);
    }

    [Fact]
    public void DecayDefersWhenSupportSearchCrossesAnUnloadedChunk()
    {
        var fixture = CreateFixture();
        var trunk = At(14, 5, 8);
        var leaf = At(15, 5, 8);
        PlaceBlock(fixture, trunk, "asteria:log_test");
        PlaceBlock(fixture, leaf, "asteria:leaf_test");
        Assert.True(fixture.Mutations.SetCellAt(trunk, VoxelCell.Empty, out _));

        Assert.Equal(0, fixture.Decay.Advance(1));
        Assert.False(fixture.World.GetCellOrEmpty(leaf).IsEmpty);
        fixture.World.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        Assert.Equal(1, fixture.Decay.Advance(2));
        Assert.True(fixture.World.GetCellOrEmpty(leaf).IsEmpty);
        Assert.Equal(1, fixture.Drops.ActiveCount);
    }

    [Fact]
    public void ResidentUnsupportedLeavesAreReconciledWhenRuntimeStarts()
    {
        var fixture = CreateFixture();
        var leaf = At(5, 5, 5);
        PlaceBlock(fixture, leaf, "asteria:leaf_test");
        fixture.Decay.Dispose();
        var runtime = new LeafDecayRuntime(
            fixture.World, fixture.Blocks, fixture.Mutations, fixture.Interactions);

        Assert.Equal(1, runtime.Advance(1));
        Assert.True(fixture.World.GetCellOrEmpty(leaf).IsEmpty);
        runtime.Dispose();
    }

    [Fact]
    public void DefaultPackAuthoredLeafAndLogFamiliesDeclareDecayAndSupport()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data", "blocks");
        var leaves = Directory.GetFiles(root, "leaf_*.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var logs = Directory.GetFiles(root, "log_*.json")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(38, leaves.Length);
        Assert.Equal(152, logs.Length);

        Assert.All(leaves, path =>
        {
            var definition = BlockDefinitionJson.Parse(File.ReadAllText(path));
            Assert.True(definition.HasTag(TreeBlockTags.DecayableLeaf), path);
        });

        var logDefinitions = logs.Select(path =>
            (Path: path, Definition: BlockDefinitionJson.Parse(File.ReadAllText(path)))).ToArray();
        var supportFamilies = logDefinitions
            .Where(entry => entry.Definition.HasTag(TreeBlockTags.LeafSupport))
            .Select(entry => entry.Definition.Variant?.Family)
            .Where(family => family is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(38, supportFamilies.Count);
        Assert.All(logDefinitions, entry =>
        {
            Assert.NotNull(entry.Definition.Variant);
            Assert.Contains(entry.Definition.Variant!.Family, supportFamilies);
        });
    }
}
