using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public static class ChunkFluidMeshBuilder
{
    public static ArrayMesh CreateMesh(
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(materials);
        var mesh = new ArrayMesh();
        AppendSurfaces(mesh, data, materials);
        return mesh;
    }

    /// <summary>Appends a fluid meshlet into the owning chunk's shared mesh.</summary>
    public static int AppendSurfaces(
        ArrayMesh mesh,
        ChunkFluidMeshData data,
        FluidMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(materials);

        var initialSurfaceCount = mesh.GetSurfaceCount();
        foreach (var batch in data.Batches)
            AppendSurface(mesh, batch.Fluid, batch.Vertices, materials);

        return mesh.GetSurfaceCount() - initialSurfaceCount;
    }

    /// <summary>Publishes a retained fluid surface without an intermediate vertex-array copy.</summary>
    internal static bool AppendSurface(
        ArrayMesh mesh,
        FluidRuntimeId fluid,
        IReadOnlyList<FluidMeshVertex> vertices,
        FluidMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(materials);
        if (vertices.Count == 0)
            return false;

        var positions = new Vector3[vertices.Count];
        var normals = new Vector3[vertices.Count];
        var uvs = new Vector2[vertices.Count];
        var colors = new Color[vertices.Count];

        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            positions[index] = new Vector3(
                vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            normals[index] = new Vector3(
                vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
            uvs[index] = new Vector2(vertex.Uv.X, vertex.Uv.Y);
            colors[index] = new Color(
                vertex.Tint.X, vertex.Tint.Y,
                vertex.Tint.Z, vertex.Tint.W);
        }

        Godot.Collections.Array arrays = [];
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;

        var surfaceIndex = mesh.GetSurfaceCount();
        mesh.AddSurfaceFromArrays(
            Mesh.PrimitiveType.Triangles, arrays,
            flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
        mesh.SurfaceSetMaterial(surfaceIndex, materials.Get(fluid));
        return true;
    }


}
