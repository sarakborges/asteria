using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkMeshBuilder
{
    public static ArrayMesh CreateMesh(
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(materials);

        var mesh = new ArrayMesh();

        foreach (var batch in data.RenderBatches)
        {
            if (batch.Vertices.Length == 0)
            {
                continue;
            }

            var surfaceIndex = mesh.GetSurfaceCount();
            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetCustomFormat(
                0,
                SurfaceTool.CustomFormat.Rgba8Unorm);

            foreach (var vertex in batch.Vertices)
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
                surface.SetUV(new Vector2(
                    vertex.Uv.X,
                    vertex.Uv.Y));
                surface.SetUV2(new Vector2(
                    vertex.EncodedTextureLayers.X,
                    vertex.EncodedTextureLayers.Y));
                surface.AddVertex(new Vector3(
                    vertex.Position.X,
                    vertex.Position.Y,
                    vertex.Position.Z));
            }

            surface.Commit(mesh);
            mesh.SurfaceSetMaterial(
                surfaceIndex,
                materials.Get(batch.Batch));
        }

        return mesh;
    }

    public static ConcavePolygonShape3D CreateCollisionShape(
        ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var faces =
            new Vector3[data.CollisionFaces.Length];

        for (var index = 0;
             index < data.CollisionFaces.Length;
             index++)
        {
            var position =
                data.CollisionFaces[index];
            faces[index] = new Vector3(
                position.X,
                position.Y,
                position.Z);
        }

        var shape =
            new ConcavePolygonShape3D();
        shape.SetFaces(faces);
        return shape;
    }
}
