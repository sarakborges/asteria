using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectPlacementResolverTests
{
    private static readonly PackContentRegistry<ObjectDefinition> Objects =
        PackContentRegistry<ObjectDefinition>.FromJson([
            """
            {"id":"asteria:starfish","category":"foliage",
             "icon":"textures/objects/starfish.png",
             "placementFaces":["top"],
             "visual":{"type":"extrudedSprite",
               "texture":"textures/objects/starfish.png",
               "size":[0.72,0.72],"height":0.012,"baseOffset":0.0125},
             "target":{"size":[0.72,0.14,0.72],"centerOffset":[0,0.07,0]}}
            """
        ], ObjectDefinition.Parse, value => value.Id);

    [Fact]
    public void PreviewAndActualPlacementShareFaceSupportAndTargetValidation()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone")
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var support = new WorldVoxelCoord(4, 5, 6);
        var target = support + (0, 1, 0);
        Assert.True(world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        var selected = new InventoryStack(InventoryEntry.FromObject("asteria:starfish"));
        var hit = new VoxelWorldHit(support, 0, 1, 0);

        var approved = ObjectPlacementResolver.Resolve(
            world, blocks, Objects, selected, hit);
        Assert.True(approved.Accepted);
        Assert.Equal(support, approved.Support);
        Assert.Equal(BlockFace.Top, approved.Object.Face);

        // An occluded target, disallowed face or unrelated selection cannot
        // create a preview that actual placement would reject.
        Assert.True(world.SetBlockAt(target, blocks.GetId("asteria:stone"), out _));
        Assert.False(ObjectPlacementResolver.Resolve(
            world, blocks, Objects, selected, hit).Accepted);
        Assert.True(world.SetBlockAt(target, BlockRuntimeId.Air, out _));
        Assert.False(ObjectPlacementResolver.Resolve(
            world, blocks, Objects, selected,
            new VoxelWorldHit(support, 1, 0, 0)).Accepted);
        Assert.False(ObjectPlacementResolver.Resolve(
            world, blocks, Objects,
            new InventoryStack(InventoryEntry.FromItem("asteria:starfish")),
            hit).Accepted);
    }
}
