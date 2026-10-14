using System.Buffers.Binary;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Patches the Godot attribute buffer without touching vertex positions,
/// normals, primitive topology or surface materials. Godot stores COLOR, UV,
/// optional UV2 and CUSTOM0 interleaved: every patch supplies a complete
/// attribute record rather than thousands of four-byte native calls.
/// </summary>
internal static class MeshLightAttributePatcher
{
    public static bool TryUpdate(ArrayMesh mesh, ChunkMeshData data)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);
        var batches = data.RenderBatches
            .Where(batch => batch.Vertices.Length > 0).ToArray();
        if (mesh.GetSurfaceCount() != batches.Length)
            return false;

        var patches = new byte[batches.Length][];
        for (var surface = 0; surface < batches.Length; surface++)
        {
            var vertices = batches[surface].Vertices;
            if (!TryPrepare(mesh, surface, vertices.Length, hasUv2: true,
                    out var layout))
                return false;
            var bytes = new byte[checked(vertices.Length * layout.Stride)];
            for (var index = 0; index < vertices.Length; index++)
            {
                var v = vertices[index];
                Write(bytes, index, layout, v.TintAndAo, v.Uv,
                    v.EncodedTextureLayers, v.VoxelLight);
            }
            patches[surface] = bytes;
        }

        Publish(mesh, patches);
        return true;
    }

    public static bool TryUpdate(ArrayMesh mesh, ChunkFluidMeshData data)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(data);
        var batches = data.Batches
            .Where(batch => batch.Vertices.Length > 0).ToArray();
        if (mesh.GetSurfaceCount() != batches.Length)
            return false;

        var patches = new byte[batches.Length][];
        for (var surface = 0; surface < batches.Length; surface++)
        {
            var vertices = batches[surface].Vertices;
            if (!TryPrepare(mesh, surface, vertices.Length, hasUv2: false,
                    out var layout))
                return false;
            var bytes = new byte[checked(vertices.Length * layout.Stride)];
            for (var index = 0; index < vertices.Length; index++)
            {
                var v = vertices[index];
                Write(bytes, index, layout, v.TintAndAo, v.Uv,
                    default, v.VoxelLight);
            }
            patches[surface] = bytes;
        }

        Publish(mesh, patches);
        return true;
    }

    private static bool TryPrepare(
        ArrayMesh mesh, int surface, int count, bool hasUv2,
        out AttributeLayout layout)
    {
        layout = default;
        if (mesh.SurfaceGetArrayLen(surface) != count)
            return false;

        var format = mesh.SurfaceGetFormat(surface);
        const long required = 8L | 16L | 64L;
        const long uv2Channel = 32L;
        var bits = (long)format;
        if ((bits & required) != required ||
            ((bits & uv2Channel) != 0) != hasUv2 ||
            ((bits >> 13) & 7L) != 0L)
            return false;

        var native = (RenderingServer.ArrayFormat)bits;
        var stride = checked((int)RenderingServer
            .MeshSurfaceGetFormatAttributeStride(native, count));
        var color = checked((int)RenderingServer
            .MeshSurfaceGetFormatOffset(native, count, (int)Mesh.ArrayType.Color));
        var uv = checked((int)RenderingServer
            .MeshSurfaceGetFormatOffset(native, count, (int)Mesh.ArrayType.TexUV));
        var uv2 = hasUv2 ? checked((int)RenderingServer
            .MeshSurfaceGetFormatOffset(native, count, (int)Mesh.ArrayType.TexUV2)) : 0;
        var light = checked((int)RenderingServer
            .MeshSurfaceGetFormatOffset(native, count, (int)Mesh.ArrayType.Custom0));

        if (stride < (hasUv2 ? 24 : 16) ||
            !Fits(color, 4, stride) || !Fits(uv, 8, stride) ||
            (hasUv2 && !Fits(uv2, 8, stride)) ||
            !Fits(light, 4, stride))
            return false;

        layout = new AttributeLayout(stride, color, uv, uv2, light, hasUv2);
        return true;
    }

    private static bool Fits(int offset, int bytes, int stride) =>
        offset >= 0 && offset <= stride - bytes;

    private static void Publish(ArrayMesh mesh, byte[][] patches)
    {
        for (var surface = 0; surface < patches.Length; surface++)
            mesh.SurfaceUpdateAttributeRegion(surface, 0, patches[surface]);
    }

    private static void Write(
        byte[] bytes, int vertexIndex, AttributeLayout layout,
        Vector4 color, Vector2 uv, Vector2 uv2, Vector4 light)
    {
        var start = vertexIndex * layout.Stride;
        // Vertex COLOR uses 4 x UNORM8 in the Godot attribute buffer.
        bytes[start + layout.Color] = PackColor(color.X);
        bytes[start + layout.Color + 1] = PackColor(color.Y);
        bytes[start + layout.Color + 2] = PackColor(color.Z);
        bytes[start + layout.Color + 3] = PackColor(color.W);
        WriteFloat(bytes, start + layout.Uv, uv.X);
        WriteFloat(bytes, start + layout.Uv + 4, uv.Y);
        if (layout.HasUv2)
        {
            WriteFloat(bytes, start + layout.Uv2, uv2.X);
            WriteFloat(bytes, start + layout.Uv2 + 4, uv2.Y);
        }
        bytes[start + layout.Light] = PackLight(light.X);
        bytes[start + layout.Light + 1] = PackLight(light.Y);
        bytes[start + layout.Light + 2] = PackLight(light.Z);
        bytes[start + layout.Light + 3] = PackLight(light.W);
    }

    private static byte PackColor(float value) =>
        (byte)Math.Clamp((int)(value * 255f), 0, 255);

    private static byte PackLight(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static void WriteFloat(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private readonly record struct AttributeLayout(
        int Stride, int Color, int Uv, int Uv2,
        int Light, bool HasUv2);
}
