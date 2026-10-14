using Asteria.Client.Content;
using Asteria.Client.Rendering;
using Asteria.Core.Content;
using Godot;
using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Tests;

/// <summary>
/// Native C# importer smoke: the ordinary raw-GLB test doesn't exercise the
/// exact UV-mapped, alpha-scissored skin material used by all player views.
/// This is run by the headless Godot smoke without instantiating WRY.
/// </summary>
public partial class PlayerSkinFactorySmoke : Node
{
    public bool Verify()
    {
        try
        {
            var selection = PackSelection.Default;
            var path = ProjectSettings.GlobalizePath(
                "res://packs/default/data/entities/player.json");
            var definition = PlayerVisualDefinition.Parse(File.ReadAllText(path));
            var scene = PlayerVisualSceneFactory.Load(selection, definition);
            try
            {
                var parts = PlayerVisualSceneFactory.Descendants(scene)
                    .OfType<MeshInstance3D>()
                    .Where(node => PlayerSkinUvMapper.TryMap(
                        node.Name.ToString(), NVector3.Zero, NVector3.UnitZ, out _))
                    .ToArray();
                var expected = new[]
                {
                    "HeadMesh", "HairLayer", "BodyMesh", "RightArmMesh",
                    "LeftArmMesh", "RightLegMesh", "LeftLegMesh"
                };
                foreach (var name in expected)
                {
                    var part = parts.Single(node => node.Name.ToString() == name);
                    CheckPart(part, name);
                }
                if (parts.Length != expected.Length)
                    throw new InvalidDataException("Unexpected player skin cuboids.");

                GD.Print("Native player skin factory smoke: " +
                    string.Join(", ", expected));
                return true;
            }
            finally
            {
                scene.Free();
            }
        }
        catch (Exception exception)
        {
            GD.PushError("Native player skin factory smoke failed: " + exception);
            return false;
        }
    }

    private static void CheckPart(MeshInstance3D part, string name)
    {
        if (part.MaterialOverride is not StandardMaterial3D material ||
            material.AlbedoTexture is not ImageTexture texture ||
            material.Transparency != BaseMaterial3D.TransparencyEnum.AlphaScissor)
            throw new InvalidDataException(
                $"Player skin material is not applied to {name}.");

        var image = texture.GetImage();
        if (image.GetSize() != new Vector2I(64, 64))
            throw new InvalidDataException(
                $"Selected default skin must be 64×64: {name}.");

        var mesh = part.Mesh ??
            throw new InvalidDataException($"Player mesh is missing: {name}.");
        if (mesh.GetSurfaceCount() == 0)
            throw new InvalidDataException($"Player mesh has no surfaces: {name}");

        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            if (vertices.Length == 0 || vertices.Length != normals.Length ||
                vertices.Length != uv.Length)
                throw new InvalidDataException(
                    $"Skin atlas UVs were lost on {name}, surface {surface}.");

            for (var index = 0; index < vertices.Length; index++)
            {
                var point = vertices[index];
                var face = normals[index];
                if (!PlayerSkinUvMapper.TryMap(name,
                    new NVector3(point.X, point.Y, point.Z),
                    new NVector3(face.X, face.Y, face.Z), out var expectedUv) ||
                    (uv[index] - new Godot.Vector2(expectedUv.X, expectedUv.Y))
                    .LengthSquared() > 0.000001f)
                    throw new InvalidDataException(
                        $"Incorrect imported UV on {name}, vertex {index}.");
            }
        }

        // Validate opaque texels on every canonical base face, not only that
        // a valid PNG exists. A transparent, mis-addressed texture is invisible.
        var frontCenter = name switch
        {
            "HeadMesh" => new Vector2I(12, 12),
            "HairLayer" => new Vector2I(44, 12),
            "BodyMesh" => new Vector2I(24, 26),
            "RightArmMesh" => new Vector2I(46, 26),
            "LeftArmMesh" => new Vector2I(54, 54),
            "RightLegMesh" => new Vector2I(6, 26),
            "LeftLegMesh" => new Vector2I(22, 54),
            _ => throw new InvalidDataException("Unknown player cuboid."),
        };
        // Hair overlays may intentionally be transparent at their midpoint.
        if (name != "HairLayer" &&
            image.GetPixel(frontCenter.X, frontCenter.Y).A < 0.1f)
            throw new InvalidDataException(
                $"Default skin atlas contains a transparent body: {name}.");
    }
}
