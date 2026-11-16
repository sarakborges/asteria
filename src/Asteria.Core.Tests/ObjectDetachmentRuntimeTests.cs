using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectDetachmentRuntimeTests
{
    private static readonly string[] ObjectDocuments =
    [
        """
        {"id":"asteria:flower","category":"foliage",
         "icon":"textures/objects/flower.png","dropSelf":true,
         "visual":{"type":"crossedSprite","texture":"textures/objects/flower.png",
                   "width":0.75,"height":0.75}}
        """,
        """
        {"id":"asteria:grass","category":"foliage",
         "icon":"textures/objects/grass.png","dropSelf":false,
         "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png",
                   "width":0.75,"height":0.75}}
        """,
    ];

    private static (VoxelWorld World, VoxelMutationRuntime Mutations,
        DroppedBlockRuntime Drops, BlockRegistry Blocks,
        PackContentRegistry<ObjectDefinition> Objects) Setup(int capacity = 16)
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:dirt"),
        ]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ObjectDocuments, ObjectDefinition.Parse, definition => definition.Id);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks, maximumActive: capacity);
        mutations.ConfigureObjectDetachment(
            new ObjectDetachmentRuntime(world, objects, drops));
        return (world, mutations, drops, blocks, objects);
    }

    [Fact]
    public void RemovingSupportDropsOnlyEligibleObjectsAndKeepsOtherObjects()
    {
        var (world, mutations, drops, blocks, _) = Setup();
        var support = new WorldVoxelCoord(2, 3, 4);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        Assert.True(mutations.AddObjectAt(support,
            new ObjectCell("asteria:flower", BlockFace.Top)));
        Assert.True(mutations.AddObjectAt(support,
            new ObjectCell("asteria:grass", BlockFace.Top)));
        Assert.True(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.True(world.TryGetObjects(support, out var objects));
        Assert.Empty(objects);
        var dropped = Assert.Single(drops.ActiveBlocks);
        Assert.Equal("asteria:flower", dropped.Stack.Id);
        Assert.Equal(InventoryEntryKind.Object, dropped.Stack.Kind);
        Assert.Equal(1, dropped.Stack.Quantity);
        Assert.True(dropped.Position.Y > support.Y + 1);
        Assert.False(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Single(drops.ActiveBlocks);
    }

    [Fact]
    public void ReplacingSupportAlsoDetachesButSameBlockStateEditDoesNot()
    {
        var (world, mutations, drops, blocks, _) = Setup();
        var support = new WorldVoxelCoord(1, 1, 1);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        var flower = new ObjectCell("asteria:flower", BlockFace.Top);
        mutations.AddObjectAt(support, flower);
        var revised = new VoxelCell(blocks.GetId("asteria:stone"),
            textureRotation: TextureRotation.Degrees90);
        Assert.True(mutations.SetCellAt(support, revised, out _));
        Assert.True(world.TryGetObjects(support, out var stillAttached));
        Assert.Equal(new[] { flower }, stillAttached);
        Assert.Empty(drops.ActiveBlocks);
        var replacement = BlockStateSnapshot.FromCell(
            new VoxelCell(blocks.GetId("asteria:dirt")));
        Assert.True(mutations.SetBlockStateAt(support, replacement, out _));
        Assert.Single(drops.ActiveBlocks);
        Assert.True(world.TryGetObjects(support, out var detached));
        Assert.Empty(detached);
        Assert.Equal(blocks.GetId("asteria:dirt"), world.GetCellOrEmpty(support).Block);
    }

    [Fact]
    public void CreativeRemovalSuppressesDropsWithoutRetainingObjects()
    {
        var (world, mutations, drops, blocks, _) = Setup();
        var support = new WorldVoxelCoord(3, 3, 3);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        mutations.AddObjectAt(support, new ObjectCell("asteria:flower", BlockFace.Top));
        Assert.True(mutations.SetCellAt(support, VoxelCell.Empty, out _,
            BlockBreakLootPolicy.Suppress));
        Assert.Empty(drops.ActiveBlocks);
        Assert.True(world.TryGetObjects(support, out var detached));
        Assert.Empty(detached);
    }

    [Fact]
    public void FullDropPoolRejectsMutationWithoutDetachingOrEvicting()
    {
        var (world, mutations, drops, blocks, _) = Setup(capacity: 1);
        var support = new WorldVoxelCoord(2, 3, 4);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        var flower = new ObjectCell("asteria:flower", BlockFace.Top);
        mutations.AddObjectAt(support, flower);
        drops.Spawn(new InventoryStack(InventoryEntry.FromItem("asteria:existing")),
            new Vector3(6, 6, 6));
        var priorRevision = world.Revision;
        Assert.False(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Equal(priorRevision, world.Revision);
        Assert.Equal(blocks.GetId("asteria:stone"), world.GetCellOrEmpty(support).Block);
        Assert.True(world.TryGetObjects(support, out var stillAttached));
        Assert.Equal(new[] { flower }, stillAttached);
        Assert.Equal("asteria:existing", Assert.Single(drops.ActiveBlocks).Stack.Id);
    }

    [Fact]
    public void StructurePreflightsDropsForAllSupportChangesAtomically()
    {
        var (world, mutations, drops, blocks, _) = Setup(capacity: 1);
        var first = new WorldVoxelCoord(1, 2, 1);
        var second = new WorldVoxelCoord(2, 2, 1);
        foreach (var support in new[] { first, second })
        {
            mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
            mutations.AddObjectAt(support, new ObjectCell("asteria:flower", BlockFace.Top));
        }
        var revision = world.Revision;
        Assert.False(mutations.ApplyStructureChanges([
            new VoxelStructureChange(first, null, FluidCell.Empty),
            new VoxelStructureChange(second, null, FluidCell.Empty),
        ]));
        Assert.Equal(revision, world.Revision);
        Assert.Empty(drops.ActiveBlocks);
        foreach (var support in new[] { first, second })
        {
            Assert.Equal(blocks.GetId("asteria:stone"), world.GetCellOrEmpty(support).Block);
            Assert.True(world.TryGetObjects(support, out var attached));
            Assert.Single(attached);
        }
    }

    [Fact]
    public void BlockBreakPreflightsCombinedHostAndObjectDrops()
    {
        var (world, mutations, drops, blocks, _) = Setup(capacity: 1);
        var support = new WorldVoxelCoord(2, 3, 4);
        mutations.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        mutations.AddObjectAt(support, new ObjectCell("asteria:flower", BlockFace.Top));
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops);
        var hit = new VoxelWorldHit(support, 0, 1, 0);
        var result = interactions.Break(hit, BlockBreakLootPolicy.DropSelf);
        Assert.False(result.Accepted);
        Assert.Equal(blocks.GetId("asteria:stone"), world.GetCellOrEmpty(support).Block);
        Assert.True(world.TryGetObjects(support, out var attached));
        Assert.Single(attached);
        Assert.Empty(drops.ActiveBlocks);

        result = interactions.Break(hit, BlockBreakLootPolicy.Suppress);
        Assert.True(result.Accepted);
        Assert.Empty(drops.ActiveBlocks);
    }
}
