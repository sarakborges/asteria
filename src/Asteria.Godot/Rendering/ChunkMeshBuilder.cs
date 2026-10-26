
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
            AppendSurface(mesh, batch.Batch, batch.Vertices, materials);

        return mesh.GetSurfaceCount() - initialSurfaceCount;
    }

    /// <summary>
    /// Publishes one material surface directly from the presentation's retained
    /// vertex list, avoiding a temporary full-size vertex-array copy.
    /// </summary>
    internal static bool AppendSurface(
        ArrayMesh mesh,
        TerrainRenderBatch batch,
        IReadOnlyList<ChunkMeshVertex> vertices,
        VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(materials);
        if (vertices.Count == 0)
            return false;

        var positions = new Vector3[vertices.Count];
        var normals = new Vector3[vertices.Count];
        var colors = new Color[vertices.Count];
        var uvs = new Vector2[vertices.Count];
        var uv2s = new Vector2[vertices.Count];

        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            positions[index] = new Vector3(
                vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            normals[index] = new Vector3(
                vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
            colors[index] = new Color(
                vertex.Tint.X, vertex.Tint.Y,
                vertex.Tint.Z, vertex.Tint.W);
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
        mesh.SurfaceSetMaterial(surfaceIndex, materials.Get(batch));
        return true;
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
