using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeLeafLootTests
{
    private static string DataRoot => Path.Combine(
        AppContext.BaseDirectory, "packs", "default", "data");

    private static IEnumerable<string> ReadDocuments(string directory) =>
        Directory.EnumerateFiles(
                Path.Combine(DataRoot, directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

    private sealed record Content(
        BlockRegistry Blocks,
        InventoryContentCatalog Catalog,
        PackContentRegistry<ObjectDefinition> Objects);

    private static Content LoadContent()
    {
        var blocks = BlockRegistry.FromJson(ReadDocuments("blocks"));
        var items = PackContentRegistry<ItemDefinition>.FromJson(
            ReadDocuments("items"), ItemDefinition.Parse, item => item.Id);
        var tools = PackContentRegistry<ToolDefinition>.FromJson(
            ReadDocuments("tools"), ToolDefinition.Parse, tool => tool.Id);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadDocuments("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var layers = AttachedLayerRegistry.FromJson(ReadDocuments("layers"));
        var catalog = new InventoryContentCatalog(
            blocks, items, tools, layers: layers, objects: objects);
        return new Content(blocks, catalog, objects);
    }

    [Fact]
    public void EveryLeafFamilyAuthorsItsMatchingSaplingDrop()
    {
        var content = LoadContent();
        var leaves = Directory.EnumerateFiles(
                Path.Combine(DataRoot, "blocks"), "leaf_*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(leaves);
        foreach (var path in leaves)
        {
            var leaf = BlockDefinitionJson.Parse(File.ReadAllText(path));
            var family = Path.GetFileNameWithoutExtension(path)["leaf_".Length..];
            var saplingId = "asteria:sapling_" + family;

            Assert.True(
                content.Objects.TryGet(saplingId, out var sapling) && sapling is not null,
                $"{leaf.Id} has no matching sapling Object {saplingId}.");
            var table = Assert.IsType<LootTable>(leaf.LootTable);
            var saplingDrop = Assert.Single(
                table.Entries.Where(entry => entry.Item == saplingId));
            Assert.Equal(0.05f, saplingDrop.Chance);
            Assert.Equal(1, saplingDrop.Quantity);
            Assert.True(content.Catalog.TryResolve(
                InventoryEntryKind.Object, saplingId, null, null, out var resolved));
            Assert.Equal(InventoryEntryKind.Object, resolved!.Kind);
            Assert.Equal(saplingId, resolved.Id);
        }
    }

    [Fact]
    public void BreakingLeavesCanProduceAPhysicalDropOfTheMatchingSapling()
    {
        var content = LoadContent();
        const string leafId = "asteria:leaf_oak";
        const string saplingId = "asteria:sapling_oak";
        var leaf = content.Blocks.GetDefinition(
            content.Blocks.GetId(leafId));
        var table = Assert.IsType<LootTable>(leaf.LootTable);

        WorldVoxelCoord position = default;
        ulong worldTick = 0;
        var foundSeed = false;
        for (var sample = 0; sample < 2048 && !foundSeed; sample++)
        {
            var candidate = new WorldVoxelCoord(
                1 + sample % 14,
                3,
                1 + sample * 7 % 14);
            var tick = (ulong)(sample * 37 + 1);
            var rewards = LootTableRoller.Roll(
                table, content.Catalog, candidate, tick);
            if (!rewards.Any(reward =>
                    reward.Kind == InventoryEntryKind.Object &&
                    reward.Id == saplingId))
                continue;

            position = candidate;
            worldTick = tick;
            foundSeed = true;
        }

        Assert.True(foundSeed, "The deterministic sample must include a sapling reward.");

        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(content.Blocks);
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, content.Blocks);
        var interactions = new BlockInteractionRuntime(
            world, content.Blocks, mutations, drops,
            inventoryCatalog: content.Catalog);

        Assert.True(mutations.SetBlockAt(
            position, content.Blocks.GetId(leafId), out _));
        var result = interactions.Break(
            new VoxelWorldHit(position, 0, 1, 0), worldTick: worldTick);

        Assert.True(result.Accepted);
        var saplingDrop = Assert.Single(
            drops.ActiveBlocks.Where(drop => drop.Stack.Id == saplingId));
        Assert.Equal(InventoryEntryKind.Object, saplingDrop.Stack.Kind);
    }
}
