using System.Numerics;

namespace Asteria.Core.World;

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

// Four packed bytes instead of a 20+ byte source tuple per vertex.
// Chunk-local x/y/z each occupy four bits, face three, corner two.
// All GPU-irrelevant metadata stays engine-agnostic in Core.
public readonly record struct ChunkMeshLightSample
{
    private readonly uint _packed;

    public ChunkMeshLightSample(
        LocalVoxelCoord source, BlockFace face, byte corner)
    {
        if ((uint)source.X >= Chunk.Size ||
            (uint)source.Y >= Chunk.Size ||
            (uint)source.Z >= Chunk.Size)
            throw new ArgumentOutOfRangeException(nameof(source));
        if ((uint)face > (uint)BlockFace.Back)
            throw new ArgumentOutOfRangeException(nameof(face));
        if (corner > 3)
            throw new ArgumentOutOfRangeException(nameof(corner));

        _packed = (uint)source.X |
                  ((uint)source.Y << 4) |
                  ((uint)source.Z << 8) |
                  ((uint)face << 12) |
                  ((uint)corner << 15);
    }

    public LocalVoxelCoord Source => new(
        (int)(_packed & 0x0f),
        (int)((_packed >> 4) & 0x0f),
        (int)((_packed >> 8) & 0x0f));

    public BlockFace Face => (BlockFace)((_packed >> 12) & 0x07);

    public byte Corner => (byte)((_packed >> 15) & 0x03);
}

public readonly record struct ChunkMeshVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 Uv,
    Vector2 EncodedTextureLayers,
    Vector4 TintAndAo,
    Vector4 VoxelLight,
    ChunkMeshLightSample LightSample = default);

public sealed record ChunkRenderBatchData(
    TerrainRenderBatch Batch,
    ChunkMeshVertex[] Vertices)
{
    public int TriangleCount => Vertices.Length / 3;
}

public sealed record ChunkMeshData(
    ChunkRenderBatchData[] RenderBatches,
    Vector3[] CollisionFaces,
    bool CanRelightWithoutRemesh = false)
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
