using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ExtrudedSpriteMeshTests
{
    private static readonly ObjectVisualDefinition Visual =
        new(ObjectVisualKind.ExtrudedSprite,
            "textures/objects/seashell.png", .6f, .012f, 1, .0125f, .5f, .4f);

    [Fact]
    public void JoinedPixelsExtrudeOnlyContourNotInternalEdges()
    {
        // Opaque 2x2 square inside a 3x3 sprite: eight perimeter walls
        // plus the top and bottom faces = ten quads.
        byte[] alpha =
        [
            255, 255, 0,
            255, 255, 0,
              0,   0, 0
        ];
        var mesh = ExtrudedSpriteMesh.Build(Visual, 3, 3, alpha);
        Assert.Equal(10 * 6, mesh.Length);
        Assert.Equal(Vector3.UnitY, mesh[0].Normal);
        Assert.Equal(-Vector3.UnitY, mesh[6].Normal);
        Assert.Equal(new Vector2(0, 0), mesh[0].Uv);
        Assert.Equal(new Vector2(1, 1), mesh[2].Uv);
        Assert.Contains(mesh, v => v.Normal == -Vector3.UnitX);
        Assert.Contains(mesh, v => v.Normal == Vector3.UnitX);
        Assert.Contains(mesh, v => v.Normal == -Vector3.UnitZ);
        Assert.Contains(mesh, v => v.Normal == Vector3.UnitZ);

        // Every wall is a real, tiny extrusion, including the authored offset.
        Assert.All(mesh.Skip(12), vertex =>
            Assert.True(vertex.Position.Y == Visual.BaseOffset ||
                MathF.Abs(vertex.Position.Y -
                    (Visual.BaseOffset + Visual.Height)) < .00001f));
    }

    [Fact]
    public void TransparentHolesProduceInsideWallsAndDeterministicMesh()
    {
        byte[] ring =
        [
            255, 255, 255,
            255, 0,   255,
            255, 255, 255
        ];
        var first = ExtrudedSpriteMesh.Build(Visual, 3, 3, ring);
        var second = ExtrudedSpriteMesh.Build(Visual, 3, 3, ring);
        Assert.Equal(18 * 6, first.Length); // twelve outer + four inner walls
        Assert.Equal(first, second);
        Assert.Contains(first, v => v.Position.X < 0f && v.Normal == -Vector3.UnitX);
        Assert.Contains(first, v => v.Position.X > 0f && v.Normal == Vector3.UnitX);
    }

    [Fact]
    public void CutoffAndInvalidImageMasksAreBounded()
    {
        Assert.Equal(6 * 6, ExtrudedSpriteMesh.Build(
            Visual with { AlphaCutoff = .5f }, 2, 1, [128, 127]).Length);
        Assert.Throws<ArgumentException>(() =>
            ExtrudedSpriteMesh.Build(Visual, 2, 2, [255, 255]));
        Assert.Throws<ArgumentException>(() =>
            ExtrudedSpriteMesh.Build(Visual, 2, 1, [0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExtrudedSpriteMesh.Build(Visual, 512, 512, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() =>
            ExtrudedSpriteMesh.Build(
                Visual with { Kind = ObjectVisualKind.CrossedSprite },
                2, 1, [255, 255]));
    }

    [Fact]
    public void FlatMeshBoundsMatchSharedTargetDuringRotationAndScale()
    {
        var obj = ObjectDefinition.Parse("""
            {"id":"asteria:shell","category":"foliage",
             "icon":"textures/objects/seashell.png",
             "visual":{"type":"extrudedSprite",
               "texture":"textures/objects/seashell.png",
               "size":[0.6,0.4],"height":0.012,"baseOffset":0.0125},
             "target":{"size":[0.6,0.14,0.4],"centerOffset":[0,0.07,0]}}
            """);
        var cell = new ObjectCell(obj.Id, BlockFace.Top,
            TextureRotation.Degrees90,
            ObjectTransform.FromParts(new Vector3(.1f, 0, 0),
                new Vector3(1.4f, 1f, 1.2f)));
        var pose = WorldObjectGeometry.Pose(new WorldVoxelCoord(2, 6, 2), cell, obj);
        var extent = WorldObjectGeometry.TargetAabb(pose, obj.Target);
        var verts = ExtrudedSpriteMesh.Build(obj.Visual, 1, 1, [255]);
        Assert.All(verts, vertex =>
        {
            var world = pose.ToWorld(vertex.Position);
            Assert.InRange(world.X, extent.Minimum.X - .0001f, extent.Maximum.X + .0001f);
            Assert.InRange(world.Y, extent.Minimum.Y - .0001f, extent.Maximum.Y + .0001f);
            Assert.InRange(world.Z, extent.Minimum.Z - .0001f, extent.Maximum.Z + .0001f);
        });
        var center = pose.ToWorld(obj.Target.CenterOffset);
        Assert.True(WorldObjectGeometry.TryHitTarget(pose, obj.Target,
            center + new Vector3(0, 2f, 0), -Vector3.UnitY, 3f, out _));
    }

    [Fact]
    public void DefaultPackGroundDecoratorsUseExtrudedObjectsWithoutReplacingWater()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");
        IEnumerable<string> Read(string directory) =>
            Directory.EnumerateFiles(Path.Combine(data, directory), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);

        var blocks = BlockRegistry.FromJson(Read("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            Read("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(Read("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        foreach (var (name, biomeId, submerged) in new[]
        {
            ("seashell", "asteria:overworld/ocean", true),
            ("starfish", "asteria:overworld/ocean", true),
            ("clover", "asteria:overworld/plains", false)
        })
        {
            var id = "asteria:" + name;
            Assert.False(blocks.TryGetId(id, out _));
            var definition = objects.Get(id);
            Assert.Equal(ObjectVisualKind.ExtrudedSprite, definition.Visual.Kind);
            Assert.True(definition.Supports(BlockFace.Top));
            var decorator = Assert.Single(biomes.Get(biomeId).Decorations,
                rule => rule.Block == id);
            Assert.True(decorator.IsObject);
            Assert.Equal(submerged
                    ? DecorationFluidPlacement.Submerged
                    : DecorationFluidPlacement.Dry,
                decorator.FluidPlacement);
        }
    }
}
