using System.Numerics;

namespace Asteria.Core.World;

public static class FluidMeshDataBuilder
{
    private static readonly int[] TriangleOrder =
        [0, 2, 1, 0, 3, 2];
    private static readonly int[] FlippedTriangleOrder =
        [0, 3, 1, 1, 3, 2];
    private static readonly int[] ReverseTriangleOrder =
        [0, 1, 2, 0, 2, 3];
    private static readonly int[] ReverseFlippedTriangleOrder =
        [0, 1, 3, 1, 2, 3];

    private static readonly BlockFace[] Faces =
    [
        BlockFace.Right,
        BlockFace.Left,
        BlockFace.Top,
        BlockFace.Bottom,
        BlockFace.Front,
        BlockFace.Back,
    ];

    public static ChunkFluidMeshData BuildMeshlet(
        VoxelWorld world,
        ChunkCoord coord,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int meshletIndex)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        var chunk = world.GetChunk(coord);
        var buffers =
            new Dictionary<FluidRuntimeId, List<FluidMeshVertex>>();
        var bounds =
            ChunkMeshletMask.Bounds(meshletIndex);
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        for (var y = bounds.MinY;
             y < bounds.MaxYExclusive;
             y++)
        {
            for (var z = bounds.MinZ;
                 z < bounds.MaxZExclusive;
                 z++)
            {
                for (var x = bounds.MinX;
                     x < bounds.MaxXExclusive;
                     x++)
                {
                    var fluid =
                        chunk.GetFluid(x, y, z);

                    if (fluid.IsEmpty)
                    {
                        continue;
                    }

                    var position =
                        new WorldVoxelCoord(
                            originX + x,
                            originY + y,
                            originZ + z);

                    // A solid block displaces fluid presentation immediately;
                    // the simulation then clears the stale fluid state.
                    if (!world.GetCellOrEmpty(position).IsEmpty)
                    {
                        continue;
                    }

                    if (!buffers.TryGetValue(
                            fluid.Fluid,
                            out var vertices))
                    {
                        vertices =
                            new List<FluidMeshVertex>(256);
                        buffers.Add(
                            fluid.Fluid,
                            vertices);
                    }

                    var heights =
                        SurfaceHeights(
                            world,
                            position,
                            fluid.Fluid);

                    foreach (var face in Faces)
                    {
                        EmitFaceIfVisible(
                            vertices,
                            world,
                            blocks,
                            position,
                            new Vector3(x, y, z),
                            fluid,
                            heights,
                            face);
                    }
                }
            }
        }

        return new ChunkFluidMeshData(
            buffers
                .OrderBy(entry => entry.Key.Value)
                .Select(entry =>
                    new ChunkFluidBatchData(
                        entry.Key,
                        entry.Value.ToArray()))
                .ToArray());
    }

    private static void EmitFaceIfVisible(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        Vector3 localOrigin,
        FluidCell fluid,
        FluidFaceHeights heights,
        BlockFace face)
    {
        var offset = FaceOffset(face);
        var neighborPosition =
            position + offset;
        var neighborBlock =
            world.GetCellOrEmpty(
                neighborPosition);

        if (!neighborBlock.IsEmpty)
        {
            return;
        }

        var neighborFluid =
            world.GetFluidOrEmpty(
                neighborPosition);

        if (face == BlockFace.Top)
        {
            if (!neighborFluid.IsEmpty &&
                neighborFluid.Fluid == fluid.Fluid)
            {
                return;
            }

            EmitTop(
                vertices,
                world,
                blocks,
                position,
                localOrigin,
                heights);
            return;
        }

        if (face == BlockFace.Bottom)
        {
            if ((!neighborFluid.IsEmpty &&
                 neighborFluid.Fluid == fluid.Fluid) ||
                position.Y <= 0)
            {
                return;
            }

            EmitFlatFace(
                vertices,
                world,
                blocks,
                position,
                localOrigin,
                face,
                0f,
                1f);
            return;
        }

        var neighborHeights =
            neighborFluid.IsEmpty ||
            neighborFluid.Fluid != fluid.Fluid
                ? default
                : SurfaceHeights(
                    world,
                    neighborPosition,
                    fluid.Fluid);

        if (!ShouldEmitSide(
                face,
                heights,
                neighborFluid,
                neighborHeights,
                fluid.Fluid))
        {
            return;
        }

        EmitSide(
            vertices,
            world,
            blocks,
            position,
            localOrigin,
            face,
            heights,
            neighborFluid,
            neighborHeights,
            fluid.Fluid);
    }

    private static bool ShouldEmitSide(
        BlockFace face,
        FluidFaceHeights source,
        FluidCell neighbor,
        FluidFaceHeights neighborHeights,
        FluidRuntimeId fluid)
    {
        if (neighbor.IsEmpty ||
            neighbor.Fluid != fluid)
        {
            return true;
        }

        var sourceAverage =
            EdgeAverage(face, source);
        var neighborAverage =
            EdgeAverage(Opposite(face), neighborHeights);

        return sourceAverage >
               neighborAverage + 0.001f;
    }

    private static void EmitTop(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        Vector3 origin,
        FluidFaceHeights heights)
    {
        Span<Vector3> points =
            stackalloc Vector3[4];

        points[0] =
            origin +
            new Vector3(0f, heights.H01, 1f);
        points[1] =
            origin +
            new Vector3(1f, heights.H11, 1f);
        points[2] =
            origin +
            new Vector3(1f, heights.H10, 0f);
        points[3] =
            origin +
            new Vector3(0f, heights.H00, 0f);

        EmitQuad(
            vertices,
            world,
            blocks,
            position,
            BlockFace.Top,
            points);
        EmitTopUnderside(
            vertices,
            world,
            blocks,
            position,
            points);
    }

    private static void EmitTopUnderside(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        ReadOnlySpan<Vector3> points)
    {
        var lighting =
            VoxelMeshLighting.SampleFace(
                world,
                blocks,
                position,
                BlockFace.Top);
        var triangleOrder =
            lighting.ShouldFlipDiagonal
                ? ReverseFlippedTriangleOrder
                : ReverseTriangleOrder;

        foreach (var index in triangleOrder)
        {
            var light = lighting[index];

            vertices.Add(
                new FluidMeshVertex(
                    points[index],
                    -Vector3.UnitY,
                    new Vector4(
                        1f,
                        1f,
                        1f,
                        light.AmbientOcclusion),
                    new Vector4(
                        light.Sky,
                        light.BlockRed,
                        light.BlockGreen,
                        light.BlockBlue)));
        }
    }

    private static void EmitSide(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        Vector3 origin,
        BlockFace face,
        FluidFaceHeights source,
        FluidCell neighbor,
        FluidFaceHeights neighborHeights,
        FluidRuntimeId fluid)
    {
        var (topA, topB) =
            EdgeHeights(face, source);
        var (bottomA, bottomB) =
            neighbor.IsEmpty ||
            neighbor.Fluid != fluid
                ? (0f, 0f)
                : EdgeHeights(
                    Opposite(face),
                    neighborHeights);

        bottomA = MathF.Min(bottomA, topA);
        bottomB = MathF.Min(bottomB, topB);

        Span<Vector3> points =
            stackalloc Vector3[4];

        switch (face)
        {
            case BlockFace.Right:
                points[0] = origin + new Vector3(1f, bottomA, 0f);
                points[1] = origin + new Vector3(1f, topA, 0f);
                points[2] = origin + new Vector3(1f, topB, 1f);
                points[3] = origin + new Vector3(1f, bottomB, 1f);
                break;

            case BlockFace.Left:
                points[0] = origin + new Vector3(0f, bottomA, 1f);
                points[1] = origin + new Vector3(0f, topA, 1f);
                points[2] = origin + new Vector3(0f, topB, 0f);
                points[3] = origin + new Vector3(0f, bottomB, 0f);
                break;

            case BlockFace.Front:
                points[0] = origin + new Vector3(1f, bottomA, 1f);
                points[1] = origin + new Vector3(1f, topA, 1f);
                points[2] = origin + new Vector3(0f, topB, 1f);
                points[3] = origin + new Vector3(0f, bottomB, 1f);
                break;

            case BlockFace.Back:
                points[0] = origin + new Vector3(0f, bottomA, 0f);
                points[1] = origin + new Vector3(0f, topA, 0f);
                points[2] = origin + new Vector3(1f, topB, 0f);
                points[3] = origin + new Vector3(1f, bottomB, 0f);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(face));
        }

        EmitQuad(
            vertices,
            world,
            blocks,
            position,
            face,
            points);
    }

    private static void EmitFlatFace(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        Vector3 origin,
        BlockFace face,
        float minimumHeight,
        float maximumHeight)
    {
        var corners =
            FaceCorners(face);
        Span<Vector3> points =
            stackalloc Vector3[4];

        for (var index = 0; index < 4; index++)
        {
            var corner = corners[index];
            points[index] =
                origin +
                new Vector3(
                    corner.X,
                    corner.Y == 0f
                        ? minimumHeight
                        : maximumHeight,
                    corner.Z);
        }

        EmitQuad(
            vertices,
            world,
            blocks,
            position,
            face,
            points);
    }

    private static void EmitQuad(
        List<FluidMeshVertex> vertices,
        VoxelWorld world,
        BlockRegistry blocks,
        WorldVoxelCoord position,
        BlockFace face,
        ReadOnlySpan<Vector3> points)
    {
        var lighting =
            VoxelMeshLighting.SampleFace(
                world,
                blocks,
                position,
                face);
        var triangleOrder =
            lighting.ShouldFlipDiagonal
                ? FlippedTriangleOrder
                : TriangleOrder;
        var normal =
            FaceNormal(face);

        foreach (var index in triangleOrder)
        {
            var light = lighting[index];
            vertices.Add(
                new FluidMeshVertex(
                    points[index],
                    normal,
                    new Vector4(
                        1f,
                        1f,
                        1f,
                        light.AmbientOcclusion),
                    new Vector4(
                        light.Sky,
                        light.BlockRed,
                        light.BlockGreen,
                        light.BlockBlue)));
        }
    }

    private static FluidFaceHeights SurfaceHeights(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid) =>
        new(
            CornerHeight(world, position, fluid, 0, 0),
            CornerHeight(world, position, fluid, 1, 0),
            CornerHeight(world, position, fluid, 1, 1),
            CornerHeight(world, position, fluid, 0, 1));

    private static float CornerHeight(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid,
        int xDirection,
        int zDirection)
    {
        var x = xDirection == 0 ? -1 : 1;
        var z = zDirection == 0 ? -1 : 1;
        var xNeighbor =
            position + (x, 0, 0);
        var zNeighbor =
            position + (0, 0, z);
        var diagonal =
            position + (x, 0, z);

        if (HasFluidAbove(
                world,
                position,
                fluid) ||
            HasFluidAbove(
                world,
                xNeighbor,
                fluid) ||
            HasFluidAbove(
                world,
                zNeighbor,
                fluid) ||
            HasFluidAbove(
                world,
                diagonal,
                fluid))
        {
            return 1f;
        }

        var total = 0f;
        var count = 0;

        AccumulateSurfaceHeight(
            world,
            position,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            world,
            xNeighbor,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            world,
            zNeighbor,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            world,
            diagonal,
            fluid,
            ref total,
            ref count);

        return count == 0
            ? 0f
            : total / count;
    }

    private static bool HasFluidAbove(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid)
    {
        var above =
            world.GetFluidOrEmpty(
                position + (0, 1, 0));

        return !above.IsEmpty &&
               above.Fluid == fluid;
    }

    private static void AccumulateSurfaceHeight(
        VoxelWorld world,
        WorldVoxelCoord position,
        FluidRuntimeId fluid,
        ref float total,
        ref int count)
    {
        var candidate =
            world.GetFluidOrEmpty(position);

        if (candidate.IsEmpty ||
            candidate.Fluid != fluid)
        {
            return;
        }

        total += candidate.Height;
        count++;
    }

    private static (float A, float B) EdgeHeights(
        BlockFace face,
        FluidFaceHeights heights) =>
        face switch
        {
            BlockFace.Right =>
                (heights.H10, heights.H11),
            BlockFace.Left =>
                (heights.H01, heights.H00),
            BlockFace.Front =>
                (heights.H11, heights.H01),
            BlockFace.Back =>
                (heights.H00, heights.H10),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static float EdgeAverage(
        BlockFace face,
        FluidFaceHeights heights)
    {
        var (a, b) =
            EdgeHeights(face, heights);
        return (a + b) * 0.5f;
    }

    private static BlockFace Opposite(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => BlockFace.Left,
            BlockFace.Left => BlockFace.Right,
            BlockFace.Top => BlockFace.Bottom,
            BlockFace.Bottom => BlockFace.Top,
            BlockFace.Front => BlockFace.Back,
            BlockFace.Back => BlockFace.Front,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static (
        int X,
        int Y,
        int Z) FaceOffset(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => (1, 0, 0),
            BlockFace.Left => (-1, 0, 0),
            BlockFace.Top => (0, 1, 0),
            BlockFace.Bottom => (0, -1, 0),
            BlockFace.Front => (0, 0, 1),
            BlockFace.Back => (0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector3 FaceNormal(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => Vector3.UnitX,
            BlockFace.Left => -Vector3.UnitX,
            BlockFace.Top => Vector3.UnitY,
            BlockFace.Bottom => -Vector3.UnitY,
            BlockFace.Front => Vector3.UnitZ,
            BlockFace.Back => -Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static Vector3[] FaceCorners(
        BlockFace face) =>
        face switch
        {
            BlockFace.Right => FacePositiveX,
            BlockFace.Left => FaceNegativeX,
            BlockFace.Top => FacePositiveY,
            BlockFace.Bottom => FaceNegativeY,
            BlockFace.Front => FacePositiveZ,
            BlockFace.Back => FaceNegativeZ,
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private readonly record struct FluidFaceHeights(
        float H00,
        float H10,
        float H11,
        float H01);

    private static readonly Vector3[] FacePositiveX =
    [
        new(1, 0, 0),
        new(1, 1, 0),
        new(1, 1, 1),
        new(1, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeX =
    [
        new(0, 0, 1),
        new(0, 1, 1),
        new(0, 1, 0),
        new(0, 0, 0),
    ];

    private static readonly Vector3[] FacePositiveY =
    [
        new(0, 1, 1),
        new(1, 1, 1),
        new(1, 1, 0),
        new(0, 1, 0),
    ];

    private static readonly Vector3[] FaceNegativeY =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(1, 0, 1),
        new(0, 0, 1),
    ];

    private static readonly Vector3[] FacePositiveZ =
    [
        new(1, 0, 1),
        new(1, 1, 1),
        new(0, 1, 1),
        new(0, 0, 1),
    ];

    private static readonly Vector3[] FaceNegativeZ =
    [
        new(0, 0, 0),
        new(0, 1, 0),
        new(1, 1, 0),
        new(1, 0, 0),
    ];
}
