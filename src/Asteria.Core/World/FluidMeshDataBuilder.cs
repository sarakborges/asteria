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

    private readonly record struct FluidSurfaceCacheKey(
        WorldVoxelCoord Position,
        FluidRuntimeId Fluid);

    private readonly record struct GreedyFluidTopCandidate(
        FluidRuntimeId Fluid,
        float Height)
    {
        public bool Matches(GreedyFluidTopCandidate other) =>
            Fluid == other.Fluid &&
            BitConverter.SingleToInt32Bits(Height) ==
            BitConverter.SingleToInt32Bits(other.Height);
    }

    /// <summary>
    /// Coalesces only flat, exposed fluid tops inside the current 8x8x8
    /// meshlet. Sloped tops and all side faces keep the existing geometry path.
    /// </summary>
    private sealed class GreedyFluidTopFaceAccumulator
    {
        private readonly (
            int MinX,
            int MinY,
            int MinZ,
            int MaxXExclusive,
            int MaxYExclusive,
            int MaxZExclusive) _bounds;

        private readonly GreedyFluidTopCandidate?[] _cells =
            new GreedyFluidTopCandidate?[
                ChunkMeshletMask.Edge *
                ChunkMeshletMask.Edge *
                ChunkMeshletMask.Edge];

        public GreedyFluidTopFaceAccumulator(int meshletIndex)
        {
            _bounds = ChunkMeshletMask.Bounds(meshletIndex);
        }

        public bool TryAdd(
            int x,
            int y,
            int z,
            FluidRuntimeId fluid,
            FluidFaceHeights heights)
        {
            var heightBits = BitConverter.SingleToInt32Bits(heights.H00);
            if (heightBits != BitConverter.SingleToInt32Bits(heights.H10) ||
                heightBits != BitConverter.SingleToInt32Bits(heights.H11) ||
                heightBits != BitConverter.SingleToInt32Bits(heights.H01))
            {
                return false;
            }

            var localX = x - _bounds.MinX;
            var localY = y - _bounds.MinY;
            var localZ = z - _bounds.MinZ;
            _cells[Index(localX, localY, localZ)] =
                new GreedyFluidTopCandidate(fluid, heights.H00);
            return true;
        }

        public void Emit(
            VoxelWorld world,
            BlockRegistry blocks,
            ChunkCoord coord,
            Dictionary<FluidRuntimeId, List<FluidMeshVertex>> buffers)
        {
            var (originX, originY, originZ) =
                VoxelCoordinates.ChunkOrigin(coord);
            Span<Vector3> points = stackalloc Vector3[4];

            for (var y = 0; y < ChunkMeshletMask.Edge; y++)
            {
                for (var z = 0; z < ChunkMeshletMask.Edge; z++)
                {
                    for (var x = 0; x < ChunkMeshletMask.Edge; x++)
                    {
                        var index = Index(x, y, z);
                        if (_cells[index] is not { } candidate)
                            continue;

                        var width = 1;
                        while (x + width < ChunkMeshletMask.Edge &&
                               Matches(
                                   _cells[Index(x + width, y, z)],
                                   candidate))
                        {
                            width++;
                        }

                        var depth = 1;
                        while (z + depth < ChunkMeshletMask.Edge)
                        {
                            var rowMatches = true;
                            for (var column = x; column < x + width; column++)
                            {
                                if (Matches(
                                        _cells[Index(column, y, z + depth)],
                                        candidate))
                                {
                                    continue;
                                }

                                rowMatches = false;
                                break;
                            }

                            if (!rowMatches)
                                break;

                            depth++;
                        }

                        for (var row = z; row < z + depth; row++)
                        {
                            for (var column = x; column < x + width; column++)
                                _cells[Index(column, y, row)] = null;
                        }

                        if (!buffers.TryGetValue(candidate.Fluid, out var vertices))
                            continue;

                        var x0 = (float)(_bounds.MinX + x);
                        var x1 = x0 + width;
                        var z0 = (float)(_bounds.MinZ + z);
                        var z1 = z0 + depth;
                        var surfaceY = _bounds.MinY + y + candidate.Height;
                        points[0] = new Vector3(x0, surfaceY, z1);
                        points[1] = new Vector3(x1, surfaceY, z1);
                        points[2] = new Vector3(x1, surfaceY, z0);
                        points[3] = new Vector3(x0, surfaceY, z0);

                        var position = new WorldVoxelCoord(
                            originX + _bounds.MinX + x,
                            originY + _bounds.MinY + y,
                            originZ + _bounds.MinZ + z);
                        EmitQuad(
                            vertices,
                            world,
                            blocks,
                            position,
                            BlockFace.Top,
                            points,
                            MeshFaceTopology.Neutral);
                        EmitTopUnderside(
                            vertices,
                            position,
                            points,
                            MeshFaceTopology.Neutral);
                    }
                }
            }
        }

        private static bool Matches(
            GreedyFluidTopCandidate? cell,
            GreedyFluidTopCandidate expected) =>
            cell is { } candidate &&
            candidate.Matches(expected);

        private static int Index(int x, int y, int z) =>
            x +
            z * ChunkMeshletMask.Edge +
            y * ChunkMeshletMask.Edge * ChunkMeshletMask.Edge;
    }

    internal sealed class FluidSurfaceHeightCache
    {
        private readonly VoxelWorld _world;
        private readonly Dictionary<FluidSurfaceCacheKey, FluidFaceHeights> _heights = [];
        private readonly Dictionary<FluidSurfaceCacheKey, float> _cornerHeights = [];
        private readonly Dictionary<
            WorldVoxelCoord, (VoxelCell Cell, FluidCell Fluid)> _samples = [];
        private readonly Dictionary<BlockRuntimeId, bool> _fluidPermeability = [];

        public FluidSurfaceHeightCache(VoxelWorld world)
        {
            _world = world ??
                throw new ArgumentNullException(nameof(world));
        }

        internal (VoxelCell Cell, FluidCell Fluid) GetCellAndFluid(
            WorldVoxelCoord position)
        {
            if (_samples.TryGetValue(position, out var cached))
                return cached;

            _world.TryGetCellAndFluid(
                position, out var cell, out var fluid);
            var sample = (cell, fluid);
            _samples.Add(position, sample);
            return sample;
        }

        internal bool CanContainFluid(VoxelCell cell)
        {
            if (cell.IsEmpty)
                return true;

            if (_fluidPermeability.TryGetValue(cell.Block, out var cached))
                return cached;

            var canContainFluid = _world.CanContainFluid(cell);
            _fluidPermeability.Add(cell.Block, canContainFluid);
            return canContainFluid;
        }

        internal FluidFaceHeights Get(
            WorldVoxelCoord position,
            FluidRuntimeId fluid)
        {
            var key = new FluidSurfaceCacheKey(position, fluid);
            if (_heights.TryGetValue(key, out var cached))
                return cached;

            var heights = CalculateSurfaceHeights(position, fluid);
            _heights.Add(key, heights);
            return heights;
        }

        private FluidFaceHeights CalculateSurfaceHeights(
            WorldVoxelCoord position,
            FluidRuntimeId fluid) =>
            new(
                GetCornerHeight(position, fluid, 0, 0),
                GetCornerHeight(position, fluid, 1, 0),
                GetCornerHeight(position, fluid, 1, 1),
                GetCornerHeight(position, fluid, 0, 1));

        private float GetCornerHeight(
            WorldVoxelCoord position,
            FluidRuntimeId fluid,
            int xDirection,
            int zDirection)
        {
            // A grid corner is shared by up to four adjacent fluid cells.
            // Key by its world coordinate so those cells reuse the same
            // neighborhood sample instead of probing the same voxels again.
            var corner = position + (xDirection, 0, zDirection);
            var key = new FluidSurfaceCacheKey(corner, fluid);
            if (_cornerHeights.TryGetValue(key, out var cached))
                return cached;

            var height = CalculateCornerHeight(
                this,
                position,
                fluid,
                xDirection,
                zDirection);
            _cornerHeights.Add(key, height);
            return height;
        }
    }

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
        if (world.GetChunk(coord).FluidVoxelCountInMeshlet(meshletIndex) == 0)
            return ChunkFluidMeshData.Empty;

        return BuildMeshlet(
            world, coord, blocks, fluids, meshletIndex,
            new FluidSurfaceHeightCache(world));
    }

    internal static ChunkFluidMeshData BuildMeshlet(
        VoxelWorld world,
        ChunkCoord coord,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int meshletIndex,
        FluidSurfaceHeightCache surfaceHeightCache)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(surfaceHeightCache);

        var chunk = world.GetChunk(coord);
        var occupiedFluidCount =
            chunk.FluidVoxelCountInMeshlet(meshletIndex);
        if (occupiedFluidCount == 0)
            return ChunkFluidMeshData.Empty;

        var estimatedVertexCapacity = Math.Min(
            checked(occupiedFluidCount * 36),
            256);
        var buffers =
            new Dictionary<FluidRuntimeId, List<FluidMeshVertex>>();
        var greedyTopFaces = new GreedyFluidTopFaceAccumulator(meshletIndex);
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);
        // Enumerate the packed occupancy bitset rather than scanning all 512
        // coordinates and performing a palette lookup for every empty voxel.
        // The worker snapshot is immutable, so visit order remains stable.
        chunk.VisitFluidCellsInMeshlet(
            meshletIndex,
            (x, y, z, fluid) =>
            {
                var position = new WorldVoxelCoord(
                    originX + x, originY + y, originZ + z);

                // Permeable sprites and their source fluid can coexist.
                // This voxel belongs to the current chunk; no world lookup.
                var cell = chunk.GetCell(x, y, z);
                if (!surfaceHeightCache.CanContainFluid(cell))
                    return;

                if (!buffers.TryGetValue(fluid.Fluid, out var vertices))
                {
                    vertices = new List<FluidMeshVertex>(estimatedVertexCapacity);
                    buffers.Add(fluid.Fluid, vertices);
                }

                var heights = surfaceHeightCache.Get(position, fluid.Fluid);
                foreach (var face in Faces)
                {
                    EmitFaceIfVisible(
                        vertices, world, blocks, position,
                        new Vector3(x, y, z), fluid, heights,
                        surfaceHeightCache, greedyTopFaces, face);
                }
            });

        greedyTopFaces.Emit(world, blocks, coord, buffers);

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
        FluidSurfaceHeightCache surfaceHeightCache,
        GreedyFluidTopFaceAccumulator greedyTopFaces,
        BlockFace face)
    {
        var offset = FaceOffset(face);
        var neighborPosition =
            position + offset;
        // Sample the neighbor's block and fluid with one coordinate/chunk
        // lookup. Missing chunks produce empty data, preserving open borders.
        var (neighborCell, neighborFluid) =
            surfaceHeightCache.GetCellAndFluid(neighborPosition);

        // A decorative sprite must not occlude the adjacent water face.
        if (!surfaceHeightCache.CanContainFluid(neighborCell))
        {
            return;
        }

        if (face == BlockFace.Top)
        {
            if (!neighborFluid.IsEmpty &&
                neighborFluid.Fluid == fluid.Fluid)
            {
                return;
            }

            if (greedyTopFaces.TryAdd(
                    (int)localOrigin.X,
                    (int)localOrigin.Y,
                    (int)localOrigin.Z,
                    fluid.Fluid,
                    heights))
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
                : surfaceHeightCache.Get(
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

        var topology =
            MeshFaceTopology.Neutral;

        EmitQuad(
            vertices,
            world,
            blocks,
            position,
            BlockFace.Top,
            points,
            topology);
        EmitTopUnderside(
            vertices,
            position,
            points,
            topology);
    }

    private static void EmitTopUnderside(
        List<FluidMeshVertex> vertices,
        WorldVoxelCoord position,
        ReadOnlySpan<Vector3> points,
        MeshFaceTopology topology)
    {
        foreach (var index in ReverseTriangleOrder)
            vertices.Add(new FluidMeshVertex(
                points[index], -Vector3.UnitY,
                FaceUv(BlockFace.Top, points[index]), Vector4.One));
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
        ReadOnlySpan<Vector3> points,
        MeshFaceTopology? sampledTopology = null)
    {
        var normal = FaceNormal(face);
        foreach (var index in TriangleOrder)
            vertices.Add(new FluidMeshVertex(
                points[index], normal, FaceUv(face, points[index]), Vector4.One));
    }

    private static Vector2 FaceUv(
        BlockFace face,
        Vector3 point) =>
        face switch
        {
            BlockFace.Top or
            BlockFace.Bottom =>
                new Vector2(
                    point.X,
                    point.Z),
            BlockFace.Right or
            BlockFace.Left =>
                new Vector2(
                    point.Z,
                    point.Y),
            BlockFace.Front or
            BlockFace.Back =>
                new Vector2(
                    point.X,
                    point.Y),
            _ => throw new ArgumentOutOfRangeException(
                nameof(face)),
        };

    private static float CalculateCornerHeight(
        FluidSurfaceHeightCache samples,
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
                samples,
                position,
                fluid) ||
            HasFluidAbove(
                samples,
                xNeighbor,
                fluid) ||
            HasFluidAbove(
                samples,
                zNeighbor,
                fluid) ||
            HasFluidAbove(
                samples,
                diagonal,
                fluid))
        {
            return 1f;
        }

        var total = 0f;
        var count = 0;

        AccumulateSurfaceHeight(
            samples,
            position,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            samples,
            xNeighbor,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            samples,
            zNeighbor,
            fluid,
            ref total,
            ref count);
        AccumulateSurfaceHeight(
            samples,
            diagonal,
            fluid,
            ref total,
            ref count);

        return count == 0
            ? 0f
            : total / count;
    }

    private static bool HasFluidAbove(
        FluidSurfaceHeightCache samples,
        WorldVoxelCoord position,
        FluidRuntimeId fluid)
    {
        var above = samples.GetCellAndFluid(
            position + (0, 1, 0)).Fluid;

        return !above.IsEmpty &&
               above.Fluid == fluid;
    }

    private static void AccumulateSurfaceHeight(
        FluidSurfaceHeightCache samples,
        WorldVoxelCoord position,
        FluidRuntimeId fluid,
        ref float total,
        ref int count)
    {
        var candidate = samples.GetCellAndFluid(position).Fluid;

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

    internal readonly record struct FluidFaceHeights(
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
