using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public readonly record struct TerrainRenderBatch(
    BlockRenderMode RenderMode,
    bool CastsShadow) : IComparable<TerrainRenderBatch>
{
    public int CompareTo(TerrainRenderBatch other)
    {
        var mode = RenderMode.CompareTo(other.RenderMode);
        return mode != 0
            ? mode
            : other.CastsShadow.CompareTo(CastsShadow);
    }
}

public readonly record struct ChunkMeshVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 Uv,
    Vector2 EncodedTextureLayers,
    Vector4 TintAndAo,
    Vector4 VoxelLight);

public sealed record ChunkRenderBatchData(
    TerrainRenderBatch Batch,
    ChunkMeshVertex[] Vertices)
{
    public int TriangleCount => Vertices.Length / 3;
}

public sealed record ChunkMeshData(
    ChunkRenderBatchData[] RenderBatches,
    Vector3[] CollisionFaces)
{
    public int TriangleCount =>
        RenderBatches.Sum(batch => batch.TriangleCount);

    public int RenderVertexCount =>
        RenderBatches.Sum(batch => batch.Vertices.Length);

    public int CollisionTriangleCount =>
        CollisionFaces.Length / 3;

    public bool HasRenderGeometry =>
        RenderBatches.Any(batch => batch.Vertices.Length > 0);

    public bool HasCollision =>
        CollisionFaces.Length > 0;
}
