using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeSaplingContentTests
{
    [Theory]
    [InlineData("oak")]
    [InlineData("willow")]
    public void SaplingsAreObjectsWithPlacementDropsAndPersistence(string family)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var catalog = new InventoryContentCatalog(
            blocks,
            PackContentRegistry<ItemDefinition>.FromJson(
                ReadJson("items"), ItemDefinition.Parse, item => item.Id),
            PackContentRegistry<ToolDefinition>.FromJson(
                ReadJson("tools"), ToolDefinition.Parse, tool => tool.Id),
            objects: objects);
        var id = "asteria:sapling_" + family;
        Assert.False(blocks.TryGetId(id, out _));
        Assert.True(catalog.TryResolve(
            InventoryEntryKind.Object, id, null, null, out var entry));
        var definition = objects.Get(id);
        Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
        Assert.StartsWith("textures/objects/sapling_", definition.Visual.Texture);
        Assert.Equal(definition.Icon, definition.Visual.Texture);
        Assert.Equal(4, definition.Visual.Planes);
        Assert.Equal(.5f, definition.Visual.AlphaCutoff);
        Assert.Equal(definition.Visual.Width, definition.Target.Size.X);
        Assert.Equal(definition.Visual.Height, definition.Target.Size.Y);
        Assert.Equal(definition.Visual.Width, definition.Target.Size.Z);
        Assert.True(definition.Supports(BlockFace.Top));
        Assert.False(definition.Supports(BlockFace.Left));
        Assert.True(definition.DropSelf);
        Assert.True(definition.WindSway);
        Assert.False(definition.Unlit);
        Assert.Null(definition.SupportFluid);
        Assert.Null(definition.RequiredFluidAbove);

        var world = new VoxelWorld();
        world.ConfigureFluidOccupancy(blocks);
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        mutations.ConfigureObjectDetachment(
            new ObjectDetachmentRuntime(world, objects, drops));
        var interactions = new WorldObjectInteractionRuntime(
            world, blocks, objects, mutations, drops);
        var support = new WorldVoxelCoord(3, 2, 3);
        var target = support + (0, 1, 0);
        var top = new VoxelWorldHit(support, 0, 1, 0);
        var held = new InventoryStack(entry!);

        Assert.False(interactions.TryPlace(held, top));
        Assert.True(mutations.SetBlockAt(
            support, blocks.GetId("asteria:dirt"), out _));
        Assert.False(interactions.TryPlace(held,
            new VoxelWorldHit(support, 1, 0, 0)));
        Assert.False(interactions.TryPlace(held,
            new VoxelWorldHit(support, 0, -1, 0)));
        Assert.True(interactions.TryPlace(held, top));
        Assert.False(interactions.TryPlace(held, top));
        Assert.True(world.GetCellOrEmpty(target).IsEmpty);
        var planted = Assert.Single(
            world.TryGetObjects(support, out var attached) ? attached : []);
        Assert.Equal(id, planted.Id);
        Assert.Equal(BlockFace.Top, planted.Face);

        var fluidRegistry = new FluidRegistry([]);
        var dyeRegistry = new DyeRegistry([]);
        var layerRegistry = new AttachedLayerRegistry([]);
        var saved = ChunkSaveCodec.Encode(
            world.GetChunk(ChunkCoord.Zero), blocks,
            fluidRegistry, dyeRegistry, layerRegistry);
        var loaded = ChunkSaveCodec.Decode(
            saved, blocks, fluidRegistry, dyeRegistry, layerRegistry);
        Assert.Equal(planted, Assert.Single(
            loaded.ObjectsAt(support.X, support.Y, support.Z)));
        Assert.True(loaded.GetCell(target.X, target.Y, target.Z).IsEmpty);

        Assert.True(interactions.TryBreak(
            new WorldObjectHit(support, planted, 1f),
            PlayerGameMode.Survival));
        var recovered = Assert.Single(drops.ActiveBlocks).Stack;
        Assert.Equal(InventoryEntryKind.Object, recovered.Kind);
        Assert.Equal(id, recovered.Id);
        Assert.True(interactions.TryPlace(recovered, top));
        Assert.Equal(planted, Assert.Single(
            world.TryGetObjects(support, out var replanted) ? replanted : []));

        Assert.True(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Empty(world.TryGetObjects(support, out var detached) ? detached : []);
        Assert.Equal(2, drops.ActiveCount);
        Assert.All(drops.ActiveBlocks, drop =>
        {
            Assert.Equal(InventoryEntryKind.Object, drop.Stack.Kind);
            Assert.Equal(id, drop.Stack.Id);
        });
        Assert.True(world.GetCellOrEmpty(target).IsEmpty);
    }

    private static IEnumerable<string> ReadJson(string directory) =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", directory), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
