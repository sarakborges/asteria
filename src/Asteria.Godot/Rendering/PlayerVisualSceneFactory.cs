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
        var skinSize = skin.GetSize();
        if (skinSize.X != skinSize.Y ||
            skinSize.X is not (64 or 128 or 256 or 512))
            throw new InvalidDataException(
                $"Player skin '{definition.Skin}' must be a square 64/128/256/512 atlas.");
        var cuboids = Descendants(scene)
            .OfType<MeshInstance3D>()
            .Where(mesh => PlayerSkinUvMapper.TryMap(
                mesh.Name.ToString(), NVector3.Zero, NVector3.UnitZ, out _))
            .ToArray();
        // A missing limb or duplicate imported mesh must not silently
        // present an incomplete/mismatched skin for selected packs.
        var names = cuboids.Select(mesh => mesh.Name.ToString()).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Player GLB repeats named body meshes.");
        foreach (var required in new[] {
                     "HeadMesh", "BodyMesh",
                     "RightArmMesh", "LeftArmMesh",
                     "RightLegMesh", "LeftLegMesh"
                 })
            if (!names.Contains(required, StringComparer.Ordinal))
                throw new InvalidDataException(
                    $"Player GLB is missing skin cuboid '{required}'.");

        var hasAuthoredHairLayer = cuboids.Any(mesh =>
            mesh.Name.ToString() == "HairLayer");
        foreach (var mesh in cuboids)
        {
            var name = mesh.Name.ToString();
            var sourceMesh = mesh.Mesh ?? throw new InvalidDataException(
                $"Player cuboid {name} has no imported geometry.");
            // UV remapping rebuilds the mesh and discards its imported surface
            // material. Capture the glTF face/alpha policy before doing so.
            var sourceMaterial = mesh.GetActiveMaterial(0) as BaseMaterial3D
                ?? throw new InvalidDataException(
                    $"Player cuboid {name} has no glTF skin material.");

            // MineClone owns UVs by named cuboid, local position, and
            // normal. Never trust imported GLB UVs: importers and selected-pack
            // GLBs may reorder or flip them without changing geometry.
            mesh.Mesh = RemapSkinMesh(sourceMesh, name, outer: false);
            ApplySkin(mesh, skin, sourceMaterial);

            // The GLB includes an independent hair geometry already.
            // The jacket, sleeves, and trousers are expanded cuboids with
            // their own distinct canonical skin regions.
            if (name != "HairLayer" &&
                !(hasAuthoredHairLayer && name == "HeadMesh") &&
                PlayerSkinUvMapper.TryOuterUvOffset(name, out _))
            {
                var overlay = new MeshInstance3D
                {
                    Name = name + "Overlay",
                    Mesh = RemapSkinMesh(sourceMesh, name, outer: true),
                    Scale = OuterLayerScale(name),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    MaterialOverride = MakeSkinMaterial(skin, sourceMaterial),
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

    private static void ApplySkin(
        MeshInstance3D mesh, Texture2D skin, BaseMaterial3D authoredMaterial)
    {
        // UVs are already rebuilt from canonical cuboid geometry. Replace
        // only the skin image and filtering, not the glTF's alpha/cull policy.
        mesh.MaterialOverride = MakeSkinMaterial(skin, authoredMaterial);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private static StandardMaterial3D MakeSkinMaterial(
        Texture2D skin, BaseMaterial3D authoredMaterial) => new()
    {
        AlbedoTexture = skin,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        // MineClone preserves glTF alphaMode=MASK, its default 0.5 cutoff,
        // and doubleSided=true. Preserve selected-pack material semantics
        // for the base skin AND its generated jacket/sleeve/trouser shells.
        Transparency = authoredMaterial.Transparency,
        AlphaScissorThreshold = authoredMaterial.AlphaScissorThreshold,
        CullMode = authoredMaterial.CullMode,
    };

    /// <summary>Identical mapping to MineClone's SkinPartSpec and SkinBoxUv:
    /// rebuilt per surface from canonical cube-space positions and normals.
    /// The source atlas UVs are deliberately ignored for both layers.</summary>
    private static ArrayMesh RemapSkinMesh(Mesh imported, string part, bool outer)
    {
        var mapped = new ArrayMesh();
        if (imported.GetSurfaceCount() == 0)
            throw new InvalidDataException($"Player mesh '{part}' has no surfaces.");

        for (var surface = 0; surface < imported.GetSurfaceCount(); surface++)
        {
            var arrays = imported.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (vertices.Length == 0 || vertices.Length != normals.Length)
                throw new InvalidDataException(
                    $"Player mesh '{part}' has mismatched positions/normals.");

            var skinUvs = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                var normal = normals[i];
                var point = new NVector3(vertex.X, vertex.Y, vertex.Z);
                var direction = new NVector3(normal.X, normal.Y, normal.Z);
                NVector2 uv;
                var success = outer
                    ? PlayerSkinUvMapper.TryMapOuter(part, point, direction, out uv)
                    : PlayerSkinUvMapper.TryMap(part, point, direction, out uv);
                if (!success)
                    throw new InvalidDataException(
                        $"Player cuboid '{part}' contains a noncanonical face at vertex {i}.");
                skinUvs[i] = new Vector2(uv.X, uv.Y);
            }

            arrays[(int)Mesh.ArrayType.TexUV] = skinUvs;
            mapped.AddSurfaceFromArrays(
                imported is ArrayMesh sourceMesh
                    ? sourceMesh.SurfaceGetPrimitiveType(surface)
                    : Mesh.PrimitiveType.Triangles,
                arrays);
        }
        return mapped;
    }

}
