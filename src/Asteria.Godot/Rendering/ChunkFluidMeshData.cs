using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public readonly record struct FluidMeshVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector4 TintAndAo,
    Vector4 VoxelLight);

public sealed record ChunkFluidBatchData(
    FluidRuntimeId Fluid,
    FluidMeshVertex[] Vertices)
{
    public int TriangleCount =>
        Vertices.Length / 3;
}

public sealed record ChunkFluidMeshData(
    ChunkFluidBatchData[] Batches)
{
    public int TriangleCount =>
        Batches.Sum(batch => batch.TriangleCount);

    public bool HasGeometry =>
        Batches.Any(
            batch => batch.Vertices.Length > 0);
}
