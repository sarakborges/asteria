using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkMeshBuilder
{
    public static ArrayMesh CreateMesh(
        ChunkMeshData data,
        Material material)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(material);

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetCustomFormat(0, SurfaceTool.CustomFormat.Rgba8Unorm);

        foreach (var vertex in data.Vertices)
        {
            surface.SetNormal(new Vector3(
                vertex.Normal.X,
                vertex.Normal.Y,
                vertex.Normal.Z));
            surface.SetColor(new Color(
                vertex.TintAndAo.X,
                vertex.TintAndAo.Y,
                vertex.TintAndAo.Z,
                vertex.TintAndAo.W));
            surface.SetCustom(
                0,
                new Color(
                    vertex.VoxelLight.X,
                    vertex.VoxelLight.Y,
                    vertex.VoxelLight.Z,
                    vertex.VoxelLight.W));
            surface.SetUV(new Vector2(vertex.Uv.X, vertex.Uv.Y));
            surface.SetUV2(new Vector2(
                vertex.EncodedTextureLayers.X,
                vertex.EncodedTextureLayers.Y));
            surface.AddVertex(new Vector3(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z));
        }

        var mesh = surface.Commit();
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    public static ConcavePolygonShape3D CreateCollisionShape(
        ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var faces = new PackedVector3Array();
        faces.Resize(data.Vertices.Length);

        for (var index = 0; index < data.Vertices.Length; index++)
        {
            var position = data.Vertices[index].Position;
            faces[index] = new Vector3(position.X, position.Y, position.Z);
        }

        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(faces);
        return shape;
    }
}
