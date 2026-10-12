using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldObjectTargetResolverTests
{
    private static readonly PackContentRegistry<ObjectDefinition> Definitions =
        PackContentRegistry<ObjectDefinition>.FromJson(["""
        {"id":"asteria:flower","category":"foliage",
         "icon":"textures/objects/daisy.png",
         "visual":{"type":"crossedSprite","texture":"textures/objects/daisy.png",
                   "height":0.65},
         "target":{"size":[0.7,0.65,0.7],"centerOffset":[0,0.325,0]}}
        """], ObjectDefinition.Parse, definition => definition.Id);

    [Fact]
    public void BlockInFrontOfObjectPreventsTargetingThroughWall()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var support = new WorldVoxelCoord(0, 1, 4);
        var blocker = new WorldVoxelCoord(0, 2, 2);
        world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _);
        world.SetBlockAt(blocker, blocks.GetId("asteria:stone"), out _);
        var obj = new ObjectCell("asteria:flower", BlockFace.Top);
        Assert.True(world.AddObjectAt(support, obj));
        var origin = new Vector3(.5f, 2.325f, 0f);
        var forward = Vector3.UnitZ;
        Assert.Null(WorldObjectTargetResolver.RaycastVisible(
            world, blocks, Definitions, origin, forward, 6f));

        Assert.True(world.SetBlockAt(blocker, BlockRuntimeId.Air, out _));
        var visible = WorldObjectTargetResolver.RaycastVisible(
            world, blocks, Definitions, origin, forward, 6f);
        Assert.NotNull(visible);
        Assert.Equal(support, visible.Value.Support);
    }

    [Fact]
    public void EmptyObjectCollectionDoesNotFabricateTarget()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.Null(WorldObjectTargetResolver.RaycastVisible(
            world, blocks, Definitions, Vector3.Zero, Vector3.UnitZ, 6f));
    }
}
