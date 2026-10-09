using Asteria.Client.Content;
using Asteria.Core.Content;
using Godot;
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
        foreach (var mesh in cuboids)
        {
            ApplySkin(mesh, skin, outer: false);
            // The original GLB already includes its hair layer. Generate only
            // the remaining jacket/sleeves/trousers from the same authored skin.
            if (mesh.Name.ToString() != "HairLayer" &&
                PlayerSkinUvMapper.TryMapOuter(
                    mesh.Name.ToString(), NVector3.Zero, NVector3.UnitZ, out _))
            {
                var overlay = new MeshInstance3D
                {
                    Name = mesh.Name + "Overlay",
                    Mesh = BuildSkinnedMesh(mesh, outer: true),
                    Scale = Vector3.One * 1.045f,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = MakeSkinMaterial(skin, transparent: true),
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

    private static void ApplySkin(MeshInstance3D mesh, Texture2D skin, bool outer)
    {
        mesh.Mesh = BuildSkinnedMesh(mesh, outer);
        mesh.MaterialOverride = MakeSkinMaterial(skin, transparent: false);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static StandardMaterial3D MakeSkinMaterial(
        Texture2D skin, bool transparent) => new()
    {
        AlbedoTexture = skin,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        Transparency = transparent
            ? BaseMaterial3D.TransparencyEnum.AlphaScissor
            : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static ArrayMesh BuildSkinnedMesh(MeshInstance3D mesh, bool outer)
    {
        if (mesh.Mesh is not Mesh imported)
            throw new InvalidDataException(
                $"Player mesh '{mesh.Name}' has no geometry.");

        var mapped = new ArrayMesh();
        for (var surface = 0; surface < imported.GetSurfaceCount(); surface++)
        {
            var arrays = imported.SurfaceGetArrays(surface);
            var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (positions.Length == 0 || positions.Length != normals.Length)
                throw new InvalidDataException(
                    $"Player skin geometry '{mesh.Name}' has invalid normals.");

            var uv = new Vector2[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                var mapped = outer
                    ? PlayerSkinUvMapper.TryMapOuter(
                        mesh.Name.ToString(),
                        new NVector3(positions[i].X, positions[i].Y, positions[i].Z),
                        new NVector3(normals[i].X, normals[i].Y, normals[i].Z),
                        out var mappedUvOuter)
                    : PlayerSkinUvMapper.TryMap(
                        mesh.Name.ToString(),
                        new NVector3(positions[i].X, positions[i].Y, positions[i].Z),
                        new NVector3(normals[i].X, normals[i].Y, normals[i].Z),
                        out mappedUvOuter);
                var mappedUv = mappedUvOuter;
                if (!mapped)
                    throw new InvalidDataException(
                        $"Cannot map player skin UV for '{mesh.Name}'.");
                uv[i] = new Vector2(mappedUv.X, mappedUv.Y);
            }
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
            mapped.AddSurfaceFromArrays(
                imported is ArrayMesh sourceMesh
                    ? sourceMesh.SurfaceGetPrimitiveType(surface)
                    : Mesh.PrimitiveType.Triangles,
                arrays);
        }

        return mapped;
    }
}
