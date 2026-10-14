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

        foreach (var batch in data.Batches)
        {
            if (batch.Vertices.Length == 0)
            {
                continue;
            }

            var vertices = batch.Vertices;
            var positions = new Vector3[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var light = new byte[vertices.Length * 4];

            for (var index = 0; index < vertices.Length; index++)
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
            mesh.SurfaceSetMaterial(surfaceIndex, materials.Get(batch.Fluid));
        }

        return mesh;
    }

    private static byte PackUnorm8(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
