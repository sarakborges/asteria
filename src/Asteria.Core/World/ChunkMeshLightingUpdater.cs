using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Recomputes the voxel-light attributes of immutable published geometry.
/// Merged multi-voxel rectangles may remain merged only when each component
/// face retains the same uniform corner lighting. Nonuniform changes cause a
/// safe full-remesh fallback rather than painting one light over the rectangle.
/// Collision, tint, AO, UVs, triangle winding and materials are preserved.
/// </summary>
public static class ChunkMeshLightingUpdater
{
    public static ChunkMeshData Relight(
        VoxelWorld world, ChunkCoord coord,
        BlockRegistry blocks, ChunkMeshData geometry)
    {
        if (!TryRelight(world, coord, blocks, geometry, out var result))
            throw new InvalidOperationException(
                "The existing mesh requires a geometry rebuild after lighting changed.");
        return result;
    }

    public static bool TryRelight(
        VoxelWorld world, ChunkCoord coord,
        BlockRegistry blocks, ChunkMeshData geometry,
        out ChunkMeshData result)
    {
        var (ox, oy, oz) = VoxelCoordinates.ChunkOrigin(coord);
        return TryRelightAtOrigin(
            world,
            new WorldVoxelCoord(ox, oy, oz),
            blocks,
            geometry,
            out result);
    }

    /// <summary>
    /// Relights one portable block mesh at its current world voxel position.
    /// Its geometry remains local to the block, while all light/AO samples
    /// come from the actual resident world surrounding that position.
    /// </summary>
    public static bool TryRelightPortableBlock(
        VoxelWorld world,
        WorldVoxelCoord position,
        BlockRegistry blocks,
        ChunkMeshData geometry,
        out ChunkMeshData result) =>
        TryRelightAtOrigin(world, position, blocks, geometry, out result);

    private static bool TryRelightAtOrigin(
        VoxelWorld world,
        WorldVoxelCoord origin,
        BlockRegistry blocks,
        ChunkMeshData geometry,
        out ChunkMeshData result)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(geometry);
        result = geometry;
        if (!geometry.CanRelightWithoutRemesh)
            return false;

        var sampled = new Dictionary<
            (LocalVoxelCoord Source, BlockFace Face), VoxelFaceLighting>();
        var batches = new ChunkRenderBatchData[geometry.RenderBatches.Length];

        VoxelFaceLighting Sample(LocalVoxelCoord local, BlockFace face)
        {
            var key = (local, face);
            if (!sampled.TryGetValue(key, out var lighting))
            {
                lighting = VoxelMeshLighting.SampleFace(
                    world, blocks, origin + (local.X, local.Y, local.Z), face);
                sampled.Add(key, lighting);
            }
            return lighting;
        }

        for (var b = 0; b < batches.Length; b++)
        {
            var previous = geometry.RenderBatches[b];
            var vertices = new ChunkMeshVertex[previous.Vertices.Length];

            // A quad always contributes exactly six consecutive vertices.
            // Geometry larger than one block on either tangential axis is a
            // greedy rectangle; its source is the lower U/V voxel.
            if (previous.Vertices.Length % 6 != 0)
                return false;
            for (var start = 0; start < vertices.Length; start += 6)
            {
                var first = previous.Vertices[start];
                var sample = first.LightSample;
                var corners = Sample(sample.Source, sample.Face);

                if (!MergedRectangleIsUniform(
                        previous.Vertices, start, sample, corners, Sample))
                    return false;

                for (var i = start; i < start + 6; i++)
                {
                    var old = previous.Vertices[i];
                    var local = old.LightSample;
                    if (local.Source != sample.Source ||
                        local.Face != sample.Face)
                        return false;

                    var light = corners[local.Corner];
                    vertices[i] = old with
                    {
                        VoxelLight = new Vector4(
                            light.Sky, light.BlockRed,
                            light.BlockGreen, light.BlockBlue),
                    };
                }
            }
            batches[b] = new ChunkRenderBatchData(previous.Batch, vertices);
        }

        result = new ChunkMeshData(
            batches, geometry.CollisionFaces,
            CanRelightWithoutRemesh: true);
        return true;
    }

    private static bool MergedRectangleIsUniform(
        ChunkMeshVertex[] vertices,
        int start,
        ChunkMeshLightSample sample,
        VoxelFaceLighting current,
        Func<LocalVoxelCoord, BlockFace, VoxelFaceLighting> sampleAt)
    {
        // All greedy quads are axis-aligned, with integer block widths.
        // Fine/microblock and sprite geometry stays within its voxel.
        var lower = vertices[start].Position;
        var upper = lower;
        for (var i = start + 1; i < start + 6; i++)
        {
            lower = Vector3.Min(lower, vertices[i].Position);
            upper = Vector3.Max(upper, vertices[i].Position);
        }

        var span = upper - lower;
        var width = sample.Face switch
        {
            BlockFace.Right or BlockFace.Left => span.Z,
            _ => span.X,
        };
        var height = sample.Face switch
        {
            BlockFace.Right or BlockFace.Left => span.Y,
            BlockFace.Top or BlockFace.Bottom => span.Z,
            _ => span.Y,
        };
        if (width <= 1.001f && height <= 1.001f)
            return true;

        var countU = (int)MathF.Round(width);
        var countV = (int)MathF.Round(height);
        if (countU is < 1 or > ChunkMeshletMask.Edge ||
            countV is < 1 or > ChunkMeshletMask.Edge ||
            MathF.Abs(width - countU) > 0.001f ||
            MathF.Abs(height - countV) > 0.001f)
            return false;

        // Merged faces were emitted only when all four corner samples of
        // *each* source voxel were equal. Retaining the original rectangle
        // requires that invariant, including cells at its far U/V edges.
        if (!TryUniform(current, out var uniform))
            return false;
        for (var v = 0; v < countV; v++)
        for (var u = 0; u < countU; u++)
        {
            var local = sample.Face switch
            {
                BlockFace.Right or BlockFace.Left =>
                    new LocalVoxelCoord(
                        sample.Source.X, sample.Source.Y + v,
                        sample.Source.Z + u),
                BlockFace.Top or BlockFace.Bottom =>
                    new LocalVoxelCoord(
                        sample.Source.X + u, sample.Source.Y,
                        sample.Source.Z + v),
                _ => new LocalVoxelCoord(
                    sample.Source.X + u, sample.Source.Y + v,
                    sample.Source.Z),
            };
            // Invalid provenance is not a valid reason to sample outside
            // the resident chunk. Falling back retains correct topology.
            if ((uint)local.X >= Chunk.Size ||
                (uint)local.Y >= Chunk.Size ||
                (uint)local.Z >= Chunk.Size)
                return false;

            if (!TryUniform(sampleAt(local, sample.Face), out var value) ||
                value != uniform)
                return false;
        }
        return true;
    }

    private static bool TryUniform(
        VoxelFaceLighting face, out VoxelVertexLighting uniform)
    {
        uniform = face.Corner0;
        return face.Corner1 == uniform &&
               face.Corner2 == uniform &&
               face.Corner3 == uniform;
    }
}
