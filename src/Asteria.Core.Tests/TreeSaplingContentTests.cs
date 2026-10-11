using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeSaplingContentTests
{
    [Theory]
    [InlineData("charwood")]
    [InlineData("enchanted_tree")]
    [InlineData("frost_wood")]
    [InlineData("juniper")]
    [InlineData("oak")]
    [InlineData("rime_deadwood")]
    [InlineData("skywood")]
    [InlineData("thornwood")]
    [InlineData("willow")]
    [InlineData("windwood")]
    [InlineData("world_tree")]
    public void SaplingsCanBeSelectedPlantedRecoveredAndReplanted(string family)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var catalog = new InventoryContentCatalog(
            blocks,
            PackContentRegistry<ItemDefinition>.FromJson(ReadJson("items"),
                ItemDefinition.Parse, item => item.Id),
            PackContentRegistry<ToolDefinition>.FromJson(ReadJson("tools"),
                ToolDefinition.Parse, tool => tool.Id));
        var id = "asteria:sapling_" + family;
        Assert.True(catalog.TryResolve(
            InventoryEntryKind.Block, id, null, null, out var entry));
        var cell = entry!.Block!.Cell;
        Assert.Equal(BlockVisualKind.CrossedSprite,
            blocks.GetDefinition(cell.Block).Visual.Kind);
        Assert.Equal($"textures/blocks/trees/{family}/sapling.png",
            blocks.GetDefinition(cell.Block).Visual.Texture!.Value.Texture);

        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var physicsUpdates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), physicsUpdates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops, inventoryCatalog: catalog);
        var physics = new BlockPhysicsRuntime(
            world, blocks, mutations, physicsUpdates, drops);
        var support = new WorldVoxelCoord(3, 2, 3);
        var position = support + (0, 1, 0);
        var top = new VoxelWorldHit(support, 0, 1, 0);
        // A player can occupy the plant's space: saplings must not obstruct walking.
        var player = new WorldAabb(new Vector3(3, 3, 3), new Vector3(4, 4, 4));

        Assert.Equal(BlockPlacementRejection.MissingSupport,
            interactions.Place(top, cell, player).Rejection);
        Assert.True(mutations.SetBlockAt(
            support, blocks.GetId("asteria:dirt"), out _));
        Assert.Equal(BlockPlacementRejection.UnsupportedPlacementFace,
            interactions.Place(
                new VoxelWorldHit(support, 1, 0, 0), cell, player).Rejection);
        Assert.Equal(BlockPlacementRejection.UnsupportedPlacementFace,
            interactions.Place(
                new VoxelWorldHit(support, 0, -1, 0), cell, player).Rejection);

        Assert.True(interactions.Place(top, cell, player).Accepted);
        physics.ProcessWakeups();
        Assert.Equal(cell, world.GetCellOrEmpty(position));
        Assert.Empty(drops.ActiveBlocks);

        Assert.True(interactions.Break(
            new VoxelWorldHit(position, 0, 1, 0)).Accepted);
        Assert.True(world.GetCellOrEmpty(position).IsEmpty);
        var recovered = catalog.ForDroppedBlock(Assert.Single(drops.ActiveBlocks).Block);
        Assert.Equal(id, recovered.Id);
        Assert.True(interactions.Place(top, recovered.Block!.Cell, player).Accepted);
        physics.ProcessWakeups();
        Assert.Equal(cell, world.GetCellOrEmpty(position));

        Assert.True(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Equal(1, physics.ProcessWakeups().UnsupportedRemoved);
        Assert.True(world.GetCellOrEmpty(position).IsEmpty);
        Assert.Equal(2, drops.ActiveCount);
        Assert.All(drops.ActiveBlocks, drop =>
            Assert.Equal(cell, drop.Block.Cell));
    }

    private static IEnumerable<string> ReadJson(string directory) =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
