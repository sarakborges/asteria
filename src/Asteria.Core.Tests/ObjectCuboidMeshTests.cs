using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ObjectCuboidMeshTests
{
    private const string Cuboid = """
        {"id":"asteria:branch","category":"foliage",
         "icon":"textures/objects/branch.png",
         "visual":{"type":"cuboidSet",
           "texture":"textures/objects/branch.png",
           "parts":[
             {"from":[-0.15,0,-0.10],"to":[0.15,0.65,0.10],
              "uv":[0.125,0.25,0.625,0.75]},
             {"from":[-0.30,0.55,-0.05],"to":[0.30,0.75,0.05]}
           ]}}
        """;

    [Fact]
    public void ParsesPartsAndDerivesAccurateTargetBounds()
    {
        var obj = ObjectDefinition.Parse(Cuboid);
        Assert.Equal(ObjectVisualKind.CuboidSet, obj.Visual.Kind);
        Assert.Equal(2, obj.Visual.Parts!.Count);
        Assert.Equal(new Vector4(.125f, .25f, .625f, .75f),
            obj.Visual.Parts[0].Uv);
        Assert.Equal(new Vector4(0, 0, 1, 1), obj.Visual.Parts[1].Uv);
        Assert.Equal(new Vector3(.6f, .75f, .2f), obj.Target.Size);
        Assert.Equal(new Vector3(0f, .375f, 0f), obj.Target.CenterOffset);
    }

    [Fact]
    public void EmitsAllFacesWithAuthoredUvAndOutwardNormals()
    {
        var obj = ObjectDefinition.Parse(Cuboid);
        var a = ObjectCuboidMesh.Build(obj.Visual);
        var b = ObjectCuboidMesh.Build(obj.Visual);
        Assert.Equal(72, a.Length);
        Assert.Equal(a, b);
        Assert.Equal(-Vector3.UnitX, a[0].Normal);
        Assert.Equal(Vector3.UnitX, a[6].Normal);
        Assert.Equal(Vector3.UnitY, a[12].Normal);
        Assert.Equal(-Vector3.UnitY, a[18].Normal);
        Assert.Equal(Vector3.UnitZ, a[24].Normal);
        Assert.Equal(-Vector3.UnitZ, a[30].Normal);
        Assert.Equal(new Vector2(.125f, .75f), a[0].Uv);
        Assert.Equal(new Vector2(.625f, .75f), a[1].Uv);
        Assert.Equal(new Vector2(.625f, .25f), a[2].Uv);
        Assert.Equal(new Vector2(0, 1), a[36].Uv);
    }

    [Fact]
    public void RotatedAndScaledGeometryStaysWithinSharedHitbox()
    {
        var obj = ObjectDefinition.Parse(Cuboid);
        var cell = new ObjectCell(obj.Id, BlockFace.Front,
            TextureRotation.Degrees90,
            ObjectTransform.FromParts(new Vector3(.08f, .05f, .03f),
                new Vector3(1.25f, 1.1f, .95f)));
        var pose = WorldObjectGeometry.Pose(
            new WorldVoxelCoord(3, 6, 4), cell, obj);
        var bounds = WorldObjectGeometry.TargetAabb(pose, obj.Target);
        foreach (var vertex in ObjectCuboidMesh.Build(obj.Visual))
        {
            var world = pose.ToWorld(vertex.Position);
            Assert.InRange(world.X, bounds.Minimum.X - .00001f, bounds.Maximum.X + .00001f);
            Assert.InRange(world.Y, bounds.Minimum.Y - .00001f, bounds.Maximum.Y + .00001f);
            Assert.InRange(world.Z, bounds.Minimum.Z - .00001f, bounds.Maximum.Z + .00001f);
        }

        var center = pose.ToWorld(obj.Target.CenterOffset);
        Assert.True(WorldObjectGeometry.TryHitTarget(pose, obj.Target,
            center + new Vector3(2, 0, 0), -Vector3.UnitX, 3, out _));
        Assert.True(PlayerHeldVisualLayout.ObjectPose(obj).Scale > 0);
        Assert.False(PlayerHeldVisualLayout.ObjectPose(obj).FaceCamera);
    }

    [Theory]
    [InlineData("\"parts\":[]")]
    [InlineData("\"parts\":{}")]
    [InlineData("\"parts\":[{\"from\":[0,0,0],\"to\":[0,1,1],\"uv\":[0,0,1,1]}]")]
    [InlineData("\"parts\":[{\"from\":[-5,0,0],\"to\":[0,1,1]}]")]
    [InlineData("\"parts\":[{\"from\":[0,0,0],\"to\":[1,1,1],\"uv\":[0.8,0,0.5,1]}]")]
    [InlineData("\"parts\":[{\"from\":[0,0,0],\"to\":[1,1,1],\"uv\":[0,0,1]}]")]
    public void RejectsBadParts(string partField)
    {
        var start = Cuboid.IndexOf("\"parts\":", StringComparison.Ordinal);
        var end = Cuboid.LastIndexOf("]}}", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var json = Cuboid[..start] + partField + "}}";
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(json));
    }

    [Fact]
    public void RejectsMoreThan64PartsAndUnsupportedRenderKind()
    {
        var repeated = string.Join(",", Enumerable.Repeat(
            """{"from":[0,0,0],"to":[0.1,0.2,0.3]}""", 65));
        var json = Cuboid[..Cuboid.IndexOf("\"parts\":", StringComparison.Ordinal)] +
            "\"parts\":[" + repeated + "]}}";
        Assert.Throws<FormatException>(() => ObjectDefinition.Parse(json));

        var visual = ObjectDefinition.Parse(Cuboid).Visual;
        Assert.Throws<ArgumentException>(() =>
            ObjectCuboidMesh.Build(visual with { Kind = ObjectVisualKind.CrossedSprite }));
    }
}
