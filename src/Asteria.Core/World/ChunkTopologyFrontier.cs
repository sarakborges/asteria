namespace Asteria.Core.World;

public readonly record struct ChunkNeighborMeshInvalidation(
    ChunkCoord Neighbor,
    ChunkMeshletMask Terrain,
    ChunkMeshletMask Fluid);

public static class ChunkTopologyFrontier
{
    public static IEnumerable<ChunkNeighborMeshInvalidation>
        PresentedNeighborMeshInvalidationsForAddition(
            ChunkCoord source,
            VoxelWorld world,
            Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(
            world);
        ArgumentNullException.ThrowIfNull(
            isPresented);

        if (!world.TryGetChunk(
                source,
                out var sourceChunk))
        {
            yield break;
        }

        for (var y = -1;
             y <= 1;
             y++)
        {
            for (var z = -1;
                 z <= 1;
                 z++)
            {
                for (var x = -1;
                     x <= 1;
                     x++)
                {
                    if (x == 0 &&
                        y == 0 &&
                        z == 0)
                    {
                        continue;
                    }

                    var neighbor =
                        new ChunkCoord(
                            source.X + x,
                            source.Y + y,
                            source.Z + z);

                    if (!isPresented(
                            neighbor) ||
                        !world.TryGetChunk(
                            neighbor,
                            out var neighborChunk))
                    {
                        continue;
                    }

                    var sourceHasContent =
                        sourceChunk
                            .DependencyBoundaryHasContent(
                                x,
                                y,
                                z);
                    var neighborHasContent =
                        neighborChunk
                            .DependencyBoundaryHasContent(
                                -x,
                                -y,
                                -z);
                    var neighborHasFluid =
                        neighborChunk
                            .DependencyBoundaryHasFluid(
                                -x,
                                -y,
                                -z);
                    var cardinal =
                        Math.Abs(x) +
                        Math.Abs(y) +
                        Math.Abs(z) ==
                        1;
                    var sourceHasCardinalFluid =
                        cardinal &&
                        sourceChunk
                            .DependencyBoundaryHasFluid(
                                x,
                                y,
                                z);
                    var meshlets =
                        ChunkMeshletMask
                            .ForDependencyOffset(
                                -x,
                                -y,
                                -z);
                    var terrain =
                        sourceHasContent &&
                        neighborHasContent
                            ? meshlets
                            : ChunkMeshletMask.None;
                    var fluid =
                        neighborHasFluid &&
                        sourceHasContent ||
                        sourceHasCardinalFluid
                            ? meshlets
                            : ChunkMeshletMask.None;

                    if (terrain.IsEmpty &&
                        fluid.IsEmpty)
                    {
                        continue;
                    }

                    yield return
                        new ChunkNeighborMeshInvalidation(
                            neighbor,
                            terrain,
                            fluid);
                }
            }
        }
    }

    /// <summary>
    /// A retired chunk is absent from the live world. Only geometry belonging
    /// to the surviving neighbor can need a new boundary mesh. Empty
    /// neighbors never need a rebuild, and fluid-free neighbors never need
    /// a fluid rebuild. Lighting remains reconciled separately by residency.
    /// </summary>
    public static IEnumerable<ChunkNeighborMeshInvalidation>
        PresentedNeighborMeshInvalidationsForRetirement(
            ChunkCoord source,
            VoxelWorld world,
            Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(isPresented);

        foreach (var (neighbor, meshlets) in
                 PresentedNeighborMeshlets(source, isPresented))
        {
            if (!world.TryGetChunk(neighbor, out var chunk))
                continue;

            var dx = source.X - neighbor.X;
            var dy = source.Y - neighbor.Y;
            var dz = source.Z - neighbor.Z;
            var terrain =
                chunk.HasTerrainContent &&
                chunk.DependencyBoundaryHasContent(dx, dy, dz)
                    ? meshlets
                    : ChunkMeshletMask.None;
            var fluid =
                chunk.HasFluidContent &&
                chunk.DependencyBoundaryHasFluid(dx, dy, dz)
                    ? meshlets
                    : ChunkMeshletMask.None;

            if (!terrain.IsEmpty || !fluid.IsEmpty)
                yield return new ChunkNeighborMeshInvalidation(
                    neighbor, terrain, fluid);
        }
    }

    public static IEnumerable<(
        ChunkCoord Neighbor,
        ChunkMeshletMask Meshlets)> PresentedNeighborMeshlets(
        ChunkCoord source,
        Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(isPresented);

        for (var y = -1; y <= 1; y++)
        {
            for (var z = -1; z <= 1; z++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    var neighbor = new ChunkCoord(
                        source.X + x,
                        source.Y + y,
                        source.Z + z);

                    if (!isPresented(neighbor))
                    {
                        continue;
                    }

                    yield return (
                        neighbor,
                        ChunkMeshletMask.ForDependencyOffset(
                            -x,
                            -y,
                            -z));
                }
            }
        }
    }
}
