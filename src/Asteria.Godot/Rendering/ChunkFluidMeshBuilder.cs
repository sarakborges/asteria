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
        var light = new byte[vertices.Count * 4];

        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            positions[index] = new Vector3(
                vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            normals[index] = new Vector3(
                vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
            uvs[index] = new Vector2(vertex.Uv.X, vertex.Uv.Y);
            colors[index] = new Color(
                vertex.TintAndAo.X, vertex.TintAndAo.Y,
                vertex.TintAndAo.Z, vertex.TintAndAo.W);
            var offset = index * 4;
            light[offset] = PackUnorm8(vertex.VoxelLight.X);
            light[offset + 1] = PackUnorm8(vertex.VoxelLight.Y);
            light[offset + 2] = PackUnorm8(vertex.VoxelLight.Z);
            light[offset + 3] = PackUnorm8(vertex.VoxelLight.W);
        }

        Godot.Collections.Array arrays = [];
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Custom0] = light;

        var surfaceIndex = mesh.GetSurfaceCount();
        mesh.AddSurfaceFromArrays(
            Mesh.PrimitiveType.Triangles, arrays,
            flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
        mesh.SurfaceSetMaterial(surfaceIndex, materials.Get(fluid));
        return true;
    }

    public static bool TryUpdateLighting(ArrayMesh mesh, ChunkFluidMeshData data) =>
        MeshLightAttributePatcher.TryUpdate(mesh, data);

    public static bool TryUpdateLighting(
        ArrayMesh mesh, ChunkFluidMeshData data,
        int firstSurface, int surfaceCount) =>
        MeshLightAttributePatcher.TryUpdate(
            mesh, data, firstSurface, surfaceCount);

    public static bool TryUpdateLightingRange(
        ArrayMesh mesh,
        int surfaceIndex,
        int firstVertex,
        int surfaceVertexCount,
        ChunkFluidBatchData batch) =>
        MeshLightAttributePatcher.TryUpdateSurfaceRange(
            mesh, surfaceIndex, firstVertex, surfaceVertexCount, batch);

    private static byte PackUnorm8(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
