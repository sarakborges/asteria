using Asteria.Client.Content;
using Asteria.Core.Content;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Bakes static authored GLB nodes into one multi-surface ArrayMesh. All
/// imported submeshes retain their materials and node-local transforms, while
/// chunks keep their existing one-MultiMesh-per-Object-ID draw path.
/// No pack assets pass through Godot's on-disk import/resource scanner.
/// </summary>
internal static class WorldObjectModelMesh
{
    private const int MaximumModelBytes = 16 * 1024 * 1024;
    private const int MaximumSurfaces = 128;
    private const int MaximumSceneNodes = 512;
    private const int MaximumTriangleIndices = 786_432;
    private const int MaximumVertices = 262_144;
    private static Texture2D? _whiteTexture;

    public static Mesh Load(
        PackSelection pack, ObjectDefinition definition)
    {
        if (definition.Visual.Kind != ObjectVisualKind.Model ||
            definition.Visual.ModelPath is not { } modelPath)
            throw new ArgumentException("Requires authored Object model.", nameof(definition));

        var path = ProjectPackFiles.AbsoluteResourcePath(pack, modelPath);
        var bytes = new FileInfo(path).Length;
        if (bytes is < 1 or > MaximumModelBytes)
            throw new InvalidDataException(
                $"Object {definition.Id} GLB exceeds the 16 MiB resource limit.");

        var document = new GltfDocument();
        var state = new GltfState();
        var result = document.AppendFromBuffer(File.ReadAllBytes(path), "", state);
        if (result != Error.Ok)
            throw new InvalidDataException(
                $"Object {definition.Id} GLB could not be decoded: {result}");

        var scene = document.GenerateScene(state)
            ?? throw new InvalidDataException(
                $"Object {definition.Id} GLB contains no scene.");
        try
        {
            var mesh = new ArrayMesh();
            var surfaces = 0;
            var vertices = 0;
            var indices = 0;
            var nodes = 0;
            void Visit(Node node, Transform3D parent)
            {
                if (++nodes > MaximumSceneNodes)
                    throw new InvalidDataException(
                        $"Object {definition.Id} GLB exceeds the scene-node limit.");
                var transform = node is Node3D spatial
                    ? parent * spatial.Transform : parent;
                if (node is MeshInstance3D instance && instance.Mesh is { } source)
                {
                    for (var i = 0; i < source.GetSurfaceCount(); i++)
                    {
                        if (source is not ArrayMesh arraySource ||
                            arraySource.SurfaceGetPrimitiveType(i) != Mesh.PrimitiveType.Triangles)
                            throw new InvalidDataException(
                                $"Object {definition.Id} GLB requires triangle primitives.");
                        var arrays = source.SurfaceGetArrays(i);
                        var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                        var surfaceIndices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                        var indexCount = surfaceIndices.Length == 0
                            ? points.Length : surfaceIndices.Length;
                        if (points.Length == 0 || points.Length != normals.Length ||
                            indexCount % 3 != 0 ||
                            ++surfaces > MaximumSurfaces ||
                            (vertices += points.Length) > MaximumVertices ||
                            (indices += indexCount) > MaximumTriangleIndices)
                            throw new InvalidDataException(
                                $"Object {definition.Id} GLB exceeds bounded mesh surfaces/vertices.");

                        var normalBasis = transform.Basis.Inverse().Transposed();
                        var half = definition.Target.Size * .5f;
                        var center = definition.Target.CenterOffset;
                        for (var v = 0; v < points.Length; v++)
                        {
                            var point = transform * points[v];
                            if (!Finite(point) ||
                                MathF.Abs(point.X - center.X) > half.X + .001f ||
                                MathF.Abs(point.Y - center.Y) > half.Y + .001f ||
                                MathF.Abs(point.Z - center.Z) > half.Z + .001f)
                                throw new InvalidDataException(
                                    $"Object {definition.Id} GLB geometry exceeds its authored target bounds.");
                            points[v] = point;
                            var transformedNormal = normalBasis * normals[v];
                            if (!Finite(transformedNormal) ||
                                transformedNormal.LengthSquared() < .000001f)
                                throw new InvalidDataException(
                                    $"Object {definition.Id} GLB has invalid normals.");
                            normals[v] = transformedNormal.Normalized();
                        }
                        arrays[(int)Mesh.ArrayType.Vertex] = points;
                        arrays[(int)Mesh.ArrayType.Normal] = normals;
                        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                        var authoredMaterial = instance.MaterialOverride ??
                            instance.GetSurfaceOverrideMaterial(i) ??
                            source.SurfaceGetMaterial(i);
                        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1,
                            AdaptMaterial(authoredMaterial, definition));
                    }
                }
                foreach (var child in node.GetChildren())
                    Visit(child, transform);
            }
            Visit(scene, Transform3D.Identity);
            if (surfaces == 0)
                throw new InvalidDataException(
                    $"Object {definition.Id} GLB contains no static mesh surfaces.");
            return mesh;
        }
        finally
        {
            scene.Free();
        }
    }

    private static Material AdaptMaterial(Material? imported, ObjectDefinition obj)
    {
        if (imported is not null and not StandardMaterial3D)
            throw new InvalidDataException(
                $"Object {obj.Id} GLB uses unsupported material {imported.GetType().Name}.");
        var source = imported as StandardMaterial3D;
        if (source is not null &&
            source.Transparency == BaseMaterial3D.TransparencyEnum.Alpha)
            throw new InvalidDataException(
                $"Object {obj.Id} GLB uses alpha blend; static Object materials require opaque or cutout.");

        var texture = source?.AlbedoTexture ?? WhiteTexture();
        var albedo = source?.AlbedoColor ?? Colors.White;
        // One shared material policy for imported GLB surfaces and Objects
        // authored as sprites/cuboids. Keep imported base color/alpha.
        return obj.Unlit
            ? WorldObjectMaterials.CreateUnlit(
                obj, texture, source?.AlphaScissorThreshold ?? .5f,
                albedo, preserveVertexColors: true)
            : WorldObjectMaterials.CreateLit(
                obj, texture, source?.AlphaScissorThreshold ?? .5f,
                albedo);
    }

    private static Texture2D WhiteTexture()
    {
        if (_whiteTexture is not null) return _whiteTexture;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return _whiteTexture = ImageTexture.CreateFromImage(image)
            ?? throw new InvalidOperationException("Cannot allocate white model texture.");
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
