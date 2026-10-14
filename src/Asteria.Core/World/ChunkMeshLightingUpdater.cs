using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Relights only the vertices of existing geometry. Collision triangles and
/// every non-light vertex attribute are reused. Callers validate structural
/// dependencies before accepting the result. Merged multi-voxel greedy
/// rectangles must be remeshed when lighting changes.
/// </summary>
public static class ChunkMeshLightingUpdater
{
    public static ChunkMeshData Relight(
        VoxelWorld world, ChunkCoord coord,
        BlockRegistry blocks, ChunkMeshData geometry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(geometry);
        if (!geometry.CanRelightWithoutRemesh)
            throw new InvalidOperationException(
                "The existing mesh geometry cannot be relit safely.");

        var (ox, oy, oz) = VoxelCoordinates.ChunkOrigin(coord);
        var sampled = new Dictionary<
            (LocalVoxelCoord Source, BlockFace Face), VoxelFaceLighting>();
        var batches = new ChunkRenderBatchData[geometry.RenderBatches.Length];
        for (var b = 0; b < batches.Length; b++)
        {
            var previous = geometry.RenderBatches[b];
            var vertices = new ChunkMeshVertex[previous.Vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var original = previous.Vertices[i];
                var sample = original.LightSample;
                var key = (sample.Source, sample.Face);
                if (!sampled.TryGetValue(key, out var corners))
                {
                    var source = new WorldVoxelCoord(
                        ox + sample.Source.X, oy + sample.Source.Y,
                        oz + sample.Source.Z);
                    corners = VoxelMeshLighting.SampleFace(
                        world, blocks, source, sample.Face);
                    sampled.Add(key, corners);
                }

                var value = corners[sample.Corner];
                vertices[i] = original with
                {
                    VoxelLight = new Vector4(
                        value.Sky, value.BlockRed,
                        value.BlockGreen, value.BlockBlue),
                };
            }
            batches[b] = new ChunkRenderBatchData(previous.Batch, vertices);
        }

        return new ChunkMeshData(
            batches, geometry.CollisionFaces, CanRelightWithoutRemesh: true);
    }
}
