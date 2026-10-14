using System.Buffers.Binary;
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
            var vertices = batch.Vertices;
            if (vertices.Length == 0)
                continue;

            // One packed upload per attribute instead of calling SurfaceTool
            // setters six times per vertex across the managed/native boundary.
            // CUSTOM0 retains the previous RGBA8 UNORM voxel-light encoding.
            var positions = new Vector3[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var uv2s = new Vector2[vertices.Length];
            var light = new byte[vertices.Length * 4];

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
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.TexUV2] = uv2s;
            arrays[(int)Mesh.ArrayType.Custom0] = light;

            var surfaceIndex = mesh.GetSurfaceCount();
            // CUSTOM0 format 0 is RGBA8 UNORM, matching SurfaceTool's
            // previous CustomFormat.Rgba8Unorm declaration.
            mesh.AddSurfaceFromArrays(
                Mesh.PrimitiveType.Triangles, arrays,
                flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
            mesh.SurfaceSetMaterial(
                surfaceIndex, materials.Get(batch.Batch));
        }

        return mesh;
    }


    /// <summary>
    /// Updates the existing mesh's attribute buffer without touching its
    /// vertices, normals, surface count, materials, or collision. Godot stores
    /// COLOR / UV / UV2 / CUSTOM0 interleaved, so a partial 4-byte CUSTOM0
    /// upload per vertex would cause thousands of engine boundary calls.
    /// Instead write one complete attribute region per surface.
    /// </summary>
    public static bool TryUpdateLighting(ArrayMesh mesh, ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);

        var batches = data.RenderBatches
            .Where(batch => batch.Vertices.Length > 0).ToArray();
        if (mesh.GetSurfaceCount() != batches.Length)
            return false;

        // Validate every surface before mutating any GPU buffer.
        var updates = new byte[batches.Length][];
        for (var surface = 0; surface < batches.Length; surface++)
        {
            var vertices = batches[surface].Vertices;
            if (mesh.SurfaceGetArrayLen(surface) != vertices.Length)
                return false;

            var format = mesh.SurfaceGetFormat(surface);
            const long requiredChannels = 8L | 16L | 32L | 64L;
            // CUSTOM0 must be RGBA8_UNORM, matching CreateMesh.
            if (((long)format & requiredChannels) != requiredChannels ||
                (((long)format >> 13) & 7L) != 0L)
                return false;

            var serverFormat = (RenderingServer.ArrayFormat)(long)format;
            var stride = checked((int)RenderingServer
                .MeshSurfaceGetFormatAttributeStride(serverFormat, vertices.Length));
            var colorOffset = checked((int)RenderingServer
                .MeshSurfaceGetFormatOffset(serverFormat, vertices.Length,
                    (int)Mesh.ArrayType.Color));
            var uvOffset = checked((int)RenderingServer
                .MeshSurfaceGetFormatOffset(serverFormat, vertices.Length,
                    (int)Mesh.ArrayType.TexUV));
            var uv2Offset = checked((int)RenderingServer
                .MeshSurfaceGetFormatOffset(serverFormat, vertices.Length,
                    (int)Mesh.ArrayType.TexUV2));
            var lightOffset = checked((int)RenderingServer
                .MeshSurfaceGetFormatOffset(serverFormat, vertices.Length,
                    (int)Mesh.ArrayType.Custom0));

            if (stride < 24 ||
                colorOffset < 0 || colorOffset + 4 > stride ||
                uvOffset < 0 || uvOffset + 8 > stride ||
                uv2Offset < 0 || uv2Offset + 8 > stride ||
                lightOffset < 0 || lightOffset + 4 > stride)
                return false;

            var buffer = new byte[checked(vertices.Length * stride)];
            for (var index = 0; index < vertices.Length; index++)
            {
                var v = vertices[index];
                var start = index * stride;
                // Godot packs ARRAY_COLOR in four UNORM bytes.
                buffer[start + colorOffset] = PackColor(v.TintAndAo.X);
                buffer[start + colorOffset + 1] = PackColor(v.TintAndAo.Y);
                buffer[start + colorOffset + 2] = PackColor(v.TintAndAo.Z);
                buffer[start + colorOffset + 3] = PackColor(v.TintAndAo.W);
                WriteFloat(buffer, start + uvOffset, v.Uv.X);
                WriteFloat(buffer, start + uvOffset + 4, v.Uv.Y);
                WriteFloat(buffer, start + uv2Offset, v.EncodedTextureLayers.X);
                WriteFloat(buffer, start + uv2Offset + 4, v.EncodedTextureLayers.Y);
                buffer[start + lightOffset] = PackUnorm8(v.VoxelLight.X);
                buffer[start + lightOffset + 1] = PackUnorm8(v.VoxelLight.Y);
                buffer[start + lightOffset + 2] = PackUnorm8(v.VoxelLight.Z);
                buffer[start + lightOffset + 3] = PackUnorm8(v.VoxelLight.W);
            }
            updates[surface] = buffer;
        }

        for (var surface = 0; surface < updates.Length; surface++)
            mesh.SurfaceUpdateAttributeRegion(surface, 0, updates[surface]);
        return true;
    }

    private static byte PackColor(float value) =>
        (byte)Math.Clamp((int)(value * 255f), 0, 255);

    private static void WriteFloat(byte[] data, int index, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(index, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static byte PackUnorm8(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

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
