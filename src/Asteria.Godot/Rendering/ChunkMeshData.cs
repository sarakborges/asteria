using System.Numerics;

namespace Asteria.Client.Rendering;

public readonly record struct ChunkMeshVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 Uv,
    Vector2 EncodedTextureLayers,
    Vector4 TintAndAo,
    Vector4 VoxelLight);

public sealed record ChunkMeshData(ChunkMeshVertex[] Vertices)
{
    public int TriangleCount => Vertices.Length / 3;
}
