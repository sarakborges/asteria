
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
        AppendSurfaces(mesh, data, materials);
        return mesh;
    }

    /// <summary>
    /// Appends one meshlet's render batches to an existing chunk ArrayMesh.
    /// Each non-empty batch remains a material surface while many meshlets
    /// share one mesh resource and one MeshInstance3D.
    /// </summary>
    public static int AppendSurfaces(
        ArrayMesh mesh,
        ChunkMeshData data,
        VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(materials);

        var initialSurfaceCount = mesh.GetSurfaceCount();
        foreach (var batch in data.RenderBatches)
        {
            var vertices = batch.Vertices;
            if (vertices.Length == 0)
                continue;

            var positions = new Vector3[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var uv2s = new Vector2[vertices.Length];

            for (var index = 0; index < vertices.Length; index++)
            {
                var vertex = vertices[index];
                positions[index] = new Vector3(
                    vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
                normals[index] = new Vector3(
                    vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
                colors[index] = new Color(
                    vertex.TintAndAo.X, vertex.TintAndAo.Y,
                    vertex.TintAndAo.Z, vertex.TintAndAo.W);
                uvs[index] = new Vector2(vertex.Uv.X, vertex.Uv.Y);
                uv2s[index] = new Vector2(
                    vertex.EncodedTextureLayers.X,
                    vertex.EncodedTextureLayers.Y);
            }

            Godot.Collections.Array arrays = [];
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.TexUV2] = uv2s;

            var surfaceIndex = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles, arrays,
                flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
            mesh.SurfaceSetMaterial(
                surfaceIndex, materials.Get(batch.Batch));
        }

        return mesh.GetSurfaceCount() - initialSurfaceCount;
    }


    public static ConcavePolygonShape3D CreateCollisionShape(
        ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CreateCollisionShape(data.CollisionFaces);
    }

    // Deferred collision publication retains only the compact Core faces,
    // not the full render DTO or a premature Godot physics resource.
    public static ConcavePolygonShape3D CreateCollisionShape(
        System.Numerics.Vector3[] collisionFaces)
    {
        ArgumentNullException.ThrowIfNull(collisionFaces);
        var faces = new Vector3[collisionFaces.Length];

        for (var index = 0;
             index < collisionFaces.Length;
             index++)
        {
            var position =
                collisionFaces[index];
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
