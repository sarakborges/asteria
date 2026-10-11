using Asteria.Client.Content;
using Asteria.Core.Content;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client.Rendering;

/// <summary>
/// Imports a selected-pack player visual for both third-person and an
/// offscreen inventory portrait. Mesh UVs and materials are built once per
/// scene instance without creating Godot importer sidecar files.
/// </summary>
internal static class PlayerVisualSceneFactory
{
    public static Node Load(
        PackSelection selection, PlayerVisualDefinition definition)
    {
        var source = ProjectPackFiles.AbsoluteResourcePath(
            selection, definition.Model);
        var bytes = File.ReadAllBytes(source);
        var state = new GltfState();
        var document = new GltfDocument();
        var status = document.AppendFromBuffer(bytes, "", state);
        if (status != Error.Ok)
            throw new InvalidDataException(
                $"Cannot decode authored player GLB: {status}");

        var scene = document.GenerateScene(state)
            ?? throw new InvalidDataException("Authored player GLB has no scene.");
        var skin = ProjectPackFiles.LoadTexture(selection, definition.Skin);
        var cuboids = Descendants(scene)
            .OfType<MeshInstance3D>()
            .Where(mesh => PlayerSkinUvMapper.TryMap(
                mesh.Name.ToString(), NVector3.Zero, NVector3.UnitZ, out _))
            .ToArray();
        var hasAuthoredHairLayer = cuboids.Any(mesh =>
            mesh.Name.ToString() == "HairLayer");
        foreach (var mesh in cuboids)
        {
            ApplySkin(mesh, skin);
            // The original GLB already includes its hair layer. Generate only
            // the remaining jacket/sleeves/trousers from the same authored skin.
            if (mesh.Name.ToString() != "HairLayer" &&
                !(hasAuthoredHairLayer && mesh.Name.ToString() == "HeadMesh") &&
                PlayerSkinUvMapper.TryOuterUvOffset(
                    mesh.Name.ToString(), out var offset))
            {
                var overlay = new MeshInstance3D
                {
                    Name = mesh.Name.ToString() + "Overlay",
                    Mesh = BuildOuterMesh(mesh, offset),
                    Scale = OuterLayerScale(mesh.Name.ToString()),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = MakeSkinMaterial(skin),
                };
                mesh.AddChild(overlay);
            }
        }
        return scene;
    }

    public static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    // Match MineClone's 0.25-pixel outward shell on each side of
    // the canonical 64x64 skin. A single uniform multiplier makes sleeves
    // and jacket thickness inconsistent.
    private static Vector3 OuterLayerScale(string part)
    {
        var size = part == "BodyMesh"
            ? new Vector3(8f, 12f, 4f)
            : new Vector3(4f, 12f, 4f);
        return (size + Vector3.One * 0.5f) / size;
    }

    private static void ApplySkin(MeshInstance3D mesh, Texture2D skin)
    {
        // Keep the canonical face UVs embedded in the GLB. Regenerating them
        // from Godot-imported normals/positions may flip or swap skin faces.
        // The GLB authored alphaMode=MASK for every body part, not just hair.
        mesh.MaterialOverride = MakeSkinMaterial(skin);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static StandardMaterial3D MakeSkinMaterial(Texture2D skin) => new()
    {
        AlbedoTexture = skin,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
        AlphaScissorThreshold = 0.1f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static ArrayMesh BuildOuterMesh(MeshInstance3D mesh, NVector2 uvOffset)
    {
        if (mesh.Mesh is not Mesh imported)
            throw new InvalidDataException(
                $"Player mesh '{mesh.Name}' has no geometry.");

        var mapped = new ArrayMesh();
        for (var surface = 0; surface < imported.GetSurfaceCount(); surface++)
        {
            var arrays = imported.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var authoredUv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            if (vertices.Length == 0 || vertices.Length != authoredUv.Length)
                throw new InvalidDataException(
                    $"Player mesh '{mesh.Name}' has no canonical GLB UVs.");

            // The outer atlas region has the same box layout as the inner
            // region, only translated. This preserves the original imported
            // face orientation and texture-coordinate conventions.
            var offset = new Vector2(uvOffset.X, uvOffset.Y);
            var outerUv = new Vector2[authoredUv.Length];
            for (var i = 0; i < authoredUv.Length; i++)
                outerUv[i] = authoredUv[i] + offset;
            arrays[(int)Mesh.ArrayType.TexUV] = outerUv;
            mapped.AddSurfaceFromArrays(
                imported is ArrayMesh sourceMesh
                    ? sourceMesh.SurfaceGetPrimitiveType(surface)
                    : Mesh.PrimitiveType.Triangles,
                arrays);
        }

        return mapped;
    }
}
