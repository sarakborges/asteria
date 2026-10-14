using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Updates immutable fluid-quad vertex light without recomputing fluid
/// heights or faces. A changed light-dependent quad diagonal requires a
/// complete remesh and is reported as an ineligible fast path.
/// </summary>
public static class FluidMeshLightingUpdater
{
    private static readonly byte[] Standard = [0, 2, 1, 0, 3, 2];
    private static readonly byte[] Flipped = [0, 3, 1, 1, 3, 2];
    private static readonly byte[] Reverse = [0, 1, 2, 0, 2, 3];
    private static readonly byte[] ReverseFlipped = [0, 1, 3, 1, 2, 3];

    public static bool TryRelight(
        VoxelWorld world, ChunkCoord coord, BlockRegistry blocks,
        ChunkFluidMeshData geometry, out ChunkFluidMeshData result)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(geometry);

        var (ox, oy, oz) = VoxelCoordinates.ChunkOrigin(coord);
        var batches = new ChunkFluidBatchData[geometry.Batches.Length];
        var sampled = new Dictionary<
            (LocalVoxelCoord Source, BlockFace Face), VoxelFaceLighting>();

        for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            var batch = geometry.Batches[batchIndex];
            if (batch.Vertices.Length % 6 != 0)
            {
                result = geometry;
                return false;
            }

            var vertices = new FluidMeshVertex[batch.Vertices.Length];
            for (var start = 0; start < vertices.Length; start += 6)
            {
                var original = batch.Vertices[start];
                var sample = original.LightSample;
                var key = (sample.Source, sample.Face);
                if (!sampled.TryGetValue(key, out var lighting))
                {
                    var source = new WorldVoxelCoord(
                        ox + sample.Source.X,
                        oy + sample.Source.Y,
                        oz + sample.Source.Z);
                    lighting = VoxelMeshLighting.SampleFace(
                        world, blocks, source, sample.Face);
                    sampled.Add(key, lighting);
                }

                var underside = sample.Face == BlockFace.Top &&
                    original.Normal == -Vector3.UnitY;
                var order = underside
                    ? (lighting.ShouldFlipDiagonal ? ReverseFlipped : Reverse)
                    : (lighting.ShouldFlipDiagonal ? Flipped : Standard);

                for (var corner = 0; corner < 6; corner++)
                {
                    var old = batch.Vertices[start + corner];
                    if (old.LightSample.Source != sample.Source ||
                        old.LightSample.Face != sample.Face ||
                        old.LightSample.Corner != order[corner])
                    {
                        result = geometry;
                        return false;
                    }

                    var light = lighting[order[corner]];
                    vertices[start + corner] = old with
                    {
                        VoxelLight = new Vector4(
                            light.Sky, light.BlockRed,
                            light.BlockGreen, light.BlockBlue),
                    };
                }
            }

            batches[batchIndex] =
                new ChunkFluidBatchData(batch.Fluid, vertices);
        }

        result = new ChunkFluidMeshData(batches);
        return true;
    }
}
