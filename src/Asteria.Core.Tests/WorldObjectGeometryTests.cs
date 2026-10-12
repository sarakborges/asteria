using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldObjectGeometryTests
{
    private const string Grass = """
    {"id":"asteria:grass","category":"foliage",
     "icon":"textures/objects/grass.png",
     "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png",
       "width":0.72,"height":0.62,"planes":4},
     "target":{"size":[0.72,0.62,0.72],"centerOffset":[0,0.31,0]}}
    """;

    private static readonly PackContentRegistry<ObjectDefinition> Definitions =
        PackContentRegistry<ObjectDefinition>.FromJson(
            [Grass], ObjectDefinition.Parse, definition => definition.Id);

    [Fact]
    public void SupportFaceRotationMovesTargetOutwardFromBlock()
    {
        var objectDefinition = Definitions.Get("asteria:grass");
        var support = new WorldVoxelCoord(0, 2, 0);
        var obj = new ObjectCell("asteria:grass", BlockFace.Top);
        var top = WorldObjectGeometry.Pose(support, obj, objectDefinition);
        Assert.Equal(new Vector3(.5f, 3f, .5f), top.Position);
        Assert.Equal(Vector3.One, top.Scale);
        var bounds = WorldObjectGeometry.TargetAabb(top, objectDefinition.Target);
        Assert.Equal(3f, bounds.Minimum.Y);
        Assert.Equal(3.62f, bounds.Maximum.Y, 3);

        // Definition permits only Top, so disallowed face cannot silently
        // affect either the rendered pose or the hitbox.
        Assert.Throws<ArgumentException>(() => WorldObjectGeometry.Pose(
            support, new ObjectCell("asteria:grass", BlockFace.Right),
            objectDefinition));
    }

    [Fact]
    public void RotatedOffsetAndScaleAreSharedByPresentationAndHitbox()
    {
        var obj = new ObjectCell("asteria:grass", BlockFace.Top,
            TextureRotation.Degrees90,
            ObjectTransform.FromParts(new Vector3(.125f, 0f, 0f),
                new Vector3(1f, .5f, 1f)));
        var pose = WorldObjectGeometry.Pose(new WorldVoxelCoord(0, 0, 0),
            obj, Definitions.Get(obj.Id));
        Assert.Equal(new Vector3(.5f, 1f, .375f), pose.Position, new Vector3Comparer(.0001f));
        Assert.Equal(.5f, pose.Scale.Y);
        var center = pose.ToWorld(Definitions.Get(obj.Id).Target.CenterOffset);
        Assert.True(WorldObjectGeometry.TryHitTarget(
            pose, Definitions.Get(obj.Id).Target,
            center + new Vector3(0f, 0f, -4f),
            Vector3.UnitZ, 8f, out var distance));
        Assert.InRange(distance, 3f, 4f);
    }

    [Fact]
    public void RaycastReadsSparseObjectGroupsAndFindsNearest()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var chunk = new Chunk();
        chunk.SetBlock(0, 0, 0, blocks.GetId("asteria:stone"));
        chunk.SetBlock(0, 0, 2, blocks.GetId("asteria:stone"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var objectCell = new ObjectCell("asteria:grass", BlockFace.Top);
        Assert.True(world.AddObjectAt(new WorldVoxelCoord(0, 0, 0), objectCell));
        Assert.True(world.AddObjectAt(new WorldVoxelCoord(0, 0, 2), objectCell));

        var hit = WorldObjectRaycaster.Raycast(
            world, Definitions, new Vector3(.5f, 1.31f, -3f),
            Vector3.UnitZ, 8f);
        Assert.NotNull(hit);
        Assert.Equal(new WorldVoxelCoord(0, 0, 0), hit.Value.Support);
        Assert.True(hit.Value.Distance > 0f);
        Assert.Null(WorldObjectRaycaster.Raycast(
            world, Definitions, new Vector3(25f, 1.31f, -3f),
            Vector3.UnitZ, 8f));
        Assert.Null(WorldObjectRaycaster.Raycast(
            world, Definitions, Vector3.Zero,
            Vector3.Zero, 6f));
        Assert.Null(WorldObjectRaycaster.Raycast(
            world, Definitions, Vector3.Zero,
            Vector3.UnitZ, 40f));
    }

    [Fact]
    public void RaycastFindsOffsetScaledObjectAcrossChunkBoundaries()
    {
        var definitions = PackContentRegistry<ObjectDefinition>.FromJson(["""
        {"id":"asteria:offset_target","category":"foliage",
         "icon":"textures/objects/grass.png",
         "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png"},
         "target":{"size":[0.5,0.5,0.5],"centerOffset":[2,0.325,0]}}
        """], ObjectDefinition.Parse, definition => definition.Id);
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var support = new WorldVoxelCoord(0, 0, 0);
        Assert.True(world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        Assert.True(world.AddObjectAt(support,
            new ObjectCell("asteria:offset_target", BlockFace.Top,
                transform: ObjectTransform.FromParts(
                    new Vector3(8f, 0f, 0f), new Vector3(8f, 1f, 1f)))));

        // Target lies in chunk X=1 while its supporting chunk is X=0.
        var hit = WorldObjectRaycaster.Raycast(world, definitions,
            new Vector3(24.5f, 1.325f, -3f), Vector3.UnitZ, 6f);
        Assert.NotNull(hit);
        Assert.Equal(support, hit.Value.Support);
        Assert.InRange(hit.Value.Distance, 2f, 4f);
    }

    [Fact]
    public void RaycastFindsDefinitionTargetDisplacedBeyondSupportChunk()
    {
        var definitions = PackContentRegistry<ObjectDefinition>.FromJson(["""
        {"id":"asteria:remote_target","category":"foliage",
         "icon":"textures/objects/grass.png",
         "visual":{"type":"crossedSprite","texture":"textures/objects/grass.png"},
         "target":{"size":[0.5,0.5,0.5],"centerOffset":[40,0.325,0]}}
        """], ObjectDefinition.Parse, definition => definition.Id);
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        var support = new WorldVoxelCoord(-17, 0, 0);
        var coord = VoxelCoordinates.FromWorld(support.X, support.Y, support.Z).Chunk;
        world.InsertChunk(coord, new Chunk());
        Assert.True(world.SetBlockAt(support, blocks.GetId("asteria:stone"), out _));
        Assert.True(world.AddObjectAt(support,
            new ObjectCell("asteria:remote_target", BlockFace.Top)));

        // Negative support chunk X=-2, target lives in world chunk X=1.
        var hit = WorldObjectRaycaster.Raycast(world, definitions,
            new Vector3(23.5f, 1.325f, -3f), Vector3.UnitZ, 6f);
        Assert.NotNull(hit);
        Assert.Equal(support, hit.Value.Support);
    }

    [Fact]
    public void ObjectsChangedEventIncludesSupportRemovalAndNoOpMutationsDoNotEmit()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        var chunk = new Chunk();
        chunk.SetBlock(0, 0, 0, blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var changed = new List<ChunkCoord>();
        world.ObjectChunkChanged += changed.Add;
        var obj = new ObjectCell("asteria:grass", BlockFace.Top);
        var support = new WorldVoxelCoord(0, 0, 0);
        Assert.True(world.AddObjectAt(support, obj));
        Assert.False(world.AddObjectAt(support, obj));
        Assert.True(world.SetCellAt(support, VoxelCell.Empty, out _));
        Assert.Equal(new[] { ChunkCoord.Zero, ChunkCoord.Zero }, changed);
        Assert.False(world.RemoveObjectAt(support, obj));
        Assert.Empty(world.ResidentObjectChunkCoords);
    }

    private sealed class Vector3Comparer(float epsilon) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) =>
            Vector3.Distance(x, y) <= epsilon;

        public int GetHashCode(Vector3 obj) => obj.GetHashCode();
    }
}
